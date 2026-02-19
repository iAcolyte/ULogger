#nullable enable

using System;
using System.Collections.Generic;

using UnityEngine;

namespace ULogger {
    [CreateAssetMenu(fileName = "Composite LogHandler", menuName = "ULogger/Composite Log Handler")]
    public sealed class CompositeLogHandler: ULogHandler {
        [SerializeField] private List<ULogHandler?> logHandlers;
        public IReadOnlyList<ULogHandler?> LogHandlers => logHandlers;

        void OnValidate() {
            for (int i = 0; i < logHandlers.Count; i++) {
                if (logHandlers[i] == this) logHandlers[i] = null;
            }
        }

        protected override void LogExceptionInherit(Exception exception, UnityEngine.Object context) {
            foreach (var logHandler in logHandlers)
                logHandler?.LogException(exception, context);
        }

        protected override bool LogFormatInherit(LogType logType, UnityEngine.Object context, string format, params object[] args) {
            foreach (var logHandler in logHandlers)
                logHandler?.LogFormat(logType, context, format, args);
            return true;
        }
    }
}
