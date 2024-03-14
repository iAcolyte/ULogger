using UnityEngine;
using ULogger;
using System.Text;
using System.IO;

namespace Luka.Tomo.ULogger {
    [CreateAssetMenu(fileName = "FileLogger", menuName = "ULogger/FileLogger")]
    public class FileLogger: UAbstractLogger {
        public bool time = true;
        public bool levelPrefix = true;
        public bool callerPostfix = true;
        public string fileName = "log";

        private string path = string.Empty;

        private void OnEnable() {
            path = Path.Combine(Application.persistentDataPath, $"{fileName}_{System.DateTime.Now:HH.mm.ss}.log");
        }

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
            if (!Application.isPlaying) return;
            var sb = new StringBuilder();
            if (time) AppendTime(sb);
            if (levelPrefix) AppendPrefix(logLevel, sb);
            sb.Append(message);
            if (callerPostfix && caller.name is not null) AppendCallerAtNewLine(sb, caller);
            sb.AppendLine();
            var msg = sb.ToString();

            File.AppendAllText(path, msg);
        }
    }
}
