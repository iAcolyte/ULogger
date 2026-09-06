using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

using NUnit.Framework;

using ULogger;

using UnityEngine;
using UnityEngine.TestTools;

// UnityEngine.PlayerLoop is both a namespace and a class; the namespace wins name lookup, so the
// class is reached through an alias here exactly as it is in the dispatcher itself.
using PlayerLoopApi = UnityEngine.LowLevel.PlayerLoop;
using PlayerLoopSystem = UnityEngine.LowLevel.PlayerLoopSystem;

/// <summary>
/// The player-loop pump and the console handler's background queue. Play mode, because none of
/// this exists without a running player loop.
/// </summary>
public class MainThreadDispatcherTests {
    readonly List<UnityEngine.Object> created = new();
    readonly List<(string message, string stackTrace, LogType type)> received = new();

    StackTraceLogType[] savedStackTraces;

    [SetUp]
    public void SetUp() {
        savedStackTraces = new StackTraceLogType[5];
        for (var i = 0; i < savedStackTraces.Length; i++)
            savedStackTraces[i] = Application.GetStackTraceLogType((LogType)i);

        received.Clear();
        Application.logMessageReceived += OnLogMessage;
    }

    [TearDown]
    public void TearDown() {
        Application.logMessageReceived -= OnLogMessage;
        LogAssert.ignoreFailingMessages = false;

        foreach (var instance in created) TestConfig.Destroy(instance);
        created.Clear();

        // The dispatcher flips these process-wide while draining; a test that fails mid-drain must
        // not leave the project's setting changed for every test after it.
        for (var i = 0; i < savedStackTraces.Length; i++)
            Application.SetStackTraceLogType((LogType)i, savedStackTraces[i]);
    }

    void OnLogMessage(string message, string stackTrace, LogType type) =>
        received.Add((message, stackTrace, type));

    ConsoleLogHandler MakeConsole(params (string field, object value)[] fields) {
        var handler = TestConfig.Create<ConsoleLogHandler>(fields);
        created.Add(handler);
        return handler;
    }

    /// <summary>Logs from a thread that is definitely not the player loop's, and waits for it.</summary>
    static void LogFromBackground(Action action) {
        var task = Task.Run(action);
        Assert.IsTrue(task.Wait(TimeSpan.FromSeconds(5)), "The background thread blocked; a log call must never wait.");
        Assert.IsNull(task.Exception, task.Exception?.ToString());
    }

    IEnumerable<string> Messages => received.ConvertAll(entry => entry.message);

    // ------------------------------------------------------------------ installation

    [Test]
    public void IsInstalledOnTheMainThread() {
        Assert.IsTrue(MainThreadDispatcher.IsInstalled);
        Assert.IsTrue(MainThreadDispatcher.IsMainThread);
    }

    [Test]
    public void ReportsBackgroundThreadsAsSuch() {
        var onMainThread = true;
        LogFromBackground(() => onMainThread = MainThreadDispatcher.IsMainThread);
        Assert.IsFalse(onMainThread);
    }

    [Test]
    public void IsInjectedIntoPostLateUpdateExactlyOnce() {
        var injections = 0;

        foreach (var system in PlayerLoopApi.GetCurrentPlayerLoop().subSystemList) {
            if (system.type != typeof(UnityEngine.PlayerLoop.PostLateUpdate)) continue;
            foreach (var child in system.subSystemList ?? Array.Empty<PlayerLoopSystem>())
                if (child.type == typeof(MainThreadDispatcher)) injections++;
        }

        // Re-entering play mode with domain reload disabled must not stack a second copy.
        Assert.AreEqual(1, injections, "Expected exactly one dispatcher system under PostLateUpdate.");
    }

    // ------------------------------------------------------------------ delivery

    [UnityTest]
    public IEnumerator MainThreadEntriesAreEmittedImmediately() {
        var handler = MakeConsole(("minLevel", LogLevel.Trace));
        yield return null;
        received.Clear();

        handler.Write(LogLevel.Info, default, "immediate".AsSpan(), null);

        // No frame in between: the main thread has no reason to queue.
        CollectionAssert.Contains(Messages, "immediate");
        Assert.IsFalse(handler.HasPendingWork);
    }

    [UnityTest]
    public IEnumerator BackgroundEntriesAreQueuedUntilTheNextFrame() {
        var handler = MakeConsole(("minLevel", LogLevel.Trace));
        yield return null;
        received.Clear();

        LogFromBackground(() => handler.Write(LogLevel.Info, default, "deferred".AsSpan(), null));

        Assert.IsTrue(handler.HasPendingWork, "The entry should still be waiting for the pump.");
        CollectionAssert.DoesNotContain(Messages, "deferred");

        yield return null;

        Assert.IsFalse(handler.HasPendingWork);
        CollectionAssert.Contains(Messages, "deferred");
    }

    [UnityTest]
    public IEnumerator BackgroundEntriesKeepTheirOrder() {
        var handler = MakeConsole(("minLevel", LogLevel.Trace));
        yield return null;
        received.Clear();

        LogFromBackground(() => {
            for (var i = 0; i < 5; i++) handler.Write(LogLevel.Info, default, $"entry {i}".AsSpan(), null);
        });
        yield return null;

        var delivered = received.ConvertAll(entry => entry.message).FindAll(m => m.StartsWith("entry "));
        CollectionAssert.AreEqual(new[] { "entry 0", "entry 1", "entry 2", "entry 3", "entry 4" }, delivered);
    }

    [UnityTest]
    public IEnumerator BackgroundExceptionsAreDelivered() {
        var handler = MakeConsole(("minLevel", LogLevel.Trace));
        yield return null;
        received.Clear();

        // Delivered as text, not through LogException: the original object is gone by then, and
        // Unity would re-capture a trace pointing at the dispatcher. Reset in TearDown -- a
        // yield cannot sit inside a try/finally.
        LogAssert.ignoreFailingMessages = true;

        LogFromBackground(() => handler.WriteException(new InvalidOperationException("boom"), null));
        yield return null;

        Assert.IsTrue(received.Exists(entry => entry.message.Contains("boom")),
            "The exception never reached the console.");
    }

    // ------------------------------------------------------------------ overflow

    [UnityTest]
    public IEnumerator OverflowIsReportedOncePerOccurrence() {
        var handler = MakeConsole(("minLevel", LogLevel.Trace), ("backgroundQueueCapacity", 4));
        yield return null;
        received.Clear();

        LogFromBackground(() => {
            for (var i = 0; i < 20; i++) handler.Write(LogLevel.Info, default, $"flood {i}".AsSpan(), null);
        });
        yield return null;

        var delivered = received.FindAll(entry => entry.message.StartsWith("flood "));
        Assert.AreEqual(4, delivered.Count, "The queue must hold exactly its capacity.");

        var warnings = received.FindAll(entry =>
            entry.type == LogType.Warning && entry.message.Contains("dropped 16"));
        Assert.AreEqual(1, warnings.Count, "Expected exactly one drop report.");
        // The report must name the real capacity, not whatever the inspector field says.
        StringAssert.Contains("queue capacity 4", warnings[0].message);

        received.Clear();
        yield return null;

        Assert.AreEqual(0, received.FindAll(entry => entry.message.Contains("dropped")).Count,
            "A drop was reported again on the next frame.");
    }

    [UnityTest]
    public IEnumerator BookkeepingSettlesAfterAnOverflow() {
        var handler = MakeConsole(("minLevel", LogLevel.Trace), ("backgroundQueueCapacity", 2));
        yield return null;

        LogFromBackground(() => {
            for (var i = 0; i < 10; i++) handler.Write(LogLevel.Info, default, "flood".AsSpan(), null);
        });
        yield return null;
        yield return null;

        // Whatever happened to the entries, the bookkeeping has to settle: a stuck drop counter
        // makes the dispatcher run a full stack-trace save/restore cycle every single frame.
        Assert.IsFalse(handler.HasPendingWork);
    }

    // ------------------------------------------------------------------ stack traces

    [UnityTest]
    public IEnumerator BackgroundEntriesCarryTheirOwnStackTrace() {
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.ScriptOnly);
        var handler = MakeConsole(("minLevel", LogLevel.Trace), ("captureBackgroundStackTrace", true));

        // The dispatcher caches the project's setting once per frame, so let it observe the change.
        yield return null;
        received.Clear();

        LogFromBackground(() => LogFromANamedFrame(handler));
        yield return null;

        var entry = received.Find(item => item.message.StartsWith("traced"));
        Assert.IsNotNull(entry.message, "The traced entry never arrived.");
        StringAssert.Contains(nameof(LogFromANamedFrame), entry.message,
            "The trace captured at the call site was not attached to the message.");
    }

    static void LogFromANamedFrame(ConsoleLogHandler handler) =>
        handler.Write(LogLevel.Info, default, "traced".AsSpan(), null);

    [UnityTest]
    public IEnumerator NoStackTraceIsCapturedWhenTheProjectSaysNone() {
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
        var handler = MakeConsole(("minLevel", LogLevel.Trace), ("captureBackgroundStackTrace", true));

        yield return null;
        received.Clear();

        LogFromBackground(() => LogFromANamedFrame(handler));
        yield return null;

        var entry = received.Find(item => item.message.StartsWith("traced"));
        Assert.AreEqual("traced", entry.message, "A trace was attached although the project asked for none.");
    }

    [UnityTest]
    public IEnumerator TheProjectStackTraceSettingIsRestoredAfterADrain() {
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.ScriptOnly);
        var handler = MakeConsole(("minLevel", LogLevel.Trace));
        yield return null;

        LogFromBackground(() => handler.Write(LogLevel.Info, default, "entry".AsSpan(), null));
        yield return null;

        // The drain suppresses Unity's own capture process-wide; leaving it off would silently
        // strip traces from everything else in the project.
        Assert.AreEqual(StackTraceLogType.ScriptOnly, Application.GetStackTraceLogType(LogType.Log));
    }

    // ------------------------------------------------------------------ registration

    [UnityTest]
    public IEnumerator AnUnregisteredHandlerIsNoLongerPumped() {
        var handler = MakeConsole(("minLevel", LogLevel.Trace));
        yield return null;
        received.Clear();

        LogFromBackground(() => handler.Write(LogLevel.Info, default, "flushed".AsSpan(), null));

        // OnDisable drains what is left before it unregisters, so nothing is silently lost.
        created.Remove(handler);
        TestConfig.Destroy(handler);

        CollectionAssert.Contains(Messages, "flushed");
        yield return null;
    }

    [Test]
    public void RegisterIsIdempotent() {
        var handler = MakeConsole();

        // OnEnable already registered it; a second call must not make the pump run twice.
        Assert.DoesNotThrow(() => MainThreadDispatcher.Register(handler));
        Assert.DoesNotThrow(() => MainThreadDispatcher.Unregister(handler));
        Assert.DoesNotThrow(() => MainThreadDispatcher.Unregister(handler));
    }

    [Test]
    public void RegisterRejectsNull() {
        Assert.Throws<ArgumentNullException>(() => MainThreadDispatcher.Register(null));
    }
}
