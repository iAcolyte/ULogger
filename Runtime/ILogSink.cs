#nullable enable

using System;

namespace ULogger {
    /// <summary>
    /// The allocation-free logging destination. Deliberately non-generic: generic virtual methods
    /// need per-value-type AOT code on IL2CPP and blow up when a type is only known at runtime.
    /// All generic formatting lives in <see cref="LogSinkExtensions"/>, which is static and gets
    /// instantiated at the call site, where the argument types are known to the compiler.
    /// </summary>
    public interface ILogSink {
        /// <summary>
        /// Whether entries of this level are wanted. Callers check this before formatting, so a
        /// disabled level costs one virtual call and nothing else.
        /// </summary>
        bool IsEnabled(LogLevel level);

        /// <summary>Writes an already formatted entry. The spans are only valid for the call.</summary>
        void Write(LogLevel level, ReadOnlySpan<char> tag, ReadOnlySpan<char> message, UnityEngine.Object? context);

        /// <summary>Writes an exception. Unavoidably allocating: the stack trace is built as a string.</summary>
        void WriteException(Exception exception, UnityEngine.Object? context);
    }
}
