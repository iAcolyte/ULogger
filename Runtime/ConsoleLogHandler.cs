#nullable enable

using System;
using System.Text;

using UnityEngine;

namespace ULogger {
    [CreateAssetMenu(fileName = "ConsoleLogHandler", menuName = "ULogger/Console Log")]
    public sealed class ConsoleLogHandler: ULogHandler, IMainThreadPump {
        // Unity's rich-text parser accepts these names verbatim; nameof keeps them tied to the
        // Color properties they stand for, so a typo cannot survive compilation.
        const string warningColor = nameof(Color.yellow);
        const string errorColor = nameof(Color.red);
        const string assertColor = nameof(Color.magenta);

        static readonly Color InfoColor = Color.gray;

        static ILogHandler? capturedDefaultHandler;

        // Rendering happens on the calling thread, which may be any thread, so the scratch builders
        // cannot be instance state. One dispatch never spans threads, so per-thread is enough.
        [ThreadStatic] static StringBuilder? messageBuilder;
        [ThreadStatic] static StringBuilder? formatBuilder;

        /// <summary>
        /// Unity's own handler, captured before it is replaced by a <see cref="ULogHandler"/>.
        /// Resolved lazily as well as eagerly: the attribute below does not run in edit mode, and a
        /// null handler here means log entries vanish without a trace.
        /// </summary>
        static ILogHandler? DefaultHandler {
            get {
                if (capturedDefaultHandler != null) return capturedDefaultHandler;
                var current = Debug.unityLogger.logHandler;
                if (current is ULogHandler) return null;
                return capturedDefaultHandler = current;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void CaptureDefaultHandler() {
            capturedDefaultHandler = null;
            if (Debug.unityLogger.logHandler is not ULogHandler) {
                capturedDefaultHandler = Debug.unityLogger.logHandler;
            }
        }

        [SerializeField] Color infoColor = InfoColor;
        [SerializeField] LogLevel minLevel = LogLevel.Trace;
        [SerializeField] string tagFormatOverride = "{0}: {1}";
        [SerializeField] bool useColors = false;

        [Tooltip("Capture a managed stack trace for entries logged off the main thread. " +
                 "Honours the project's per-LogType Stack Trace setting, but costs an allocation " +
                 "and symbol lookup at the call site.")]
        [SerializeField] bool captureBackgroundStackTrace = false;

        [Tooltip("Entries buffered per frame for delivery from background threads. " +
                 "Entries beyond this are dropped, and the drop is reported to the console.")]
        [SerializeField] int backgroundQueueCapacity = 256;

        SwapQueue<PendingEntry>? pending;
        string[]? colorWrappedFormats;

        // Rendered on the calling thread, emitted on the main thread.
        readonly struct PendingEntry {
            public readonly LogType LogType;
            public readonly string Message;
            public readonly UnityEngine.Object? Context;
            public readonly string? StackTrace;

            public PendingEntry(LogType logType, string message, UnityEngine.Object? context, string? stackTrace) {
                LogType = logType;
                Message = message;
                Context = context;
                StackTrace = stackTrace;
            }
        }

        SwapQueue<PendingEntry> Pending =>
            pending ??= new SwapQueue<PendingEntry>(Mathf.Max(1, backgroundQueueCapacity));

        void OnEnable() {
            colorWrappedFormats = null;
            MainThreadDispatcher.Register(this);
        }

        void OnDisable() {
            // Drain what is left; after unregistering nothing would ever emit it.
            if (MainThreadDispatcher.IsMainThread) Pump();
            MainThreadDispatcher.Unregister(this);
        }

        void OnValidate() {
            backgroundQueueCapacity = Mathf.Max(1, backgroundQueueCapacity);
            colorWrappedFormats = null;
        }

        // ------------------------------------------------------------------ emit

        protected override bool IsEnabledInherit(LogLevel level) => level >= minLevel;

        protected override void LogExceptionInherit(Exception exception, UnityEngine.Object? context) {
            if (exception is IOverrideContextForException overriddenContext) context = overriddenContext.Context;

            if (MainThreadDispatcher.IsMainThread || !MainThreadDispatcher.IsInstalled) {
                DefaultHandler?.LogException(exception, context);
                return;
            }
            // ToString() is the same text Unity's handler would render, and it must be produced
            // here: the exception may be mutated or reused by the time the frame ends.
            EnqueueOrReport(LogType.Exception, exception.ToString(), context);
        }

        protected override void WriteInherit(LogLevel level, ReadOnlySpan<char> tag, ReadOnlySpan<char> message, UnityEngine.Object? context) {
            // Unity's handler takes a string, so this path allocates one by definition.
            var builder = messageBuilder ??= new StringBuilder();
            builder.Clear();
            if (tag.Length > 0) builder.Append('[').Append(tag).Append("] ");
            builder.Append(message);

            var logType = ToLogType(level);
            var text = builder.ToString();

            if (MainThreadDispatcher.IsMainThread || !MainThreadDispatcher.IsInstalled) {
                // The message is passed as an argument rather than as the format, so that braces
                // inside it are not re-interpreted by string.Format.
                DefaultHandler?.LogFormat(logType, context, WrappedArgumentFormat(logType), text);
                return;
            }
            EnqueueOrReport(logType, text, context);
        }

        protected override bool LogFormatInherit(LogType logType, UnityEngine.Object? context, string format, params object[] args) {
            if (ToLogLevel(logType) < minLevel) return false;

            var handler = DefaultHandler;
            if (handler == null) return false;

            var formatOverride = args.Length == 0 ? format
                : args.Length == 1 ? "{0}"
                : !string.IsNullOrEmpty(tagFormatOverride) ? tagFormatOverride
                : format;

            if (MainThreadDispatcher.IsMainThread || !MainThreadDispatcher.IsInstalled) {
                handler.LogFormat(logType, context, ModifyFormat(logType, formatOverride), args);
                return true;
            }

            // args is owned by the caller and may be a reused buffer, so it cannot outlive this call.
            return EnqueueOrReport(logType, string.Format(formatOverride, args), context);
        }

        bool EnqueueOrReport(LogType logType, string message, UnityEngine.Object? context) {
            var stackTrace = captureBackgroundStackTrace
                ? MainThreadDispatcher.CaptureStackTrace(logType, skipFrames: 2)
                : null;

            if (Pending.Enqueue(new PendingEntry(logType, message, context, stackTrace))) return true;

            // Dropped; the count is reported from Pump, where writing to the console is legal.
            return false;
        }

        // ------------------------------------------------------------------ IMainThreadPump

        public bool HasPendingWork => pending != null && (pending.HasEntries || pending.DroppedCount > 0);

        public void Pump() {
            var queue = pending;
            if (queue == null) return;

            var handler = DefaultHandler;
            var count = queue.BeginDrain(out var entries);
            try {
                if (handler == null) return;

                for (var i = 0; i < count; i++) {
                    var entry = entries[i];

                    // The object may have been destroyed between the log call and this frame;
                    // Unity's null check is only meaningful here, on the main thread.
                    var context = entry.Context == null ? null : entry.Context;

                    var message = entry.StackTrace == null
                        ? entry.Message
                        : entry.Message + '\n' + entry.StackTrace;

                    // An exception is emitted as text rather than through LogException: the original
                    // object is long gone, and LogException would re-capture a trace pointing here.
                    handler.LogFormat(entry.LogType, context, WrappedArgumentFormat(entry.LogType), message);
                }
            } finally {
                queue.EndDrain();
            }

            var dropped = queue.TakeDropped();
            if (dropped > 0) {
                handler?.LogFormat(LogType.Warning, this,
                    "[ULogger] {0}: dropped {1} background log entry/entries (queue capacity {2}).",
                    name, dropped, backgroundQueueCapacity);
            }
        }

        // ------------------------------------------------------------------ colors

        /// <summary>
        /// The "{0}" format used by every single-argument path, pre-wrapped in colour tags. Cached
        /// because it is otherwise rebuilt on every entry and there are only five variants.
        /// </summary>
        string WrappedArgumentFormat(LogType logType) {
            var cache = colorWrappedFormats;
            if (cache == null) {
                cache = new string[5];
                for (var i = 0; i < cache.Length; i++) cache[i] = ModifyFormat((LogType)i, "{0}");
                colorWrappedFormats = cache;
            }
            return cache[(int)logType];
        }

        string ModifyFormat(LogType logType, string format) {
            if (!useColors) return format;

            // Plain informational entries in the default colour are left alone, so the console's
            // own styling (and its search) is not disturbed for the common case.
            var shouldWrap = logType != LogType.Log || infoColor != InfoColor;
            if (!shouldWrap) return format;

            var color = logType switch {
                LogType.Error or LogType.Exception => errorColor,
                LogType.Warning => warningColor,
                LogType.Assert => assertColor,
                _ => '#' + ColorUtility.ToHtmlStringRGB(infoColor)
            };

            var builder = formatBuilder ??= new StringBuilder();
            builder.Clear();
            builder.Append("<color=").Append(color).Append('>').Append(format).Append("</color>");
            return builder.ToString();
        }
    }
}
