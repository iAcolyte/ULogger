#nullable enable

using System;
using System.Threading.Tasks;

using UnityEngine;

namespace ULogger.MonoLogger
{
    /// <summary>
    /// The ILogSink side of the package: the direct API, which formats into a pooled buffer instead
    /// of building a string and boxing every argument the way Debug.LogFormat does.
    /// <para>
    /// The handler comes from Resources, the same asset <see cref="ULoggerBootstrap"/> installs, so
    /// the sample has exactly one place that decides which chain is in use.
    /// </para>
    /// </summary>
    public sealed class SinkShowcase : MonoBehaviour
    {
        // TaggedLogSink is a struct that carries the tag and the context: binding them once and
        // keeping the result in a field costs nothing per call.
        TaggedLogSink gameplay;

        void Start()
        {
            // Typed as ULogHandler rather than ILogSink: the null check has to go through Unity's
            // own operator, which an interface-typed reference would bypass.
            var handler = Resources.Load<ULogHandler>("CompositeLH");
            if (handler == null)
            {
                Debug.LogWarning("SinkShowcase: 'CompositeLH' not found in Resources.");
                return;
            }

            ILogSink sink = handler;
            gameplay = sink.WithTag("GAMEPLAY").For(this);

            // The generic overloads take up to four arguments of any type without boxing them: the
            // argument types are known at the call site, so the formatting is compiled per call.
            sink.Info("started on frame {0}, delta {1}", Time.frameCount, Time.deltaTime);

            // "GAMEPLAY" is not the tag 'ConsoleLH TAG' subscribes to, so the console drops these,
            // while FileLH has no tag filter and writes them with its own tagFormat.
            gameplay.Info("player {0} entered zone {1}", "p1", 3);
            gameplay.Warning("inventory is {0}% full", 92.5f);

            // IsEnabled before formatting: the idiom for a message whose arguments are expensive to
            // produce. A disabled level then costs one virtual call and nothing else.
            if (sink.IsEnabled(LogLevel.Trace)) sink.Trace("scene dump: {0}", DescribeScene());

            try
            {
                throw new InvalidOperationException("Sample sink exception, nothing is broken.");
            }
            catch (InvalidOperationException e)
            {
                // Same policy as Debug.LogException: governed by logExceptions, never tag-filtered.
                sink.Exception(e, this);
            }

            LogFromWorkerThread(sink);
        }

        // ConsoleLogHandler cannot touch the Unity console off the main thread, so it parks the entry
        // in a lock-free queue and MainThreadDispatcher delivers it on the next frame; that is why
        // this line appears after the ones above even though it is written first. FileLogHandler has
        // no such restriction and writes from whatever thread called it.
        //
        // The task is deliberately not awaited and not cancelled: if the scene unloads first, the
        // entry simply arrives to a chain nobody is looking at any more.
        static void LogFromWorkerThread(ILogSink sink) =>
            Task.Run(() => sink.Warning("written from a worker thread, delivered on the next frame"));

        string DescribeScene() => $"{name} at {transform.position}";
    }
}
