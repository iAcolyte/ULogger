#nullable enable

using System;
using System.Text;

using UnityEngine;

namespace ULogger {
    [CreateAssetMenu(fileName = "ConsoleLogHandler", menuName = "ULogger/Console Log")]
    public sealed class ConsoleLogHandler: ULogHandler {
        const string warningColor = nameof(Color.yellow);
        const string errorColor = nameof(Color.red);
        const string assertColor = nameof(Color.magenta);

        static readonly Color InfoColor = Color.gray;

        static ILogHandler? defaultHandler;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void CaptureDefaultHandler() {
            if (Debug.unityLogger.logHandler is not ULogHandler) {
                defaultHandler = Debug.unityLogger.logHandler;
            }
        }
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
        [SerializeField] LogLevel minLevel = LogLevel.Trace;
        [SerializeField] string tagFormatOverride = "{0}: {1}";
        [SerializeField] bool useColors = false;

        readonly StringBuilder builder = new();
        readonly StringBuilder builder2 = new();

        protected override void LogExceptionInherit(Exception exception, UnityEngine.Object? context) {
            if (exception is IOverrideContextForException overriddenContext) context = overriddenContext.Context;
            defaultHandler?.LogException(exception, context);
        }

        protected override bool IsEnabledInherit(LogLevel level) => level >= minLevel;

        protected override void WriteInherit(LogLevel level, ReadOnlySpan<char> tag, ReadOnlySpan<char> message, UnityEngine.Object? context) {
            // Unity's handler takes a string, so this path allocates one by definition; the
            // message is passed as an argument rather than as the format so that braces inside
            // it are not re-interpreted by string.Format.
            builder.Clear();
            if (tag.Length > 0) builder.Append('[').Append(tag).Append("] ");
            builder.Append(message);

            var logType = ToLogType(level);
            defaultHandler?.LogFormat(logType, context,
                ModifyFormat(useColors, infoColor, logType, "{0}", builder2), builder.ToString());
        }

        protected override bool LogFormatInherit(LogType logType, UnityEngine.Object? context, string format, params object[] args) {
            if (ToLogLevel(logType) < minLevel) {
                return false;
            }
            var formatOverride = args.Length == 0 ? format : args.Length == 1 ? "{0}" : !string.IsNullOrEmpty(tagFormatOverride) ? tagFormatOverride : format;

            defaultHandler?.LogFormat(logType, context, ModifyFormat(useColors, infoColor, logType, formatOverride, builder), args);

            return true;
        }
    }
}
