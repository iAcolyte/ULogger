#nullable enable

using System;
using System.Diagnostics;
using System.Threading;

using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

using Debug = UnityEngine.Debug;

namespace ULogger {
    /// <summary>A log destination that has work it can only do on the main thread.</summary>
    public interface IMainThreadPump {
        /// <summary>Cheap check; the dispatcher skips the whole frame when nothing has work.</summary>
        bool HasPendingWork { get; }

        /// <summary>Called once per frame on the main thread. Never called re-entrantly.</summary>
        void Pump();
    }

    /// <summary>
    /// Runs queued log work on the main thread, driven from an injected <see cref="PlayerLoop"/>
    /// system (no GameObject, no scene, survives scene loads by construction).
    /// <para>
    /// Unity's console itself tolerates background threads, but the <c>UnityEngine.Object</c> a log
    /// entry is attached to does not, and entries emitted off-thread surface out of order. Handlers
    /// therefore render on the calling thread and hand the finished entry over to here.
    /// </para>
    /// </summary>
    public static class MainThreadDispatcher {
        static int mainThreadId = -1;
        static IMainThreadPump[] pumps = Array.Empty<IMainThreadPump>();
        static readonly object gate = new();

        // Indexed by (int)LogType: Error, Assert, Warning, Log, Exception.
        static readonly StackTraceLogType[] stackTraceSettings = new StackTraceLogType[5];

        static bool pumping;

        /// <summary>True when called from the thread that runs the Unity player loop.</summary>
        public static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == mainThreadId;

        /// <summary>False until the player loop has been installed; before that nothing can be pumped.</summary>
        public static bool IsInstalled => mainThreadId != -1;

        public static void Register(IMainThreadPump pump) {
            if (pump == null) throw new ArgumentNullException(nameof(pump));
            lock (gate) {
                foreach (var existing in pumps)
                    if (ReferenceEquals(existing, pump)) return;

                // Copy-on-write: Pump() reads the array without a lock, on the hot path of every frame.
                var next = new IMainThreadPump[pumps.Length + 1];
                Array.Copy(pumps, next, pumps.Length);
                next[pumps.Length] = pump;
                pumps = next;
            }
        }

        public static void Unregister(IMainThreadPump pump) {
            lock (gate) {
                var index = Array.IndexOf(pumps, pump);
                if (index < 0) return;

                var next = new IMainThreadPump[pumps.Length - 1];
                Array.Copy(pumps, next, index);
                Array.Copy(pumps, index + 1, next, index, pumps.Length - index - 1);
                pumps = next;
            }
        }

        /// <summary>
        /// Captures a stack trace for an entry logged off the main thread, honouring the project's
        /// per-<see cref="LogType"/> Stack Trace setting. Returns null when that setting is None.
        /// <para>
        /// Only managed frames are available, so a "Full" setting is served the same as "ScriptOnly";
        /// the native half of the trace cannot be reconstructed from here.
        /// </para>
        /// </summary>
        public static string? CaptureStackTrace(LogType logType, int skipFrames) {
            if (stackTraceSettings[(int)logType] == StackTraceLogType.None) return null;
            return new StackTrace(skipFrames + 1, fNeedFileInfo: true).ToString();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Install() {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            RefreshStackTraceSettings();

            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (!TryInsertPump(ref loop)) {
                Debug.LogWarning("[ULogger] PostLateUpdate not found in the player loop; " +
                                 "log entries from background threads will not be flushed.");
                return;
            }
            PlayerLoop.SetPlayerLoop(loop);

            // The player loop stops before the process does, so the tail of the queue would be lost.
            Application.quitting -= Pump;
            Application.quitting += Pump;
        }

        static bool TryInsertPump(ref PlayerLoopSystem loop) {
            var subSystems = loop.subSystemList;
            if (subSystems == null) return false;

            for (var i = 0; i < subSystems.Length; i++) {
                if (subSystems[i].type != typeof(PostLateUpdate)) continue;

                var children = subSystems[i].subSystemList ?? Array.Empty<PlayerLoopSystem>();

                // Re-entering play mode with domain reload disabled keeps the previous injection.
                foreach (var child in children)
                    if (child.type == typeof(MainThreadDispatcher)) return true;

                var next = new PlayerLoopSystem[children.Length + 1];
                Array.Copy(children, next, children.Length);
                next[children.Length] = new PlayerLoopSystem {
                    type = typeof(MainThreadDispatcher),
                    updateDelegate = Pump
                };
                subSystems[i].subSystemList = next;
                return true;
            }
            return false;
        }

        static void Pump() {
            // A pump that logs would re-enter through the handler it is draining.
            if (pumping) return;

            var snapshot = pumps;
            var hasWork = false;
            foreach (var pump in snapshot) {
                if (!pump.HasPendingWork) continue;
                hasWork = true;
                break;
            }
            if (!hasWork) return;

            pumping = true;
            RefreshStackTraceSettings();

            // Every entry about to be emitted already carries the stack trace of the thread that
            // logged it. Leaving Unity's capture on would append a second, useless trace pointing
            // at this dispatcher.
            SetStackTraceSettings(StackTraceLogType.None);
            try {
                foreach (var pump in snapshot) {
                    try {
                        pump.Pump();
                    } catch (Exception e) {
                        // Never let one destination take down the frame.
                        Debug.LogException(e);
                    }
                }
            } finally {
                RestoreStackTraceSettings();
                pumping = false;
            }
        }

        static void RefreshStackTraceSettings() {
            for (var i = 0; i < stackTraceSettings.Length; i++)
                stackTraceSettings[i] = Application.GetStackTraceLogType((LogType)i);
        }

        static void SetStackTraceSettings(StackTraceLogType value) {
            for (var i = 0; i < stackTraceSettings.Length; i++)
                Application.SetStackTraceLogType((LogType)i, value);
        }

        static void RestoreStackTraceSettings() {
            for (var i = 0; i < stackTraceSettings.Length; i++)
                Application.SetStackTraceLogType((LogType)i, stackTraceSettings[i]);
        }

#if UNITY_EDITOR
        // The player loop does not run outside play mode, so edit-mode logging from a background
        // thread (import jobs, editor tooling) would sit in the queue indefinitely.
        [UnityEditor.InitializeOnLoadMethod]
        static void InstallEditorPump() {
            if (mainThreadId == -1) mainThreadId = Thread.CurrentThread.ManagedThreadId;
            RefreshStackTraceSettings();
            UnityEditor.EditorApplication.update -= Pump;
            UnityEditor.EditorApplication.update += Pump;
        }
#endif
    }
}
