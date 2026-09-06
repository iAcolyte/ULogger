#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

using UnityEngine;
using UnityEngine.PlayerLoop;

// UnityEngine.PlayerLoop is both a namespace and, in UnityEngine.LowLevel, a class. A namespace
// wins name lookup, so the class has to be reached through an alias.
using PlayerLoopApi = UnityEngine.LowLevel.PlayerLoop;
using PlayerLoopSystem = UnityEngine.LowLevel.PlayerLoopSystem;

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
    /// Runs queued log work on the main thread, driven from an injected player loop system
    /// (no GameObject, no scene, survives scene loads by construction).
    /// <para>
    /// Unity's console itself tolerates background threads; what does not is the
    /// <c>UnityEngine.Object</c> a log entry is attached to (its null check is only meaningful on
    /// the main thread) and the stack trace Unity captures, which would describe the background
    /// thread's frames or none at all. Handlers therefore render on the calling thread and hand the
    /// finished entry over to here.
    /// </para>
    /// <para>
    /// This deliberately does not give cross-thread ordering: main-thread entries are emitted
    /// immediately, background entries at the end of the frame, so a background entry logged
    /// earlier can surface after a main-thread one logged later.
    /// </para>
    /// </summary>
    public static class MainThreadDispatcher {
        static int mainThreadId = -1;
        static IMainThreadPump[] pumps = Array.Empty<IMainThreadPump>();
        static readonly object gate = new();

        // Indexed by (int)LogType: Error, Assert, Warning, Log, Exception.
        static readonly StackTraceLogType[] stackTraceSettings = new StackTraceLogType[5];

        static bool pumping;

        // Exceptions thrown by a pump, logged once the stack-trace settings are back.
        static List<Exception>? failures;

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
                // ReferenceEquals, matching Register: UnityEngine.Object overrides Equals, and a
                // destroyed handler would otherwise compare equal to the wrong entry.
                var index = -1;
                for (var i = 0; i < pumps.Length; i++) {
                    if (!ReferenceEquals(pumps[i], pump)) continue;
                    index = i;
                    break;
                }
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
        /// <para>
        /// Reads a cache refreshed once per frame rather than calling into Unity, which is not safe
        /// from a background thread. A change to the project setting therefore takes effect on the
        /// next frame, not on the next entry.
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

            var loop = PlayerLoopApi.GetCurrentPlayerLoop();
            if (!TryInsertPump(ref loop)) {
                Debug.LogWarning("[ULogger] PostLateUpdate not found in the player loop; " +
                                 "log entries from background threads will not be flushed.");
                return;
            }
            PlayerLoopApi.SetPlayerLoop(loop);

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
            if (snapshot.Length == 0) return;

            // Refreshed even on an idle frame, and before the early-out: background threads read
            // this cache when they capture a trace, which happens long before the drain that would
            // otherwise refresh it. Five property reads, and only while a pump is registered.
            RefreshStackTraceSettings();

            var hasWork = false;
            foreach (var pump in snapshot) {
                if (!pump.HasPendingWork) continue;
                hasWork = true;
                break;
            }
            if (!hasWork) return;

            pumping = true;

            // Every entry about to be emitted already carries the stack trace of the thread that
            // logged it. Leaving Unity's capture on would append a second, useless trace pointing
            // at this dispatcher. The setting is process-wide, which is why the window is kept as
            // narrow as possible and nothing else is logged inside it.
            SetStackTraceSettings(StackTraceLogType.None);
            try {
                foreach (var pump in snapshot) {
                    try {
                        pump.Pump();
                    } catch (Exception e) {
                        // Never let one destination take down the frame. Held back rather than
                        // logged here: inside the window it would lose its own stack trace, which
                        // is the only useful part of it.
                        (failures ??= new List<Exception>()).Add(e);
                    }
                }
            } finally {
                RestoreStackTraceSettings();
                pumping = false;
            }

            ReportFailures();
        }

        /// <summary>
        /// Drains a single pump outside the player loop, with the same stack-trace suppression.
        /// For teardown paths (<c>OnDisable</c>) that would otherwise flush entries with a bogus
        /// trace pointing at the teardown itself.
        /// </summary>
        internal static void PumpOnce(IMainThreadPump pump) {
            if (pumping || !pump.HasPendingWork) return;

            pumping = true;
            RefreshStackTraceSettings();
            SetStackTraceSettings(StackTraceLogType.None);
            try {
                pump.Pump();
            } catch (Exception e) {
                (failures ??= new List<Exception>()).Add(e);
            } finally {
                RestoreStackTraceSettings();
                pumping = false;
            }

            ReportFailures();
        }

        static void ReportFailures() {
            var pending = failures;
            if (pending == null || pending.Count == 0) return;

            failures = null;
            foreach (var failure in pending) Debug.LogException(failure);
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
            UnityEditor.EditorApplication.update -= EditorPump;
            UnityEditor.EditorApplication.update += EditorPump;
        }

        // In play mode the player loop already pumps; running here too would double the
        // stack-trace setting churn every frame for no gain.
        static void EditorPump() {
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;
            Pump();
        }
#endif
    }
}
