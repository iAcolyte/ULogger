using System.Text;
using UnityEngine;

namespace ULogger {
    [CreateAssetMenu(fileName = "ConsoleLogger", menuName = "ULogger/Console")]
    public class ConsoleLogger: UAbstractLogger {

        public bool time = true;
        public bool levelPrefix = true;
        public bool callerPostfix = true;

        private void AppendPrefix(LogLevel logLevel, StringBuilder builder) {
            var prefixText = logLevel switch {
                LogLevel.Trace => "TRACE : ",
                LogLevel.Debug => "DEBUG : ",
                LogLevel.Info => "INFO : ",
                LogLevel.Warn => "WARN : ",
                LogLevel.Error => "ERROR : ",
                LogLevel.Fatal => "FATAL : ",
                _ => throw new System.NotImplementedException()
            };
            builder.Append(prefixText);
        }
        private void AppendTime(StringBuilder builder) {
            var time = System.DateTime.UtcNow.ToString("HH:mm:ss.fff");
            builder.Append(time).Append(" | ");
        }

        private void AppendCallerAtNewLine(StringBuilder builder, Caller caller) {
            builder.AppendLine();
            builder.AppendFormat("At <a href=\"{0}\" line=\"{1}\">({2}) {0}:{1}</a>", caller.filePath, caller.lineNumber, caller.name);
        }

        public override void LogFormat(LogLevel logLevel, UnityEngine.Object context, string message, Caller caller) {
            var sb = new StringBuilder();
            if (time) AppendTime(sb);
            if (levelPrefix) AppendPrefix(logLevel, sb);
            sb.Append(message);
            var logType = logLevel switch {
                LogLevel.Warn => LogType.Warning,
                LogLevel.Error or LogLevel.Fatal => LogType.Error,
                _ => LogType.Log
            };
            if (callerPostfix && caller.name is not null) AppendCallerAtNewLine(sb, caller);
            Logger.DefaultLogHandler.LogFormat(logType, context, sb.ToString(), System.Array.Empty<object>());
        }
    }
}
