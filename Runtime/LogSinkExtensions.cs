#nullable enable

using System;

namespace ULogger {
    /// <summary>
    /// The zero-allocation logging API. Static and generic by arity: every call is instantiated at
    /// the call site with concrete argument types, so nothing is boxed and no object[] is built.
    /// Beyond 4 arguments, build the message yourself and hand it to <see cref="Write"/>:
    /// <code>
    /// if (!log.IsEnabled(LogLevel.Debug)) return;
    /// var b = LogFormatter.Scratch;
    /// b.Append("t=").Append(Time.time, "F3").Append(" dt=").Append(Time.deltaTime * 1000f, "F1").Append("ms");
    /// log.Write(LogLevel.Debug, b.Span);
    /// </code>
    ///
    /// Tag and context are not parameters here: an overload taking a leading string tag would be
    /// indistinguishable from one taking a format plus a string argument. Both are bound up front
    /// through <see cref="WithTag"/> / <see cref="For"/>, which cost nothing to carry.
    /// </summary>
    public static class LogSinkExtensions {
        /// <summary>
        /// Writes a message built by hand, untagged and without a context. Check
        /// <see cref="ILogSink.IsEnabled"/> before building it.
        /// </summary>
        public static void Write(this ILogSink sink, LogLevel level, ReadOnlySpan<char> message)
            => sink.Write(level, default, message, null);

        public static void Trace(this ILogSink sink, string message) {
            if (!sink.IsEnabled(LogLevel.Trace)) return;
            sink.Write(LogLevel.Trace, default, message.AsSpan(), null);
        }

        public static void Trace<T0>(this ILogSink sink, string format, T0 a0) {
            if (!sink.IsEnabled(LogLevel.Trace)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0);
            sink.Write(LogLevel.Trace, default, b.Span, null);
        }

        public static void Trace<T0, T1>(this ILogSink sink, string format, T0 a0, T1 a1) {
            if (!sink.IsEnabled(LogLevel.Trace)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1);
            sink.Write(LogLevel.Trace, default, b.Span, null);
        }

        public static void Trace<T0, T1, T2>(this ILogSink sink, string format, T0 a0, T1 a1, T2 a2) {
            if (!sink.IsEnabled(LogLevel.Trace)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2);
            sink.Write(LogLevel.Trace, default, b.Span, null);
        }

        public static void Trace<T0, T1, T2, T3>(this ILogSink sink, string format, T0 a0, T1 a1, T2 a2, T3 a3) {
            if (!sink.IsEnabled(LogLevel.Trace)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2, a3);
            sink.Write(LogLevel.Trace, default, b.Span, null);
        }


        public static void Debug(this ILogSink sink, string message) {
            if (!sink.IsEnabled(LogLevel.Debug)) return;
            sink.Write(LogLevel.Debug, default, message.AsSpan(), null);
        }

        public static void Debug<T0>(this ILogSink sink, string format, T0 a0) {
            if (!sink.IsEnabled(LogLevel.Debug)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0);
            sink.Write(LogLevel.Debug, default, b.Span, null);
        }

        public static void Debug<T0, T1>(this ILogSink sink, string format, T0 a0, T1 a1) {
            if (!sink.IsEnabled(LogLevel.Debug)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1);
            sink.Write(LogLevel.Debug, default, b.Span, null);
        }

        public static void Debug<T0, T1, T2>(this ILogSink sink, string format, T0 a0, T1 a1, T2 a2) {
            if (!sink.IsEnabled(LogLevel.Debug)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2);
            sink.Write(LogLevel.Debug, default, b.Span, null);
        }

        public static void Debug<T0, T1, T2, T3>(this ILogSink sink, string format, T0 a0, T1 a1, T2 a2, T3 a3) {
            if (!sink.IsEnabled(LogLevel.Debug)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2, a3);
            sink.Write(LogLevel.Debug, default, b.Span, null);
        }


        public static void Info(this ILogSink sink, string message) {
            if (!sink.IsEnabled(LogLevel.Info)) return;
            sink.Write(LogLevel.Info, default, message.AsSpan(), null);
        }

        public static void Info<T0>(this ILogSink sink, string format, T0 a0) {
            if (!sink.IsEnabled(LogLevel.Info)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0);
            sink.Write(LogLevel.Info, default, b.Span, null);
        }

        public static void Info<T0, T1>(this ILogSink sink, string format, T0 a0, T1 a1) {
            if (!sink.IsEnabled(LogLevel.Info)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1);
            sink.Write(LogLevel.Info, default, b.Span, null);
        }

        public static void Info<T0, T1, T2>(this ILogSink sink, string format, T0 a0, T1 a1, T2 a2) {
            if (!sink.IsEnabled(LogLevel.Info)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2);
            sink.Write(LogLevel.Info, default, b.Span, null);
        }

        public static void Info<T0, T1, T2, T3>(this ILogSink sink, string format, T0 a0, T1 a1, T2 a2, T3 a3) {
            if (!sink.IsEnabled(LogLevel.Info)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2, a3);
            sink.Write(LogLevel.Info, default, b.Span, null);
        }


        public static void Warning(this ILogSink sink, string message) {
            if (!sink.IsEnabled(LogLevel.Warning)) return;
            sink.Write(LogLevel.Warning, default, message.AsSpan(), null);
        }

        public static void Warning<T0>(this ILogSink sink, string format, T0 a0) {
            if (!sink.IsEnabled(LogLevel.Warning)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0);
            sink.Write(LogLevel.Warning, default, b.Span, null);
        }

        public static void Warning<T0, T1>(this ILogSink sink, string format, T0 a0, T1 a1) {
            if (!sink.IsEnabled(LogLevel.Warning)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1);
            sink.Write(LogLevel.Warning, default, b.Span, null);
        }

        public static void Warning<T0, T1, T2>(this ILogSink sink, string format, T0 a0, T1 a1, T2 a2) {
            if (!sink.IsEnabled(LogLevel.Warning)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2);
            sink.Write(LogLevel.Warning, default, b.Span, null);
        }

        public static void Warning<T0, T1, T2, T3>(this ILogSink sink, string format, T0 a0, T1 a1, T2 a2, T3 a3) {
            if (!sink.IsEnabled(LogLevel.Warning)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2, a3);
            sink.Write(LogLevel.Warning, default, b.Span, null);
        }


        public static void Error(this ILogSink sink, string message) {
            if (!sink.IsEnabled(LogLevel.Error)) return;
            sink.Write(LogLevel.Error, default, message.AsSpan(), null);
        }

        public static void Error<T0>(this ILogSink sink, string format, T0 a0) {
            if (!sink.IsEnabled(LogLevel.Error)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0);
            sink.Write(LogLevel.Error, default, b.Span, null);
        }

        public static void Error<T0, T1>(this ILogSink sink, string format, T0 a0, T1 a1) {
            if (!sink.IsEnabled(LogLevel.Error)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1);
            sink.Write(LogLevel.Error, default, b.Span, null);
        }

        public static void Error<T0, T1, T2>(this ILogSink sink, string format, T0 a0, T1 a1, T2 a2) {
            if (!sink.IsEnabled(LogLevel.Error)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2);
            sink.Write(LogLevel.Error, default, b.Span, null);
        }

        public static void Error<T0, T1, T2, T3>(this ILogSink sink, string format, T0 a0, T1 a1, T2 a2, T3 a3) {
            if (!sink.IsEnabled(LogLevel.Error)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2, a3);
            sink.Write(LogLevel.Error, default, b.Span, null);
        }


        public static void Critical(this ILogSink sink, string message) {
            if (!sink.IsEnabled(LogLevel.Critical)) return;
            sink.Write(LogLevel.Critical, default, message.AsSpan(), null);
        }

        public static void Critical<T0>(this ILogSink sink, string format, T0 a0) {
            if (!sink.IsEnabled(LogLevel.Critical)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0);
            sink.Write(LogLevel.Critical, default, b.Span, null);
        }

        public static void Critical<T0, T1>(this ILogSink sink, string format, T0 a0, T1 a1) {
            if (!sink.IsEnabled(LogLevel.Critical)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1);
            sink.Write(LogLevel.Critical, default, b.Span, null);
        }

        public static void Critical<T0, T1, T2>(this ILogSink sink, string format, T0 a0, T1 a1, T2 a2) {
            if (!sink.IsEnabled(LogLevel.Critical)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2);
            sink.Write(LogLevel.Critical, default, b.Span, null);
        }

        public static void Critical<T0, T1, T2, T3>(this ILogSink sink, string format, T0 a0, T1 a1, T2 a2, T3 a3) {
            if (!sink.IsEnabled(LogLevel.Critical)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2, a3);
            sink.Write(LogLevel.Critical, default, b.Span, null);
        }


        /// <summary>Binds a tag to a sink. The result is a struct: no allocation, cache it in a field.</summary>
        public static TaggedLogSink WithTag(this ILogSink sink, string tag) => new(sink, tag, null);

        /// <summary>Binds a Unity context object, so console entries stay clickable.</summary>
        public static TaggedLogSink For(this ILogSink sink, UnityEngine.Object? context) => new(sink, null, context);

        public static void Exception(this ILogSink sink, Exception exception, UnityEngine.Object? context = null)
            => sink.WriteException(exception, context);
    }
}
