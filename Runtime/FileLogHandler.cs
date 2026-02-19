#nullable enable

using System;
using System.IO;
using System.Text;

using UnityEngine;

namespace ULogger {
    [CreateAssetMenu(fileName = "FileLogHandler", menuName = "ULogger/File Log")]
    public sealed class FileLogHandler: ULogHandler, IDisposable {

        readonly StringBuilder builder = new();

        [Header("Settings")]
        [Tooltip("You can use special '%pdp' or '%dp' variables as persistentDataPath or DataPath, and '%dt' for datetime")]
        [SerializeField] string path;
        [SerializeField] LogType logLevel = LogType.Log;
        [Header("Formatting")]
        [SerializeField] string appendTimeFormat = "yyyy-MM-dd hh:mm:ss.fff";
        [SerializeField] string tagFormat = "[{0}] \"{1}\"";
        [SerializeField] bool appendLogLevel = true;

        string Path {
            get {
                if (!string.IsNullOrEmpty(_path)) return _path;
                _path = path;
                if (_path.StartsWith("%pdp")) {
                    _path = Application.persistentDataPath + _path[4..];
                }
                if (_path.StartsWith("%dp")) {
                    _path = Application.dataPath + _path[3..];
                }
                if (_path.Contains("%dt")) {
                    _path = _path.Replace("%dt", DateTime.Now.ToString("yyyy_MM_dd_hh_mm_ss"));
                }
                return _path;
            }
        }
        StreamWriter Writer {
            get {
                if (_writer is not null) return _writer;
                var info = new FileInfo(Path);
                _writer = info.Exists ? info.AppendText() : info.CreateText();
                Application.quitting += ((IDisposable)this).Dispose;
                return _writer;
            }
        }

        string? _path;
        StreamWriter? _writer;

        void OnValidate() {
            if (_writer is not null) {
                _writer.Close();
                _writer.Dispose();
            }
            _path = null;
            _writer = null;
        }

        protected override void LogExceptionInherit(Exception exception, UnityEngine.Object context) {
            var full = exception.ToString().Replace("\r\n", " ").Replace("\n", " ");

            var colonIndex = full.IndexOf(':');
            if (colonIndex < 0) {
                LogFormatInherit(LogType.Exception, context, "[{0}] {1}", "Exception", full);
                return;
            }

            var typeName = full[..colonIndex];
            var rest = full[(colonIndex + 1)..].TrimStart();

            var atIndex = rest.IndexOf("   at ", StringComparison.Ordinal);
            if (atIndex < 0) atIndex = rest.IndexOf(" at ", StringComparison.Ordinal);

            string formatted;
            if (atIndex >= 0) {
                var message = rest[..atIndex].Trim();
                var trace = rest[atIndex..].Trim();
                formatted = $"\"{message}\" {trace}";
            } else {
                formatted = $"\"{rest.Trim()}\"";
            }

            LogFormatInherit(LogType.Exception, context, "[{0}] {1}", typeName, formatted);
        }

        protected override bool LogFormatInherit(LogType logType, UnityEngine.Object? context, string format, params object[] args) {
            if (logType != LogType.Exception && logType > logLevel) return false;
            builder.Clear();
            if (!string.IsNullOrEmpty(appendTimeFormat))
                builder.Append(DateTime.Now.ToString(appendTimeFormat)).Append(' ');

            if (appendLogLevel) {
                (string log, string tabs) result = logType switch {
                    LogType.Exception => ("FATAL", "\t"),
                    LogType.Assert => ("ASSERT", "\t"),
                    LogType.Error => ("ERROR", "\t"),
                    LogType.Warning => ("WARNING", "\t"),
                    LogType.Log => ("INFO", "\t"),
                    _ => ("INFO", "\t"),
                };
                builder.Append(result.log).Append(result.tabs);
            }

            if (args.Length > 1 && !string.IsNullOrEmpty(tagFormat)) {
                builder.AppendFormat(logType != LogType.Exception ? tagFormat : format, args);
            } else if (logType != LogType.Exception) {
                builder.Append('"').Append(args[0]).Append('"');
            } else {
                builder.Append(args[^1]);
            }
            Writer.WriteLine(builder.ToString());
            _ = Writer.FlushAsync();
            return true;
        }

        void IDisposable.Dispose() {
            Application.quitting -= ((IDisposable)this).Dispose;
            if (_writer is null) return;
            _writer.Close();
            _writer.Dispose();
        }
    }
}
