#nullable enable

using System;
using System.Collections.Generic;

using UnityEngine;

namespace ULogger {
    [CreateAssetMenu(fileName = "Composite LogHandler", menuName = "ULogger/Composite Log Handler")]
    public sealed class CompositeLogHandler: ULogHandler {
        // Composites currently dispatching on this thread. A cycle in the handler graph (A -> B -> A)
        // would otherwise recurse until the stack dies, and the base class cannot catch it: it marks
        // an entry as dispatched only after the nested call returns. Per-thread because a dispatch
        // never spans threads, and handlers may be driven from background threads.
        [ThreadStatic] static HashSet<CompositeLogHandler>? dispatching;

        [SerializeField] private List<ULogHandler?> logHandlers = new();
        public IReadOnlyList<ULogHandler?> LogHandlers => logHandlers;

        // A composite only routes logs, it is not a destination itself, so it must never be
        // deduplicated away (that would drop nested composites of the same type).
        protected override object DedupScope => this;

        void OnValidate() {
            // Reference data is left untouched: silently nulling a slot loses the user's edit and
            // gives no hint why. Cycles are broken at dispatch time instead.
            for (int i = 0; i < logHandlers.Count; i++) {
                var handler = logHandlers[i];
                if (handler == null) continue;

                if (handler == this) {
                    Debug.LogWarning($"{name}: handler at index {i} references the composite itself; it will be skipped.", this);
                    continue;
                }
                for (int j = 0; j < i; j++) {
                    if (logHandlers[j] != handler) continue;
                    Debug.LogWarning($"{name}: handler '{handler.name}' is listed twice (indices {j} and {i}).", this);
                    break;
                }
            }
        }

        protected override void LogExceptionInherit(Exception exception, UnityEngine.Object? context) {
            var active = dispatching ??= new HashSet<CompositeLogHandler>();
            if (!active.Add(this)) return;
            try {
                foreach (var logHandler in logHandlers) logHandler?.LogException(exception, context);
            } finally {
                active.Remove(this);
            }
        }

        protected override bool IsEnabledInherit(LogLevel level) {
            // Guarded like the dispatch paths: a cycle here recurses just as fatally, and it is
            // reached first -- Write asks IsEnabled before it ever enters WriteInherit.
            var active = dispatching ??= new HashSet<CompositeLogHandler>();
            if (!active.Add(this)) return false;
            try {
                foreach (var logHandler in logHandlers)
                    if (logHandler != null && logHandler.IsEnabled(level)) return true;
                return false;
            } finally {
                active.Remove(this);
            }
        }

        protected override void WriteInherit(LogLevel level, ReadOnlySpan<char> tag, ReadOnlySpan<char> message, UnityEngine.Object? context) {
            var active = dispatching ??= new HashSet<CompositeLogHandler>();
            if (!active.Add(this)) return;
            try {
                foreach (var logHandler in logHandlers) logHandler?.Write(level, tag, message, context);
            } finally {
                active.Remove(this);
            }
        }

        protected override bool LogFormatInherit(LogType logType, UnityEngine.Object? context, string format, params object[] args) {
            var active = dispatching ??= new HashSet<CompositeLogHandler>();
            if (!active.Add(this)) return false;

            var handled = false;
            try {
                foreach (var logHandler in logHandlers) {
                    if (logHandler == null) continue;
                    logHandler.LogFormat(logType, context, format, args);
                    handled = true;
                }
            } finally {
                active.Remove(this);
            }
            return handled;
        }
    }
}
