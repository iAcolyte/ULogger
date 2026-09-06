#nullable enable

using System;
using System.Collections.Generic;

using UnityEngine;

namespace ULogger {
    public abstract class ULogHandler: ScriptableObject, ILogHandler, ILogSink {
        // Dedup state is per-thread: handlers may be driven from background threads, and a shared
        // HashSet would corrupt under concurrent mutation. Deduplication is only meaningful within
        // a single dispatch anyway, and a dispatch never spans threads.
        [ThreadStatic] static HashSet<(object scope, int signature)>? dispatched;
        [ThreadStatic] static int dispatchDepth;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetDispatchState() {
            dispatched?.Clear();
            dispatchDepth = 0;
        }

        // Unity's Logger.Log(tag, message) renders through this exact format string, and that is
        // how a tagged call is recognized here. Coupled to a Unity internal detail: logging with
        // this literal format directly is likewise treated as tagged.
        const string TaggedFormat = "{0}: {1}";

        [SerializeField] string[] tags = System.Array.Empty<string>();

        HashSet<string> Tags => _tags ??= new HashSet<string>(tags);
        Type Type => _type ??= this.GetType();

        HashSet<string>? _tags;
        Type? _type;

        public void LogException(Exception exception, UnityEngine.Object? context) {
            var signature = HashCode.Combine(exception, context);
            var seen = dispatched ??= new HashSet<(object, int)>();

            if (dispatchDepth == 0) seen.Clear();
            dispatchDepth++;
            try {
                var key = (DedupScope, signature);
                if (seen.Contains(key)) return;
                LogExceptionInherit(exception, context);
                seen.Add(key);
            } finally {
                dispatchDepth--;
            }
        }

        public void LogFormat(LogType logType, UnityEngine.Object? context, string format, params object[] args) {
            var signature = HashCode.Combine(logType, context, format);
            foreach (var arg in args) signature = HashCode.Combine(signature, arg);
            var seen = dispatched ??= new HashSet<(object, int)>();

            if (dispatchDepth == 0) seen.Clear();
            dispatchDepth++;
            try {
                var key = (DedupScope, signature);
                if (seen.Contains(key)) return;

                if (tags.Length == 0) {
                    if (LogFormatInherit(logType, context, format, args)) seen.Add(key);
                    return;
                }
                if (args.Length == 0 || args[0] is not string tag || !Tags.Contains(tag)) return;
                if (LogFormatInherit(logType, context, format, args)) seen.Add(key);
            } finally {
                dispatchDepth--;
            }
        }

        // ------------------------------------------------------------------ ILogSink

        // The zero-allocation path. It carries no deduplication: unlike Unity's handler chain,
        // nothing here dispatches the same entry twice, and hashing spans would defeat the point.

        public bool IsEnabled(LogLevel level) => level != LogLevel.Off && IsEnabledInherit(level);

        public void Write(LogLevel level, ReadOnlySpan<char> tag, ReadOnlySpan<char> message, UnityEngine.Object? context) {
            if (!IsEnabled(level) || !TagAllowed(tag)) return;
            WriteInherit(level, tag, message, context);
        }

        // Intentionally not tag-filtered: a handler configured with tags would otherwise swallow
        // every exception reaching it untagged, which is the one class of entry worth never losing.
        public void WriteException(Exception exception, UnityEngine.Object? context) {
            if (!IsEnabled(LogLevel.Error)) return;
            LogExceptionInherit(exception, context);
        }

        // A handler with tags accepts tagged entries only, so an untagged call is dropped. This
        // mirrors the ILogHandler path and is what makes a tag list a filter rather than a label.
        bool TagAllowed(ReadOnlySpan<char> tag) {
            if (tags.Length == 0) return true;
            // Few tags in practice, and a HashSet cannot be probed with a span on netstandard 2.1.
            for (var i = 0; i < tags.Length; i++)
                if (tag.SequenceEqual(tags[i].AsSpan())) return true;
            return false;
        }

        protected virtual bool IsEnabledInherit(LogLevel level) => true;

        protected abstract void WriteInherit(LogLevel level, ReadOnlySpan<char> tag, ReadOnlySpan<char> message, UnityEngine.Object? context);

        // ------------------------------------------------------------------ shared

        /// <summary>Maps to Unity's inverted, coarser severity scale.</summary>
        public static LogType ToLogType(LogLevel level) => level switch {
            LogLevel.Warning => LogType.Warning,
            LogLevel.Error => LogType.Error,
            LogLevel.Critical => LogType.Exception,
            _ => LogType.Log
        };

        /// <summary>Maps back, for handlers whose filter is still expressed as a LogType.</summary>
        public static LogLevel ToLogLevel(LogType type) => type switch {
            LogType.Warning => LogLevel.Warning,
            LogType.Error or LogType.Assert => LogLevel.Error,
            LogType.Exception => LogLevel.Critical,
            _ => LogLevel.Info
        };

        protected virtual object DedupScope => Type;

        protected abstract void LogExceptionInherit(Exception exception, UnityEngine.Object? context);
        protected abstract bool LogFormatInherit(LogType logType, UnityEngine.Object? context, string format, params object[] args);
    }
}
