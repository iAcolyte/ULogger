using System;
using System.Collections.Generic;

using NUnit.Framework;

using ULogger;

using UnityEngine;

/// <summary>Fan-out and, above all, the cycle guard that keeps a bad graph from eating the stack.</summary>
public class CompositeLogHandlerTests {
    readonly List<UnityEngine.Object> created = new();

    T Make<T>(params (string field, object value)[] fields) where T : ScriptableObject {
        var instance = TestConfig.Create<T>(fields);
        created.Add(instance);
        return instance;
    }

    CompositeLogHandler Composite(params ULogHandler[] children) =>
        Make<CompositeLogHandler>(("logHandlers", new List<ULogHandler>(children)));

    [TearDown]
    public void TearDown() {
        foreach (var instance in created) TestConfig.Destroy(instance);
        created.Clear();
    }

    [Test]
    public void ForwardsToEveryChild() {
        var a = Make<RecordingLogHandler>();
        var b = Make<OtherRecordingLogHandler>();
        var composite = Composite(a, b);

        composite.Write(LogLevel.Info, default, "message".AsSpan(), null);

        Assert.AreEqual(1, a.Writes.Count);
        Assert.AreEqual(1, b.Writes.Count);
    }

    [Test]
    public void SkipsEmptySlots() {
        var a = Make<RecordingLogHandler>();
        var composite = Composite(a, null);

        // An unassigned slot in the inspector is left alone rather than silently pruned, so the
        // dispatch path has to tolerate it.
        Assert.DoesNotThrow(() => composite.Write(LogLevel.Info, default, "message".AsSpan(), null));
        Assert.AreEqual(1, a.Writes.Count);
    }

    [Test]
    public void ForwardsExceptions() {
        var a = Make<RecordingLogHandler>();
        var composite = Composite(a);

        composite.WriteException(new InvalidOperationException(), null);

        Assert.AreEqual(1, a.Exceptions.Count);
    }

    [Test]
    public void IsEnabled_IsTrueWhenAnyChildIsEnabled() {
        var quiet = Make<RecordingLogHandler>(("MinLevel", LogLevel.Error));
        var loud = Make<OtherRecordingLogHandler>(("MinLevel", LogLevel.Trace));

        Assert.IsTrue(Composite(quiet, loud).IsEnabled(LogLevel.Info));
        Assert.IsFalse(Composite(quiet).IsEnabled(LogLevel.Info));
    }

    [Test]
    public void IsEnabled_IsFalseWhenEmpty() {
        Assert.IsFalse(Composite().IsEnabled(LogLevel.Info));
    }

    [Test]
    public void BreaksASelfReference() {
        var sink = Make<RecordingLogHandler>();
        var composite = Composite();
        TestConfig.Set(composite, "logHandlers", new List<ULogHandler> { sink, composite });

        Assert.DoesNotThrow(() => composite.Write(LogLevel.Info, default, "message".AsSpan(), null));
        Assert.AreEqual(1, sink.Writes.Count);
    }

    [Test]
    public void BreaksATwoStepCycle() {
        var sink = Make<RecordingLogHandler>();
        var a = Composite();
        var b = Composite();

        // A -> B -> A. Without the per-thread guard this recurses until the stack dies, and the
        // base class cannot help: it marks an entry dispatched only after the nested call returns.
        TestConfig.Set(a, "logHandlers", new List<ULogHandler> { sink, b });
        TestConfig.Set(b, "logHandlers", new List<ULogHandler> { a });

        Assert.DoesNotThrow(() => a.Write(LogLevel.Info, default, "message".AsSpan(), null));
        Assert.AreEqual(1, sink.Writes.Count);
    }

    [Test]
    public void BreaksACycleOnEveryPath() {
        var sink = Make<RecordingLogHandler>();
        var a = Composite();
        var b = Composite();

        TestConfig.Set(a, "logHandlers", new List<ULogHandler> { sink, b });
        TestConfig.Set(b, "logHandlers", new List<ULogHandler> { a });

        Assert.DoesNotThrow(() => a.LogFormat(LogType.Log, null, "{0}", "message"));
        Assert.DoesNotThrow(() => a.WriteException(new InvalidOperationException(), null));

        Assert.AreEqual(1, sink.Formats.Count);
        Assert.AreEqual(1, sink.Exceptions.Count);
    }

    [Test]
    public void BreaksACycleReachedThroughIsEnabled() {
        var a = Composite();
        var b = Composite();

        // No sink to short-circuit on, so IsEnabledInherit walks the cycle itself. This is the
        // first thing Write touches, before any of the dispatch guards come into play.
        TestConfig.Set(a, "logHandlers", new List<ULogHandler> { b });
        TestConfig.Set(b, "logHandlers", new List<ULogHandler> { a });

        Assert.DoesNotThrow(() => Assert.IsFalse(a.IsEnabled(LogLevel.Info)));
        Assert.DoesNotThrow(() => a.Write(LogLevel.Info, default, "message".AsSpan(), null));
    }

    [Test]
    public void GuardIsReleasedAfterEachDispatch() {
        var sink = Make<RecordingLogHandler>();
        var composite = Composite(sink);

        // The guard is a set the composite adds itself to; a missing removal would silence every
        // entry after the first.
        composite.Write(LogLevel.Info, default, "one".AsSpan(), null);
        composite.Write(LogLevel.Info, default, "two".AsSpan(), null);

        CollectionAssert.AreEqual(new[] { "one", "two" }, sink.Writes);
    }

    [Test]
    public void NestedCompositesOfTheSameTypeAreNotDeduplicatedAway() {
        var a = Make<RecordingLogHandler>();
        var b = Make<OtherRecordingLogHandler>();
        var inner = Composite(b);
        var outer = Composite(a, inner);

        // DedupScope is the instance, not the type, precisely so one composite does not look like
        // a duplicate of another.
        outer.LogFormat(LogType.Log, null, "{0}", "message");

        Assert.AreEqual(1, a.Formats.Count);
        Assert.AreEqual(1, b.Formats.Count);
    }
}
