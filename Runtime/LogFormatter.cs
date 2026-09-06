#nullable enable

using System;

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

        public void Append(char c) {
            Ensure(1);
            Data[Length++] = c;
        }

        public void Append(ReadOnlySpan<char> s) {
            if (s.Length == 0) return;
            Ensure(s.Length);
            s.CopyTo(new Span<char>(Data, Length, Data.Length - Length));
            Length += s.Length;
        }

        /// <summary>Resets for a new entry, shrinking back if one huge message blew the buffer up.</summary>
        public void Reset() {
            Length = 0;
            if (Data.Length > MaxRetained) Data = new char[MaxRetained];
        }
    }

    /// <summary>Turns "{0}"-style formats and their arguments into chars without boxing.</summary>
    public static class LogFormatter {
        [ThreadStatic] static CharBuffer? scratch;

        public static CharBuffer Scratch {
            get {
                var b = scratch ??= new CharBuffer(1024);
                b.Reset();
                return b;
            }
        }

        // ------------------------------------------------------------------ values

        /// <summary>
        /// Writes a value of a statically known type. The typeof(T) comparisons are constants once
        /// the method is instantiated, so each instantiation collapses to a single branch; the
        /// reinterpret avoids the box that "(int)(object)value" would still perform at runtime.
        /// </summary>
        public static void Write<T>(CharBuffer b, T value) {
            if (typeof(T) == typeof(int)) { WriteInt(b, UnsafeUtility.As<T, int>(ref value)); return; }
            if (typeof(T) == typeof(long)) { WriteInt(b, UnsafeUtility.As<T, long>(ref value)); return; }
            if (typeof(T) == typeof(short)) { WriteInt(b, UnsafeUtility.As<T, short>(ref value)); return; }
            if (typeof(T) == typeof(sbyte)) { WriteInt(b, UnsafeUtility.As<T, sbyte>(ref value)); return; }
            if (typeof(T) == typeof(uint)) { WriteUInt(b, UnsafeUtility.As<T, uint>(ref value)); return; }
            if (typeof(T) == typeof(ulong)) { WriteUInt(b, UnsafeUtility.As<T, ulong>(ref value)); return; }
            if (typeof(T) == typeof(ushort)) { WriteUInt(b, UnsafeUtility.As<T, ushort>(ref value)); return; }
            if (typeof(T) == typeof(byte)) { WriteUInt(b, UnsafeUtility.As<T, byte>(ref value)); return; }
            if (typeof(T) == typeof(float)) { WriteDouble(b, UnsafeUtility.As<T, float>(ref value)); return; }
            if (typeof(T) == typeof(double)) { WriteDouble(b, UnsafeUtility.As<T, double>(ref value)); return; }
            if (typeof(T) == typeof(decimal)) { WriteDecimal(b, UnsafeUtility.As<T, decimal>(ref value)); return; }
            if (typeof(T) == typeof(bool)) { b.Append(UnsafeUtility.As<T, bool>(ref value) ? "True" : "False"); return; }
            if (typeof(T) == typeof(char)) { b.Append(UnsafeUtility.As<T, char>(ref value)); return; }

            WriteObject(b, value);
        }

        static void WriteObject<T>(CharBuffer b, T value) {
            // Reference types box for free (they are already references); only the fallback
            // ToString() on a struct allocates, and there is no way around it in C# 9.
            switch (value) {
                case null: return;
                case string s: b.Append(s.AsSpan()); return;
                case Enum e: WriteEnum(b, e); return;
                default: b.Append(value!.ToString().AsSpan()); return;
            }
        }

        static void WriteEnum(CharBuffer b, Enum value) {
            // Enum.ToString() goes through reflection and allocates; print the numeric value.
            if (Type.GetTypeCode(value.GetType()) == TypeCode.UInt64) WriteUInt(b, Convert.ToUInt64(value));
            else WriteInt(b, Convert.ToInt64(value));
        }

        static void WriteInt(CharBuffer b, long value) {
            b.Ensure(20);
            if (value.TryFormat(new Span<char>(b.Data, b.Length, b.Data.Length - b.Length), out var written))
                b.Length += written;
        }

        static void WriteUInt(CharBuffer b, ulong value) {
            b.Ensure(20);
            if (value.TryFormat(new Span<char>(b.Data, b.Length, b.Data.Length - b.Length), out var written))
                b.Length += written;
        }

        static void WriteDouble(CharBuffer b, double value) {
            b.Ensure(32);
            if (value.TryFormat(new Span<char>(b.Data, b.Length, b.Data.Length - b.Length), out var written))
                b.Length += written;
            else b.Append("NaN");
        }

        static void WriteDecimal(CharBuffer b, decimal value) {
            b.Ensure(32);
            if (value.TryFormat(new Span<char>(b.Data, b.Length, b.Data.Length - b.Length), out var written))
                b.Length += written;
            else b.Append("NaN");
        }

        // ------------------------------------------------------------------ format scanning

        /// <summary>
        /// Copies literal text up to the next "{n}" placeholder and returns n, or -1 at the end of
        /// the format. "{{" and "}}" are literal braces; anything else malformed is copied verbatim.
        /// </summary>
        static int Next(CharBuffer b, string format, ref int pos) {
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
                if (pos + 2 < format.Length && format[pos + 2] == '}') {
                    var digit = format[pos + 1];
                    if (digit >= '0' && digit <= '9') {
                        pos += 3;
                        return digit - '0';
                    }
                }

                b.Append('{');
                pos++;
            }
            return -1;
        }

        // Placeholder index past the supplied arguments: keep it visible instead of silently
        // dropping it, the way string.Format would throw.
        static void Missing(CharBuffer b, int index) {
            b.Append('{');
            WriteInt(b, index);
            b.Append('}');
        }

        public static void Format(CharBuffer b, string format) {
            var pos = 0;
            int idx;
            while ((idx = Next(b, format, ref pos)) >= 0) Missing(b, idx);
        }

        public static void Format<T0>(CharBuffer b, string format, T0 a0) {
            var pos = 0;
            int idx;
            while ((idx = Next(b, format, ref pos)) >= 0) {
                if (idx == 0) Write(b, a0);
                else Missing(b, idx);
            }
        }

        public static void Format<T0, T1>(CharBuffer b, string format, T0 a0, T1 a1) {
            var pos = 0;
            int idx;
            while ((idx = Next(b, format, ref pos)) >= 0) {
                switch (idx) {
                    case 0: Write(b, a0); break;
                    case 1: Write(b, a1); break;
                    default: Missing(b, idx); break;
                }
            }
        }

        public static void Format<T0, T1, T2>(CharBuffer b, string format, T0 a0, T1 a1, T2 a2) {
            var pos = 0;
            int idx;
            while ((idx = Next(b, format, ref pos)) >= 0) {
                switch (idx) {
                    case 0: Write(b, a0); break;
                    case 1: Write(b, a1); break;
                    case 2: Write(b, a2); break;
                    default: Missing(b, idx); break;
                }
            }
        }

        public static void Format<T0, T1, T2, T3>(CharBuffer b, string format, T0 a0, T1 a1, T2 a2, T3 a3) {
            var pos = 0;
            int idx;
            while ((idx = Next(b, format, ref pos)) >= 0) {
                switch (idx) {
                    case 0: Write(b, a0); break;
                    case 1: Write(b, a1); break;
                    case 2: Write(b, a2); break;
                    case 3: Write(b, a3); break;
                    default: Missing(b, idx); break;
                }
            }
        }
    }
}
