
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ULogger {
    public abstract class UAbstractLogger: ScriptableObject, ILogHandler {
        public LogLevel LogLevel { get; internal set; } = LogLevel.Trace;

        public void Initialize() {
            LogLevel = Logger.LogLevel;
        }
        public void LogException(System.Exception exception, Object context) {
            Logger.DefaultLogHandler.LogException(exception, context);
        }
        public abstract void LogFormat(LogLevel logLevel, Object context, string message, Caller caller);
        void ILogHandler.LogFormat(LogType logType, Object context, string format, params object[] args) {
            var logLevel = logType switch {
                LogType.Exception => LogLevel.Fatal,
                LogType.Error or LogType.Assert => LogLevel.Error,
                LogType.Warning => LogLevel.Warn,
                _ => LogLevel.Debug
            };
            if (LogLevel > logLevel) return;
            LogFormat(logLevel, context, string.Format(format, args), default);
        }
    }
}
