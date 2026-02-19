#nullable enable

using System;
using System.Collections.Generic;

using UnityEngine;

namespace ULogger {
    public abstract class ULogHandler: ScriptableObject, ILogHandler {
        static Dictionary<Type, int> signatures = new();

        [SerializeField] string[] tags;

        HashSet<string> Tags => _tags ??= new HashSet<string>(tags);
        Type Type => _type ??= this.GetType();

        HashSet<string>? _tags;
        Type? _type;

        public void LogException(Exception exception, UnityEngine.Object context) {
            LogExceptionInherit(exception, context);
        }

        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args) {
            var signature = HashCode.Combine(logType, context, format);
            foreach (var arg in args) signature = HashCode.Combine(signature, arg);

            if (signatures.TryGetValue(Type, out var sig) && sig == signature) return;
            if (tags.Length == 0) {
                if (LogFormatInherit(logType, context, format, args)) {
                    signatures[Type] = signature;
                }
                return;
            }
            if (format != "{0}: {1}") return;
            if (args[0] is not string tag || !Tags.Contains(tag)) return;
            if (LogFormatInherit(logType, context, format, args)) {
                signatures[Type] = signature;
            }
        }
        protected abstract void LogExceptionInherit(Exception exception, UnityEngine.Object context);
        protected abstract bool LogFormatInherit(LogType logType, UnityEngine.Object context, string format, params object[] args);
    }
}
