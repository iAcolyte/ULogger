#nullable enable

using System;
using System.Globalization;

using Unity.Collections.LowLevel.Unsafe;

namespace ULogger {
    /// <summary>Growable per-thread char scratch used to build an entry before it reaches a sink.</summary>
    public sealed class CharBuffer {
        const int MaxRetained = 64 * 1024;

        public char[] Data;
        public int Length;

        public CharBuffer(int capacity) => Data = new char[capacity];

        public ReadOnlySpan<char> Span => new(Data, 0, Length);

        public void Ensure(int extra) {
            var required = Length + extra;
            if (required <= Data.Length) return;

            var size = Data.Length;
            while (size < required) size *= 2;
            Array.Resize(ref Data, size);
        }

        public CharBuffer Append(char c) {
            Ensure(1);
            Data[Length++] = c;
            return this;
        }

        public CharBuffer Append(ReadOnlySpan<char> s) {
            if (s.Length == 0) return this;
            Ensure(s.Length);
            s.CopyTo(new Span<char>(Data, Length, Data.Length - Length));
            Length += s.Length;
            return this;
        }

        // Without it a string literal would bind to Append<string> -- an identity conversion beats
        // the user-defined one to ReadOnlySpan -- and take the slower object path.
        public CharBuffer Append(string? s) => s == null ? this : Append(s.AsSpan());

        /// <summary>Appends a value exactly as a "{0}" placeholder would print it.</summary>
        public CharBuffer Append<T>(T value) {
            LogFormatter.Write(this, value, default);
            return this;
        }

        /// <summary>Appends a value exactly as a "{0:spec}" placeholder would print it.</summary>
        public CharBuffer Append<T>(T value, ReadOnlySpan<char> spec) {
            LogFormatter.Write(this, value, spec);
            return this;
        }

        /// <summary>Resets for a new entry, shrinking back if one huge message blew the buffer up.</summary>
        public void Reset() {
            Length = 0;
            if (Data.Length > MaxRetained) Data = new char[MaxRetained];
        }
    }

    /// <summary>
    /// Turns "{0}" / "{0:F3}"-style formats and their arguments into chars without boxing. Numbers are
    /// always formatted in the invariant culture: a log is parsed by tools, and "0,5" on a machine
    /// with a Russian locale breaks every one of them.
    /// </summary>
    public static class LogFormatter {
        static readonly NumberFormatInfo Invariant = NumberFormatInfo.InvariantInfo;

        [ThreadStatic] static CharBuffer? scratch;

        public static CharBuffer Scratch {
            get {
                var b = scratch ??= new CharBuffer(1024);
                b.Reset();
                return b;
            }
        }

        // ------------------------------------------------------------------ values

        public static void Write<T>(CharBuffer b, T value) => Write(b, value, default);

        /// <summary>
        /// Writes a value of a statically known type. The typeof(T) comparisons are constants once
        /// the method is instantiated, so each instantiation collapses to a single branch; the
        /// reinterpret avoids the box that "(int)(object)value" would still perform at runtime.
        /// <paramref name="spec"/> is any .NET format string; bool, char, strings and enums ignore it.
        /// </summary>
        public static void Write<T>(CharBuffer b, T value, ReadOnlySpan<char> spec) {
            if (typeof(T) == typeof(int) || typeof(T) == typeof(long) || typeof(T) == typeof(short) ||
                typeof(T) == typeof(sbyte) || typeof(T) == typeof(uint) || typeof(T) == typeof(ulong) ||
                typeof(T) == typeof(ushort) || typeof(T) == typeof(byte) || typeof(T) == typeof(float) ||
                typeof(T) == typeof(double) || typeof(T) == typeof(decimal)) {
                WriteNumber(b, ref value, spec);
                return;
            }
            if (typeof(T) == typeof(bool)) { b.Append(UnsafeUtility.As<T, bool>(ref value) ? "True" : "False"); return; }
            if (typeof(T) == typeof(char)) { b.Append(UnsafeUtility.As<T, char>(ref value)); return; }

            WriteObject(b, value, spec);
        }

        static void WriteObject<T>(CharBuffer b, T value, ReadOnlySpan<char> spec) {
            // Reference types box for free (they are already references); only the fallback
            // ToString() on a struct allocates, and there is no way around it in C# 9.
            switch (value) {
                case null: return;
                case string s: b.Append(s.AsSpan()); return;
                case Enum e: WriteEnum(b, e); return;
                // Vector3, DateTime, TimeSpan and the like: honour the spec, and stay invariant
                // even without one, the same as the numbers above.
                case IFormattable f: b.Append(FormatInvariant(f, spec).AsSpan()); return;
                // A ToString() override is free to return null; treat that as an empty value
                // rather than throwing from inside the logger.
                default: b.Append((value!.ToString() ?? string.Empty).AsSpan()); return;
            }
        }

        static string FormatInvariant(IFormattable value, ReadOnlySpan<char> spec) {
            try {
                return value.ToString(spec.Length == 0 ? null : new string(spec), CultureInfo.InvariantCulture) ?? string.Empty;
            } catch (FormatException) {
                // A bad spec must not take the caller down; the value is still worth printing.
                return value.ToString() ?? string.Empty;
            }
        }

        // The one place a name is worth an allocation: a log line reading "3" instead of "Warning"
        // is not a log line. Convert.ToInt64 on an Enum is not free either -- it boxes and goes
        // through IConvertible -- so the numeric shortcut was buying less than it appeared to.
        static void WriteEnum(CharBuffer b, Enum value) => b.Append(value.ToString().AsSpan());

        static void WriteNumber<T>(CharBuffer b, ref T value, ReadOnlySpan<char> spec) {
            try {
                WriteNumberCore(b, ref value, spec);
            } catch (FormatException) when (spec.Length > 0) {
                // "{0:Q}" throws rather than failing softly. Printing the plain value keeps the
                // entry readable; b.Length only moves on success, so nothing half-written is left.
                WriteNumberCore(b, ref value, default);
            }
        }

        // TryFormat only fails for want of room, so grow and retry. Appending "NaN" on failure,
        // as this used to, reported a value the caller never passed; and with a spec such as "N0"
        // no fixed reserve is ever enough.
        static void WriteNumberCore<T>(CharBuffer b, ref T value, ReadOnlySpan<char> spec) {
            for (var extra = 32; ; extra *= 2) {
                b.Ensure(extra);
                if (!TryFormat(ref value, new Span<char>(b.Data, b.Length, b.Data.Length - b.Length), out var written, spec)) continue;
                b.Length += written;
                return;
            }
        }

        // Each type is formatted as itself, never widened: (double)0.1f is 0.10000000149011612,
        // and (long)(-1) under "X" is sixteen F's where the int has eight.
        static bool TryFormat<T>(ref T value, Span<char> dst, out int written, ReadOnlySpan<char> spec) {
            if (typeof(T) == typeof(int)) return UnsafeUtility.As<T, int>(ref value).TryFormat(dst, out written, spec, Invariant);
            if (typeof(T) == typeof(long)) return UnsafeUtility.As<T, long>(ref value).TryFormat(dst, out written, spec, Invariant);
            if (typeof(T) == typeof(short)) return UnsafeUtility.As<T, short>(ref value).TryFormat(dst, out written, spec, Invariant);
            if (typeof(T) == typeof(sbyte)) return UnsafeUtility.As<T, sbyte>(ref value).TryFormat(dst, out written, spec, Invariant);
            if (typeof(T) == typeof(uint)) return UnsafeUtility.As<T, uint>(ref value).TryFormat(dst, out written, spec, Invariant);
            if (typeof(T) == typeof(ulong)) return UnsafeUtility.As<T, ulong>(ref value).TryFormat(dst, out written, spec, Invariant);
            if (typeof(T) == typeof(ushort)) return UnsafeUtility.As<T, ushort>(ref value).TryFormat(dst, out written, spec, Invariant);
            if (typeof(T) == typeof(byte)) return UnsafeUtility.As<T, byte>(ref value).TryFormat(dst, out written, spec, Invariant);
            if (typeof(T) == typeof(float)) return UnsafeUtility.As<T, float>(ref value).TryFormat(dst, out written, spec, Invariant);
            if (typeof(T) == typeof(double)) return UnsafeUtility.As<T, double>(ref value).TryFormat(dst, out written, spec, Invariant);
            if (typeof(T) == typeof(decimal)) return UnsafeUtility.As<T, decimal>(ref value).TryFormat(dst, out written, spec, Invariant);
            throw new NotSupportedException(typeof(T).FullName);
        }

        // ------------------------------------------------------------------ format scanning

        /// <summary>
        /// Copies literal text up to the next "{n}" or "{n:spec}" placeholder and returns n, or -1 at
        /// the end of the format. The spec is a slice of the format, so nothing is allocated.
        /// "{{" and "}}" are literal braces; anything else malformed is copied verbatim.
        /// </summary>
        static int Next(CharBuffer b, string format, ref int pos, out ReadOnlySpan<char> spec) {
            spec = default;
            while (pos < format.Length) {
                var c = format[pos];

                if (c == '}') {
                    pos++;
                    if (pos < format.Length && format[pos] == '}') pos++;
                    b.Append('}');
                    continue;
                }
                if (c != '{') {
                    b.Append(c);
                    pos++;
                    continue;
                }

                if (pos + 1 < format.Length && format[pos + 1] == '{') {
                    b.Append('{');
                    pos += 2;
                    continue;
                }
                if (pos + 2 < format.Length) {
                    var digit = format[pos + 1];
                    if (digit >= '0' && digit <= '9') {
                        if (format[pos + 2] == '}') {
                            pos += 3;
                            return digit - '0';
                        }
                        if (format[pos + 2] == ':') {
                            var close = SpecEnd(format, pos + 3);
                            if (close >= 0) {
                                spec = format.AsSpan(pos + 3, close - (pos + 3));
                                pos = close + 1;
                                return digit - '0';
                            }
                        }
                    }
                }

                b.Append('{');
                pos++;
            }
            return -1;
        }

        // The closing brace of a spec, or -1 if an opening one comes first: "{0:F3 {1}" is a
        // missing brace, not a spec reading "F3 {1".
        static int SpecEnd(string format, int from) {
            for (var i = from; i < format.Length; i++) {
                if (format[i] == '}') return i;
                if (format[i] == '{') return -1;
            }
            return -1;
        }

        // Placeholder index past the supplied arguments: keep it visible instead of silently
        // dropping it, the way string.Format would throw.
        static void Missing(CharBuffer b, int index, ReadOnlySpan<char> spec) {
            b.Append('{').Append((char)('0' + index));
            if (spec.Length > 0) b.Append(':').Append(spec);
            b.Append('}');
        }

        public static void Format(CharBuffer b, string format) {
            var pos = 0;
            int idx;
            while ((idx = Next(b, format, ref pos, out var spec)) >= 0) Missing(b, idx, spec);
        }

        public static void Format<T0>(CharBuffer b, string format, T0 a0) {
            var pos = 0;
            int idx;
            while ((idx = Next(b, format, ref pos, out var spec)) >= 0) {
                if (idx == 0) Write(b, a0, spec);
                else Missing(b, idx, spec);
            }
        }

        public static void Format<T0, T1>(CharBuffer b, string format, T0 a0, T1 a1) {
            var pos = 0;
            int idx;
            while ((idx = Next(b, format, ref pos, out var spec)) >= 0) {
                switch (idx) {
                    case 0: Write(b, a0, spec); break;
                    case 1: Write(b, a1, spec); break;
                    default: Missing(b, idx, spec); break;
                }
            }
        }

        public static void Format<T0, T1, T2>(CharBuffer b, string format, T0 a0, T1 a1, T2 a2) {
            var pos = 0;
            int idx;
            while ((idx = Next(b, format, ref pos, out var spec)) >= 0) {
                switch (idx) {
                    case 0: Write(b, a0, spec); break;
                    case 1: Write(b, a1, spec); break;
                    case 2: Write(b, a2, spec); break;
                    default: Missing(b, idx, spec); break;
                }
            }
        }

        public static void Format<T0, T1, T2, T3>(CharBuffer b, string format, T0 a0, T1 a1, T2 a2, T3 a3) {
            var pos = 0;
            int idx;
            while ((idx = Next(b, format, ref pos, out var spec)) >= 0) {
                switch (idx) {
                    case 0: Write(b, a0, spec); break;
                    case 1: Write(b, a1, spec); break;
                    case 2: Write(b, a2, spec); break;
                    case 3: Write(b, a3, spec); break;
                    default: Missing(b, idx, spec); break;
                }
            }
        }
    }
}
