#nullable enable

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Text;
using System.Threading;

using UnityEngine;

namespace ULogger {
    [CreateAssetMenu(fileName = "FileLogHandler", menuName = "ULogger/File Log")]
    public sealed class FileLogHandler: ULogHandler, IDisposable {
        [Header("Settings")]
        [Tooltip("You can use special '%pdp' or '%dp' variables as persistentDataPath or DataPath, and '%dt' for datetime")]
        [SerializeField] string path = string.Empty;
        [SerializeField] LogType logLevel = LogType.Log;
        [SerializeField] bool logExceptions = true;
        [Tooltip("Open the log file on enable instead of on the first message. Required if the first log " +
                 "entry may come from a background thread: opening it resolves the path and subscribes to " +
                 "Unity events, both of which are main-thread only.")]
        [SerializeField] bool openWriterOnEnable;

        [Header("Formatting")]
        [Tooltip("Supported tokens: yyyy yy MM dd HH hh mm ss fff ff f. Everything else is a literal. Empty = no timestamp.")]
        [SerializeField] string appendTimeFormat = "yyyy-MM-dd HH:mm:ss.fff";
        [SerializeField] string tagFormat = "[{0}] \"{1}\"";
        [SerializeField] bool appendLogLevel = true;
        [Tooltip("Replace CR/LF inside messages with spaces so one log entry is always one line.")]
        [SerializeField] bool flattenMultiline = true;

        [Header("Limits")]
        [Tooltip("Messages longer than this (in bytes) are truncated.")]
        [SerializeField] int maxMessageBytes = 16 * 1024;
        [Tooltip("Size of each of the writer's two buffers, in bytes. Total burst capacity is twice this. " +
                 "Keep it well above Max Message Bytes, otherwise bursts will be dropped.")]
        [SerializeField] int bufferBytes = 256 * 1024;

        string? _path;
        AsyncFileWriter? _writer;
        readonly object _writerLock = new();

        const int MinBufferBytes = 4096;
        const int MinBufferToMessageRatio = 8;

        TimePart[]? _timeParts;
        byte[]? _timeLiterals;
        byte[][]? _levelLabels;

        TimeSpan _utcOffset;
        long _utcOffsetStamp;

        [ThreadStatic] static ByteBuffer? _scratch;

        static readonly UTF8Encoding Utf8NoBom = new(false);


        string Path {
            get {
                if (!string.IsNullOrEmpty(_path)) return _path!;
                var p = path;
                if (p.StartsWith("%pdp", StringComparison.Ordinal)) p = Application.persistentDataPath + p[4..];
                else if (p.StartsWith("%dp", StringComparison.Ordinal)) p = Application.dataPath + p[3..];
                if (p.Contains("%dt")) p = p.Replace("%dt", DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss"));

                _path = p;
                return _path;
            }
        }

        protected override object DedupScope => Path;

        AsyncFileWriter Writer {
            get {
                var existing = Volatile.Read(ref _writer);
                if (existing is not null) return existing;

                lock (_writerLock) {
                    if (_writer is not null) return _writer;

                    var created = new AsyncFileWriter(Path, Math.Max(bufferBytes, MinBufferBytes));
                    Application.quitting += CloseWriter;
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
                    Volatile.Write(ref _writer, created);
                    return created;
                }
            }
        }

        void CloseWriter() {
            lock (_writerLock) {
                Application.quitting -= CloseWriter;
#if UNITY_EDITOR
                UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
                _path = null;
                if (_writer is null) return;

                var writer = _writer;
                Volatile.Write(ref _writer, null);
                writer.Dispose();
            }
        }

#if UNITY_EDITOR
        void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state) {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode) CloseWriter();
        }
#endif

        // ------------------------------------------------------------------ lifecycle

        void OnEnable() {
            CompileTimeFormat();
            CompileLevelLabels();
            RefreshUtcOffset();
            _ = Path;
            // Edit mode fires OnEnable on asset load; do not create a log file just for that.
            if (openWriterOnEnable && Application.isPlaying) _ = Writer;
        }

        void OnValidate() {
            if (bufferBytes < MinBufferBytes) bufferBytes = MinBufferBytes;
            if (maxMessageBytes < 0) maxMessageBytes = 0;
            WarnOnBufferSizing();

            CompileTimeFormat();
            CompileLevelLabels();
            CloseWriter();
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        void WarnOnBufferSizing() {
            if (maxMessageBytes <= 0) return;

            if (maxMessageBytes >= bufferBytes) {
                Debug.LogWarning(
                    $"[ULogger] {name}: Max Message Bytes ({maxMessageBytes}) is not smaller than Buffer Bytes " +
                    $"({bufferBytes}). A single message can fill an entire buffer, so entries will be dropped " +
                    "under load. Raise Buffer Bytes.", this);
            } else if (maxMessageBytes * (long)MinBufferToMessageRatio > bufferBytes) {
                Debug.LogWarning(
                    $"[ULogger] {name}: Buffer Bytes ({bufferBytes}) holds fewer than {MinBufferToMessageRatio} " +
                    $"messages of Max Message Bytes ({maxMessageBytes}). Bursts are likely to be dropped; " +
                    $"consider at least {maxMessageBytes * (long)MinBufferToMessageRatio} bytes.", this);
            }
        }

        void OnDisable() => CloseWriter();

        void IDisposable.Dispose() => CloseWriter();

        protected override bool LogFormatInherit(LogType logType, UnityEngine.Object? context, string format, params object[] args) {
            if (logType != LogType.Exception && logType > logLevel) return false;

            var b = BeginEntry(logType);

            if (args.Length == 0) {
                WriteText(b, format.AsSpan());
            } else if (args.Length > 1 && !string.IsNullOrEmpty(tagFormat)) {
                WriteFormat(b, logType != LogType.Exception ? tagFormat : format, args);
            } else if (logType != LogType.Exception) {
                b.Write((byte)'"');
                WriteValue(b, args[0]);
                b.Write((byte)'"');
            } else {
                WriteValue(b, args[^1]);
            }

            EndEntry(b, logType is LogType.Error or LogType.Assert or LogType.Exception);
            return true;
        }

        ByteBuffer BeginEntry(LogType logType) {
            var b = _scratch ??= new ByteBuffer(4096);
            b.Length = 0;

            if (_timeParts is { Length: > 0 }) WriteTimestamp(b);

            if (appendLogLevel) {
                var labels = _levelLabels ??= BuildLevelLabels();
                var idx = (int)logType;
                b.Write((uint)idx < (uint)labels.Length ? labels[idx] : labels[(int)LogType.Log]);
            }

            return b;
        }

        void EndEntry(ByteBuffer b, bool urgent) {
            Truncate(b);
            if (b.Length == 0 || b.Data[b.Length - 1] != (byte)'\n') b.Write((byte)'\n');
            Writer.Enqueue(new ReadOnlySpan<byte>(b.Data, 0, b.Length), urgent);
        }

        protected override void LogExceptionInherit(Exception exception, UnityEngine.Object? context) {
            if (!logExceptions) return;

            // ToString() is the one allocation we cannot avoid: it builds the message and the
            // stack trace. Everything past it is sliced as spans straight into the byte buffer.
            var full = exception.ToString().AsSpan();
            var b = BeginEntry(LogType.Exception);

            var colonIndex = full.IndexOf(':');
            if (colonIndex < 0) {
                WriteAscii(b, "[Exception] ");
                WriteText(b, full);
                EndEntry(b, urgent: true);
                return;
            }

            var rest = full[(colonIndex + 1)..].TrimStart();
            var atIndex = rest.IndexOf("   at ".AsSpan(), StringComparison.Ordinal);
            if (atIndex < 0) atIndex = rest.IndexOf(" at ".AsSpan(), StringComparison.Ordinal);

            b.Write((byte)'[');
            WriteText(b, full[..colonIndex]);
            WriteAscii(b, "] \"");

            if (atIndex >= 0) {
                WriteText(b, rest[..atIndex].Trim());
                WriteAscii(b, "\" ");
                WriteText(b, rest[atIndex..].Trim());
            } else {
                WriteText(b, rest.Trim());
                b.Write((byte)'"');
            }

            EndEntry(b, urgent: true);
        }

        void WriteFormat(ByteBuffer b, string format, object[] args) {
            var i = 0;
            var literalStart = 0;

            while (i < format.Length) {
                var c = format[i];

                if (c == '{' && i + 1 < format.Length) {
                    if (format[i + 1] == '{') {
                        WriteText(b, format.AsSpan(literalStart, i - literalStart));
                        b.Write((byte)'{');
                        i += 2;
                        literalStart = i;
                        continue;
                    }

                    var j = i + 1;
                    var index = 0;
                    var hasDigits = false;
                    while (j < format.Length && format[j] is >= '0' and <= '9') {
                        index = index * 10 + (format[j] - '0');
                        j++;
                        hasDigits = true;
                    }

                    if (hasDigits && j < format.Length && format[j] == '}') {
                        WriteText(b, format.AsSpan(literalStart, i - literalStart));
                        if (index < args.Length) WriteValue(b, args[index]);
                        i = j + 1;
                        literalStart = i;
                        continue;
                    }
                } else if (c == '}' && i + 1 < format.Length && format[i + 1] == '}') {
                    WriteText(b, format.AsSpan(literalStart, i - literalStart));
                    b.Write((byte)'}');
                    i += 2;
                    literalStart = i;
                    continue;
                }

                i++;
            }

            WriteText(b, format.AsSpan(literalStart, format.Length - literalStart));
        }

        void WriteValue(ByteBuffer b, object? value) {
            switch (value) {
                case null: return;
                case string s: WriteText(b, s.AsSpan()); return;
                case int v: WriteInt(b, v); return;
                case long v: WriteInt(b, v); return;
                case uint v: WriteInt(b, v); return;
                case ulong v: WriteUInt(b, v); return;
                case short v: WriteInt(b, v); return;
                case ushort v: WriteInt(b, v); return;
                case byte v: WriteInt(b, v); return;
                case sbyte v: WriteInt(b, v); return;
                case bool v: WriteAscii(b, v ? "True" : "False"); return;
                case float v: WriteFloat(b, v); return;
                case double v: WriteFloat(b, v); return;
                case decimal v: WriteDecimal(b, v); return;
                case char v: WriteChar(b, v); return;
                // Enum.ToString() goes through reflection and allocates; print the numeric value.
                case Enum v: WriteEnum(b, v); return;
                default: WriteText(b, value.ToString().AsSpan()); return;
            }
        }

        void WriteChar(ByteBuffer b, char value) {
            Span<char> one = stackalloc char[1];
            one[0] = value;
            WriteText(b, one);
        }

        static void WriteEnum(ByteBuffer b, Enum value) {
            if (Type.GetTypeCode(value.GetType()) == TypeCode.UInt64) WriteUInt(b, Convert.ToUInt64(value));
            else WriteInt(b, Convert.ToInt64(value));
        }

        void WriteText(ByteBuffer b, ReadOnlySpan<char> s) {
            if (s.Length == 0) return;

            // Never grow the scratch buffer far past the limit: Truncate would throw the tail away
            // anyway, but the enlarged array would stay alive on this thread forever. One extra char
            // is kept so Truncate still sees an overflow and appends its marker.
            if (maxMessageBytes > 0) {
                if (b.Length >= maxMessageBytes) return;
                var room = maxMessageBytes - b.Length;
                if (s.Length > room) s = s[..(room + 1)];
            }

            b.Ensure(s.Length * 3);
            var data = b.Data;
            var p = b.Length;
            var flatten = flattenMultiline;

            var i = 0;
            for (; i < s.Length; i++) {
                var c = s[i];
                if (c > 0x7F) break;                                  // ASCII fast path
                data[p++] = flatten && c is '\n' or '\r' ? (byte)' ' : (byte)c;
            }
            b.Length = p;

            if (i >= s.Length) return;

            // Non-ASCII tail. Slicing at i is safe: a surrogate pair always starts here.
            var start = b.Length;
            var written = Utf8NoBom.GetBytes(s[i..], new Span<byte>(b.Data, start, b.Data.Length - start));
            b.Length += written;

            if (!flatten) return;
            // Multi-byte UTF-8 sequences never contain bytes < 0x80, so this is safe.
            var tail = b.Data;
            for (var k = start; k < b.Length; k++)
                if (tail[k] is (byte)'\n' or (byte)'\r') tail[k] = (byte)' ';
        }

        static void WriteAscii(ByteBuffer b, string s) {
            b.Ensure(s.Length);
            var data = b.Data;
            var p = b.Length;
            for (var i = 0; i < s.Length; i++) data[p++] = (byte)s[i];
            b.Length = p;
        }

        static void WriteInt(ByteBuffer b, long value) {
            b.Ensure(20);
            Utf8Formatter.TryFormat(value, new Span<byte>(b.Data, b.Length, b.Data.Length - b.Length), out var written);
            b.Length += written;
        }

        static void WriteUInt(ByteBuffer b, ulong value) {
            b.Ensure(20);
            Utf8Formatter.TryFormat(value, new Span<byte>(b.Data, b.Length, b.Data.Length - b.Length), out var written);
            b.Length += written;
        }

        static void WriteFloat(ByteBuffer b, double value) {
            b.Ensure(32);
            if (Utf8Formatter.TryFormat(value, new Span<byte>(b.Data, b.Length, b.Data.Length - b.Length), out var written))
                b.Length += written;
            else
                WriteAscii(b, "NaN");
        }

        static void WriteDecimal(ByteBuffer b, decimal value) {
            b.Ensure(32);
            if (Utf8Formatter.TryFormat(value, new Span<byte>(b.Data, b.Length, b.Data.Length - b.Length), out var written))
                b.Length += written;
            else
                WriteAscii(b, "NaN");
        }

        void Truncate(ByteBuffer b) {
            if (maxMessageBytes <= 0 || b.Length <= maxMessageBytes) return;

            var cut = maxMessageBytes;
            // do not split a UTF-8 sequence
            while (cut > 0 && (b.Data[cut] & 0xC0) == 0x80) cut--;
            b.Length = cut;
            WriteAscii(b, "...[truncated]");
        }

        enum TimeToken: byte { Literal, Year4, Year2, Month2, Day2, Hour24, Hour12, Minute2, Second2, Milli3, Milli2, Milli1 }

        readonly struct TimePart {
            public readonly TimeToken Token;
            public readonly int Start;
            public readonly int Length;
            public TimePart(TimeToken token, int start = 0, int length = 0) { Token = token; Start = start; Length = length; }
        }

        void WriteTimestamp(ByteBuffer b) {
            var parts = _timeParts!;
            var literals = _timeLiterals!;

            var utc = DateTime.UtcNow;
            if (utc.Ticks - _utcOffsetStamp > TimeSpan.TicksPerMinute * 10) RefreshUtcOffset(utc);
            var now = utc + _utcOffset;

            b.Ensure(64 + literals.Length);
            var data = b.Data;
            var p = b.Length;

            for (var i = 0; i < parts.Length; i++) {
                var part = parts[i];
                switch (part.Token) {
                    case TimeToken.Literal:
                        Buffer.BlockCopy(literals, part.Start, data, p, part.Length);
                        p += part.Length;
                        break;
                    case TimeToken.Year4: p += W4(data, p, now.Year); break;
                    case TimeToken.Year2: p += W2(data, p, now.Year % 100); break;
                    case TimeToken.Month2: p += W2(data, p, now.Month); break;
                    case TimeToken.Day2: p += W2(data, p, now.Day); break;
                    case TimeToken.Hour24: p += W2(data, p, now.Hour); break;
                    case TimeToken.Hour12: p += W2(data, p, now.Hour % 12 == 0 ? 12 : now.Hour % 12); break;
                    case TimeToken.Minute2: p += W2(data, p, now.Minute); break;
                    case TimeToken.Second2: p += W2(data, p, now.Second); break;
                    case TimeToken.Milli3: p += W3(data, p, now.Millisecond); break;
                    case TimeToken.Milli2: p += W2(data, p, now.Millisecond / 10); break;
                    case TimeToken.Milli1: data[p++] = (byte)('0' + now.Millisecond / 100); break;
                }
            }

            data[p++] = (byte)' ';
            b.Length = p;
        }

        void RefreshUtcOffset(DateTime? utcNow = null) {
            var utc = utcNow ?? DateTime.UtcNow;
            _utcOffset = TimeZoneInfo.Local.GetUtcOffset(utc);
            _utcOffsetStamp = utc.Ticks;
        }

        static int W2(byte[] d, int p, int v) {
            d[p] = (byte)('0' + v / 10 % 10);
            d[p + 1] = (byte)('0' + v % 10);
            return 2;
        }

        static int W3(byte[] d, int p, int v) {
            d[p] = (byte)('0' + v / 100 % 10);
            d[p + 1] = (byte)('0' + v / 10 % 10);
            d[p + 2] = (byte)('0' + v % 10);
            return 3;
        }

        static int W4(byte[] d, int p, int v) {
            d[p] = (byte)('0' + v / 1000 % 10);
            d[p + 1] = (byte)('0' + v / 100 % 10);
            d[p + 2] = (byte)('0' + v / 10 % 10);
            d[p + 3] = (byte)('0' + v % 10);
            return 4;
        }

        void CompileTimeFormat() {
            _timeParts = null;
            _timeLiterals = null;
            if (string.IsNullOrEmpty(appendTimeFormat)) return;

            var parts = new List<TimePart>();
            var literals = new List<byte>();
            var f = appendTimeFormat;
            var i = 0;

            while (i < f.Length) {
                var token = TimeToken.Literal;
                var len = 0;

                if (Match(f, i, "yyyy")) { token = TimeToken.Year4; len = 4; } else if (Match(f, i, "yy")) { token = TimeToken.Year2; len = 2; } else if (Match(f, i, "MM")) { token = TimeToken.Month2; len = 2; } else if (Match(f, i, "dd")) { token = TimeToken.Day2; len = 2; } else if (Match(f, i, "HH")) { token = TimeToken.Hour24; len = 2; } else if (Match(f, i, "hh")) { token = TimeToken.Hour12; len = 2; } else if (Match(f, i, "mm")) { token = TimeToken.Minute2; len = 2; } else if (Match(f, i, "ss")) { token = TimeToken.Second2; len = 2; } else if (Match(f, i, "fff")) { token = TimeToken.Milli3; len = 3; } else if (Match(f, i, "ff")) { token = TimeToken.Milli2; len = 2; } else if (Match(f, i, "f")) { token = TimeToken.Milli1; len = 1; }

                if (token != TimeToken.Literal) {
                    parts.Add(new TimePart(token));
                    i += len;
                    continue;
                }

                var start = literals.Count;
                literals.AddRange(Utf8NoBom.GetBytes(f[i].ToString()));
                var added = literals.Count - start;

                if (parts.Count > 0 && parts[^1].Token == TimeToken.Literal) {
                    var prev = parts[^1];
                    parts[^1] = new TimePart(TimeToken.Literal, prev.Start, prev.Length + added);
                } else {
                    parts.Add(new TimePart(TimeToken.Literal, start, added));
                }
                i++;
            }

            _timeParts = parts.ToArray();
            _timeLiterals = literals.ToArray();
        }

        static bool Match(string s, int at, string token) {
            if (at + token.Length > s.Length) return false;
            for (var i = 0; i < token.Length; i++)
                if (s[at + i] != token[i]) return false;
            return true;
        }

        void CompileLevelLabels() => _levelLabels = BuildLevelLabels();

        static byte[][] BuildLevelLabels() {
            return new[] {
                Encoding.ASCII.GetBytes("ERROR\t"),
                Encoding.ASCII.GetBytes("ASSERT\t"),
                Encoding.ASCII.GetBytes("WARNING\t"),
                Encoding.ASCII.GetBytes("INFO\t"),
                Encoding.ASCII.GetBytes("FATAL\t"),
            };
        }

        sealed class ByteBuffer {
            public byte[] Data;
            public int Length;

            public ByteBuffer(int capacity) => Data = new byte[capacity];

            public void Ensure(int extra) {
                var required = Length + extra;
                if (required <= Data.Length) return;
                var size = Data.Length;
                while (size < required) size <<= 1;
                Array.Resize(ref Data, size);
            }

            public void Write(byte value) {
                Ensure(1);
                Data[Length++] = value;
            }

            public void Write(byte[] bytes) {
                Ensure(bytes.Length);
                Buffer.BlockCopy(bytes, 0, Data, Length, bytes.Length);
                Length += bytes.Length;
            }
        }
    }
}
