#nullable enable

using System;
using System.Collections.Generic;

using UnityEngine;

namespace ULogger {
    public abstract class ULogHandler: ScriptableObject, ILogHandler {
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

        protected virtual object DedupScope => Type;

        protected abstract void LogExceptionInherit(Exception exception, UnityEngine.Object? context);
        protected abstract bool LogFormatInherit(LogType logType, UnityEngine.Object? context, string format, params object[] args);
    }
}
