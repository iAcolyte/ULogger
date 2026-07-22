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
        [SerializeField] string path = string.Empty;
        [SerializeField] LogType logLevel = LogType.Log;
        [SerializeField] bool logExceptions = true;
        [Header("Formatting")]
        [SerializeField] string appendTimeFormat = "yyyy-MM-dd HH:mm:ss.fff";
        [SerializeField] string tagFormat = "[{0}] \"{1}\"";
        [SerializeField] bool appendLogLevel = true;

        string? _path;
        StreamWriter? _writer;

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
                    _path = _path.Replace("%dt", DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss"));
                }
                return _path;
            }
        }

        protected override object DedupScope => Path;

        StreamWriter Writer {
            get {
                if (_writer is not null) return _writer;
                var info = new FileInfo(Path);
                info.Directory?.Create();
                _writer = info.Exists ? info.AppendText() : info.CreateText();
                _writer.AutoFlush = true;
                Application.quitting += CloseWriter;
#if UNITY_EDITOR
                UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
                return _writer;
            }
        }

        void CloseWriter() {
            Application.quitting -= CloseWriter;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
            _path = null;
            if (_writer is null) return;
            _writer.Close();
            _writer.Dispose();
            _writer = null;
        }

#if UNITY_EDITOR
        void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state) {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode) CloseWriter();
        }
#endif

        void OnValidate() {
            CloseWriter();
        }

        protected override void LogExceptionInherit(Exception exception, UnityEngine.Object context) {
            if (!logExceptions) return;
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
            return true;
        }

        void IDisposable.Dispose() => CloseWriter();
    }
}
