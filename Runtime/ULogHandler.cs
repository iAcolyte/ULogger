#nullable enable

using System;
using System.Collections.Generic;

using UnityEngine;

namespace ULogger {
    public abstract class ULogHandler: ScriptableObject, ILogHandler {
        static readonly HashSet<(object scope, int signature)> dispatched = new();
        static int dispatchDepth;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetDispatchState() {
            dispatched.Clear();
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

        public void LogException(Exception exception, UnityEngine.Object context) {
            var signature = HashCode.Combine(exception, context);

            if (dispatchDepth == 0) dispatched.Clear();
            dispatchDepth++;
            try {
                var key = (DedupScope, signature);
                if (dispatched.Contains(key)) return;
                LogExceptionInherit(exception, context);
                dispatched.Add(key);
            } finally {
                dispatchDepth--;
            }
        }

        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args) {
            var signature = HashCode.Combine(logType, context, format);
            foreach (var arg in args) signature = HashCode.Combine(signature, arg);

            if (dispatchDepth == 0) dispatched.Clear();
            dispatchDepth++;
            try {
                var key = (DedupScope, signature);
                if (dispatched.Contains(key)) return;

                if (tags.Length == 0) {
                    if (LogFormatInherit(logType, context, format, args)) dispatched.Add(key);
                    return;
                }
                if (args.Length == 0 || args[0] is not string tag || !Tags.Contains(tag)) return;
                if (LogFormatInherit(logType, context, format, args)) dispatched.Add(key);
            } finally {
                dispatchDepth--;
            }
        }

        protected virtual object DedupScope => Type;

        protected abstract void LogExceptionInherit(Exception exception, UnityEngine.Object context);
        protected abstract bool LogFormatInherit(LogType logType, UnityEngine.Object context, string format, params object[] args);
    }
}
