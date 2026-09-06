#nullable enable

using System;

namespace ULogger {
    /// <summary>
    /// A sink with a tag and/or a Unity context bound to it. A readonly struct, so holding one in a
    /// field costs nothing and passing it around allocates nothing.
    /// </summary>
    public readonly struct TaggedLogSink {
        public readonly ILogSink Sink;
        public readonly string? Tag;
        public readonly UnityEngine.Object? Context;

        public TaggedLogSink(ILogSink sink, string? tag, UnityEngine.Object? context) {
            Sink = sink;
            Tag = tag;
            Context = context;
        }

        public TaggedLogSink WithTag(string tag) => new(Sink, tag, Context);
        public TaggedLogSink For(UnityEngine.Object? context) => new(Sink, Tag, context);

        public bool IsEnabled(LogLevel level) => Sink.IsEnabled(level);

        public void Trace(string message) {
            if (!Sink.IsEnabled(LogLevel.Trace)) return;
            Sink.Write(LogLevel.Trace, Tag.AsSpan(), message.AsSpan(), Context);
        }

        public void Trace<T0>(string format, T0 a0) {
            if (!Sink.IsEnabled(LogLevel.Trace)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0);
            Sink.Write(LogLevel.Trace, Tag.AsSpan(), b.Span, Context);
        }

        public void Trace<T0, T1>(string format, T0 a0, T1 a1) {
            if (!Sink.IsEnabled(LogLevel.Trace)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1);
            Sink.Write(LogLevel.Trace, Tag.AsSpan(), b.Span, Context);
        }

        public void Trace<T0, T1, T2>(string format, T0 a0, T1 a1, T2 a2) {
            if (!Sink.IsEnabled(LogLevel.Trace)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2);
            Sink.Write(LogLevel.Trace, Tag.AsSpan(), b.Span, Context);
        }

        public void Trace<T0, T1, T2, T3>(string format, T0 a0, T1 a1, T2 a2, T3 a3) {
            if (!Sink.IsEnabled(LogLevel.Trace)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2, a3);
            Sink.Write(LogLevel.Trace, Tag.AsSpan(), b.Span, Context);
        }


        public void Debug(string message) {
            if (!Sink.IsEnabled(LogLevel.Debug)) return;
            Sink.Write(LogLevel.Debug, Tag.AsSpan(), message.AsSpan(), Context);
        }

        public void Debug<T0>(string format, T0 a0) {
            if (!Sink.IsEnabled(LogLevel.Debug)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0);
            Sink.Write(LogLevel.Debug, Tag.AsSpan(), b.Span, Context);
        }

        public void Debug<T0, T1>(string format, T0 a0, T1 a1) {
            if (!Sink.IsEnabled(LogLevel.Debug)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1);
            Sink.Write(LogLevel.Debug, Tag.AsSpan(), b.Span, Context);
        }

        public void Debug<T0, T1, T2>(string format, T0 a0, T1 a1, T2 a2) {
            if (!Sink.IsEnabled(LogLevel.Debug)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2);
            Sink.Write(LogLevel.Debug, Tag.AsSpan(), b.Span, Context);
        }

        public void Debug<T0, T1, T2, T3>(string format, T0 a0, T1 a1, T2 a2, T3 a3) {
            if (!Sink.IsEnabled(LogLevel.Debug)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2, a3);
            Sink.Write(LogLevel.Debug, Tag.AsSpan(), b.Span, Context);
        }


        public void Info(string message) {
            if (!Sink.IsEnabled(LogLevel.Info)) return;
            Sink.Write(LogLevel.Info, Tag.AsSpan(), message.AsSpan(), Context);
        }

        public void Info<T0>(string format, T0 a0) {
            if (!Sink.IsEnabled(LogLevel.Info)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0);
            Sink.Write(LogLevel.Info, Tag.AsSpan(), b.Span, Context);
        }

        public void Info<T0, T1>(string format, T0 a0, T1 a1) {
            if (!Sink.IsEnabled(LogLevel.Info)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1);
            Sink.Write(LogLevel.Info, Tag.AsSpan(), b.Span, Context);
        }

        public void Info<T0, T1, T2>(string format, T0 a0, T1 a1, T2 a2) {
            if (!Sink.IsEnabled(LogLevel.Info)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2);
            Sink.Write(LogLevel.Info, Tag.AsSpan(), b.Span, Context);
        }

        public void Info<T0, T1, T2, T3>(string format, T0 a0, T1 a1, T2 a2, T3 a3) {
            if (!Sink.IsEnabled(LogLevel.Info)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2, a3);
            Sink.Write(LogLevel.Info, Tag.AsSpan(), b.Span, Context);
        }


        public void Warn(string message) {
            if (!Sink.IsEnabled(LogLevel.Warning)) return;
            Sink.Write(LogLevel.Warning, Tag.AsSpan(), message.AsSpan(), Context);
        }

        public void Warn<T0>(string format, T0 a0) {
            if (!Sink.IsEnabled(LogLevel.Warning)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0);
            Sink.Write(LogLevel.Warning, Tag.AsSpan(), b.Span, Context);
        }

        public void Warn<T0, T1>(string format, T0 a0, T1 a1) {
            if (!Sink.IsEnabled(LogLevel.Warning)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1);
            Sink.Write(LogLevel.Warning, Tag.AsSpan(), b.Span, Context);
        }

        public void Warn<T0, T1, T2>(string format, T0 a0, T1 a1, T2 a2) {
            if (!Sink.IsEnabled(LogLevel.Warning)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2);
            Sink.Write(LogLevel.Warning, Tag.AsSpan(), b.Span, Context);
        }

        public void Warn<T0, T1, T2, T3>(string format, T0 a0, T1 a1, T2 a2, T3 a3) {
            if (!Sink.IsEnabled(LogLevel.Warning)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2, a3);
            Sink.Write(LogLevel.Warning, Tag.AsSpan(), b.Span, Context);
        }


        public void Error(string message) {
            if (!Sink.IsEnabled(LogLevel.Error)) return;
            Sink.Write(LogLevel.Error, Tag.AsSpan(), message.AsSpan(), Context);
        }

        public void Error<T0>(string format, T0 a0) {
            if (!Sink.IsEnabled(LogLevel.Error)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0);
            Sink.Write(LogLevel.Error, Tag.AsSpan(), b.Span, Context);
        }

        public void Error<T0, T1>(string format, T0 a0, T1 a1) {
            if (!Sink.IsEnabled(LogLevel.Error)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1);
            Sink.Write(LogLevel.Error, Tag.AsSpan(), b.Span, Context);
        }

        public void Error<T0, T1, T2>(string format, T0 a0, T1 a1, T2 a2) {
            if (!Sink.IsEnabled(LogLevel.Error)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2);
            Sink.Write(LogLevel.Error, Tag.AsSpan(), b.Span, Context);
        }

        public void Error<T0, T1, T2, T3>(string format, T0 a0, T1 a1, T2 a2, T3 a3) {
            if (!Sink.IsEnabled(LogLevel.Error)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2, a3);
            Sink.Write(LogLevel.Error, Tag.AsSpan(), b.Span, Context);
        }


        public void Critical(string message) {
            if (!Sink.IsEnabled(LogLevel.Critical)) return;
            Sink.Write(LogLevel.Critical, Tag.AsSpan(), message.AsSpan(), Context);
        }

        public void Critical<T0>(string format, T0 a0) {
            if (!Sink.IsEnabled(LogLevel.Critical)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0);
            Sink.Write(LogLevel.Critical, Tag.AsSpan(), b.Span, Context);
        }

        public void Critical<T0, T1>(string format, T0 a0, T1 a1) {
            if (!Sink.IsEnabled(LogLevel.Critical)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1);
            Sink.Write(LogLevel.Critical, Tag.AsSpan(), b.Span, Context);
        }

        public void Critical<T0, T1, T2>(string format, T0 a0, T1 a1, T2 a2) {
            if (!Sink.IsEnabled(LogLevel.Critical)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2);
            Sink.Write(LogLevel.Critical, Tag.AsSpan(), b.Span, Context);
        }

        public void Critical<T0, T1, T2, T3>(string format, T0 a0, T1 a1, T2 a2, T3 a3) {
            if (!Sink.IsEnabled(LogLevel.Critical)) return;
            var b = LogFormatter.Scratch;
            LogFormatter.Format(b, format, a0, a1, a2, a3);
            Sink.Write(LogLevel.Critical, Tag.AsSpan(), b.Span, Context);
        }


        // Exceptions deliberately bypass the tag filter: an untagged handler that silently swallowed
        // them would hide the one class of entry nobody can afford to lose.
        public void Exception(Exception exception) => Sink.WriteException(exception, Context);

        public void Exception(Exception exception, UnityEngine.Object? context)
            => Sink.WriteException(exception, context);
    }
}
