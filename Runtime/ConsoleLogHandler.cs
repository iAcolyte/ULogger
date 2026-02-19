#nullable enable

using System;
using System.Text;

using UnityEngine;

namespace ULogger {
    [DefaultExecutionOrder(int.MinValue)]
    [CreateAssetMenu(fileName = "ConsoleLogHandler", menuName = "ULogger/Console Log")]
    public sealed class ConsoleLogHandler: ULogHandler {
        const string warningColor = nameof(Color.yellow);
        const string errorColor = nameof(Color.red);
        const string assertColor = nameof(Color.magenta);

        static readonly Color InfoColor = Color.gray;
        static readonly ILogHandler defaultHandler = Debug.unityLogger.logHandler;

        readonly StringBuilder builder = new();

        static string ModifyFormat(bool useColors, Color infoColor, LogType logType, string format, StringBuilder builder) {
            if (!useColors) return format;

            var color = logType switch {
                LogType.Error or LogType.Exception => errorColor,
                LogType.Warning => warningColor,
                LogType.Assert => assertColor,
                _ => '#' + ColorUtility.ToHtmlStringRGB(infoColor)
            };
            builder.Clear();
            var shouldWrap = logType != LogType.Log || infoColor != InfoColor;
            if (shouldWrap) {
                builder.Append("<color=").Append(color).Append('>');
            }
            builder.Append(format);
            if (shouldWrap) {
                builder.Append("</color>");
            }

            return builder.ToString();
        }

        [SerializeField] Color infoColor = InfoColor;
        [SerializeField] LogType logLevel = LogType.Log;
        [SerializeField] string tagFormatOverride = "{0}: {1}";
        [SerializeField] bool useColors = false;

        protected override void LogExceptionInherit(Exception exception, UnityEngine.Object context) {
            defaultHandler?.LogException(exception, context);
        }

        protected override bool LogFormatInherit(LogType logType, UnityEngine.Object context, string _, params object[] args) {
            if (logType > logLevel) {
                return false;
            }
            var formatOverride = args.Length == 1 ? "{0}" : !string.IsNullOrEmpty(tagFormatOverride) ? tagFormatOverride : "{0}";
            defaultHandler?.LogFormat(logType, context, ModifyFormat(useColors, infoColor, logType, formatOverride, builder), args);

            return true;
        }
    }
}
