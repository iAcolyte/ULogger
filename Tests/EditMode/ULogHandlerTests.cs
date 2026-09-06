using System;
using System.Collections.Generic;

using NUnit.Framework;

using ULogger;

using UnityEngine;

/// <summary>
/// The contract every handler inherits: level filtering, tag matching, exception policy and
/// per-dispatch deduplication.
/// </summary>
public class ULogHandlerTests {
    readonly List<UnityEngine.Object> created = new();

    T Make<T>(params (string field, object value)[] fields) where T : ScriptableObject {
        var instance = TestConfig.Create<T>(fields);
        created.Add(instance);
        return instance;
    }

    [TearDown]
    public void TearDown() {
        foreach (var instance in created) TestConfig.Destroy(instance);
        created.Clear();
    }

    // ------------------------------------------------------------------ level filtering

    [Test]
    public void IsEnabled_ComparesAgainstMinLevel() {
        var handler = Make<ConsoleLogHandler>(("minLevel", LogLevel.Warning));

        Assert.IsFalse(handler.IsEnabled(LogLevel.Info));
        Assert.IsTrue(handler.IsEnabled(LogLevel.Warning));
        Assert.IsTrue(handler.IsEnabled(LogLevel.Critical));
    }

    [Test]
    public void IsEnabled_IsAlwaysFalseForOff() {
        // Off is a filter value, never the level of an entry, so it must not pass its own filter.
        var handler = Make<ConsoleLogHandler>(("minLevel", LogLevel.Off));
        Assert.IsFalse(handler.IsEnabled(LogLevel.Off));
        Assert.IsFalse(handler.IsEnabled(LogLevel.Critical));
    }

    [Test]
    public void Write_IsDroppedBelowMinLevel() {
        var handler = Make<RecordingLogHandler>(("MinLevel", LogLevel.Warning));

        handler.Write(LogLevel.Info, default, "dropped".AsSpan(), null);
        handler.Write(LogLevel.Warning, default, "kept".AsSpan(), null);

        CollectionAssert.AreEqual(new[] { "kept" }, handler.Writes);
    }

    // ------------------------------------------------------------------ severity mapping

    [Test]
    public void ToLogType_MapsOntoUnitysCoarserScale() {
        Assert.AreEqual(LogType.Log, ULogHandler.ToLogType(LogLevel.Trace));
        Assert.AreEqual(LogType.Log, ULogHandler.ToLogType(LogLevel.Debug));
        Assert.AreEqual(LogType.Log, ULogHandler.ToLogType(LogLevel.Info));
        Assert.AreEqual(LogType.Warning, ULogHandler.ToLogType(LogLevel.Warning));
        Assert.AreEqual(LogType.Error, ULogHandler.ToLogType(LogLevel.Error));
        Assert.AreEqual(LogType.Exception, ULogHandler.ToLogType(LogLevel.Critical));
    }

    [Test]
    public void ToLogLevel_MapsBack() {
        Assert.AreEqual(LogLevel.Info, ULogHandler.ToLogLevel(LogType.Log));
        Assert.AreEqual(LogLevel.Warning, ULogHandler.ToLogLevel(LogType.Warning));
        Assert.AreEqual(LogLevel.Error, ULogHandler.ToLogLevel(LogType.Error));
        Assert.AreEqual(LogLevel.Error, ULogHandler.ToLogLevel(LogType.Assert));
        Assert.AreEqual(LogLevel.Critical, ULogHandler.ToLogLevel(LogType.Exception));
    }

    // ------------------------------------------------------------------ tags

    [Test]
    public void Write_WithoutTagsConfigured_AcceptsEverything() {
        var handler = Make<RecordingLogHandler>();

        handler.Write(LogLevel.Info, default, "untagged".AsSpan(), null);
        handler.Write(LogLevel.Info, "net".AsSpan(), "tagged".AsSpan(), null);

        Assert.AreEqual(2, handler.Writes.Count);
    }

    [Test]
    public void Write_WithTagsConfigured_AcceptsOnlyKnownTags() {
        var handler = Make<RecordingLogHandler>(("tags", new[] { "net", "ui" }));

        handler.Write(LogLevel.Info, "net".AsSpan(), "kept".AsSpan(), null);
        handler.Write(LogLevel.Info, "audio".AsSpan(), "dropped".AsSpan(), null);
        handler.Write(LogLevel.Info, default, "untagged".AsSpan(), null);

        CollectionAssert.AreEqual(new[] { "[net] kept" }, handler.Writes);
    }

    [Test]
    public void LogFormat_WithTagsConfigured_RecognizesUnitysTaggedForm() {
        var handler = Make<RecordingLogHandler>(("tags", new[] { "net" }));

        // This is the exact shape Unity's Logger.Log(tag, message) produces.
        handler.LogFormat(LogType.Log, null, "{0}: {1}", "net", "kept");

        CollectionAssert.AreEqual(new[] { "net: kept" }, handler.Formats);
    }

    [Test]
    public void LogFormat_WithTagsConfigured_DoesNotTreatEveryTwoArgumentCallAsTagged() {
        var handler = Make<RecordingLogHandler>(("tags", new[] { "net" }));

        // Two arguments whose first happens to be the known tag, but a format of the caller's own.
        // Reading the tag out of args[0] regardless of format made this look like a tagged call.
        handler.LogFormat(LogType.Log, null, "{0} -> {1}", "net", "value");

        Assert.AreEqual(0, handler.Formats.Count,
            "A genuine LogFormat was mistaken for Unity's tag+message form.");
    }

    [Test]
    public void LogFormat_WithTagsConfigured_RejectsUnknownTags() {
        var handler = Make<RecordingLogHandler>(("tags", new[] { "net" }));

        handler.LogFormat(LogType.Log, null, "{0}: {1}", "audio", "dropped");
        handler.LogFormat(LogType.Log, null, "{0}", "untagged");

        Assert.AreEqual(0, handler.Formats.Count);
    }

    // ------------------------------------------------------------------ exceptions

    [Test]
    public void Exceptions_PassAtAnyMinLevelBelowOff() {
        // They map to LogLevel.Critical; filtering them at Error dropped them whenever a handler
        // was configured for Critical only.
        foreach (var level in new[] { LogLevel.Trace, LogLevel.Warning, LogLevel.Error, LogLevel.Critical }) {
            var handler = Make<RecordingLogHandler>(("MinLevel", level));

            handler.WriteException(new InvalidOperationException("sink"), null);
            handler.LogException(new InvalidOperationException("handler"), null);

            Assert.AreEqual(2, handler.Exceptions.Count, $"minLevel {level} swallowed an exception.");
        }
    }

    [Test]
    public void Exceptions_AreSuppressedByOff() {
        var handler = Make<RecordingLogHandler>(("MinLevel", LogLevel.Off));

        handler.WriteException(new InvalidOperationException(), null);
        handler.LogException(new InvalidOperationException(), null);

        Assert.AreEqual(0, handler.Exceptions.Count);
    }

    [Test]
    public void Exceptions_AreSuppressedByTheLogExceptionsFlag() {
        var handler = Make<RecordingLogHandler>(("logExceptions", false));

        // Both entry points obey one flag; previously only the file handler had one at all.
        handler.WriteException(new InvalidOperationException(), null);
        handler.LogException(new InvalidOperationException(), null);

        Assert.AreEqual(0, handler.Exceptions.Count);
    }

    [Test]
    public void WriteException_IgnoresTheTagFilter() {
        var handler = Make<RecordingLogHandler>(("tags", new[] { "net" }));

        // An exception carries no tag, and a tagged handler swallowing every exception is worse
        // than one seeing an exception it did not ask for.
        handler.WriteException(new InvalidOperationException(), null);

        Assert.AreEqual(1, handler.Exceptions.Count);
    }

    // ------------------------------------------------------------------ deduplication

    [Test]
    public void Deduplication_AppliesWithinOneDispatch() {
        var a = Make<RecordingLogHandler>();
        var b = Make<RecordingLogHandler>();
        var composite = Make<CompositeLogHandler>(("logHandlers", new List<ULogHandler> { a, b }));

        composite.LogFormat(LogType.Log, null, "{0}", "once");

        // Same type, so a shared dedup scope: the entry is a duplicate for the second instance.
        Assert.AreEqual(1, a.Formats.Count + b.Formats.Count);
    }

    [Test]
    public void Deduplication_DoesNotSpanSeparateDispatches() {
        var handler = Make<RecordingLogHandler>();

        handler.LogFormat(LogType.Log, null, "{0}", "twice");
        handler.LogFormat(LogType.Log, null, "{0}", "twice");

        // Dedup exists to stop one entry fanning out to the same destination twice, not to
        // collapse a caller logging the same text repeatedly.
        Assert.AreEqual(2, handler.Formats.Count);
    }

    [Test]
    public void Deduplication_IsScopedPerHandlerType() {
        var a = Make<RecordingLogHandler>();
        var b = Make<OtherRecordingLogHandler>();
        var composite = Make<CompositeLogHandler>(("logHandlers", new List<ULogHandler> { a, b }));

        composite.LogFormat(LogType.Log, null, "{0}", "both");

        Assert.AreEqual(1, a.Formats.Count);
        Assert.AreEqual(1, b.Formats.Count);
    }

    [Test]
    public void Deduplication_DistinguishesDifferentArguments() {
        var a = Make<RecordingLogHandler>();
        var b = Make<RecordingLogHandler>();
        var composite = Make<CompositeLogHandler>(("logHandlers", new List<ULogHandler> { a, b }));

        composite.LogFormat(LogType.Log, null, "{0}", "first");
        composite.LogFormat(LogType.Log, null, "{0}", "second");

        Assert.AreEqual(2, a.Formats.Count + b.Formats.Count);
    }

    [Test]
    public void Write_IsNotDeduplicated() {
        var a = Make<RecordingLogHandler>();
        var b = Make<RecordingLogHandler>();
        var composite = Make<CompositeLogHandler>(("logHandlers", new List<ULogHandler> { a, b }));

        // The span path carries no dedup at all: hashing spans would defeat its whole purpose.
        composite.Write(LogLevel.Info, default, "message".AsSpan(), null);

        Assert.AreEqual(1, a.Writes.Count);
        Assert.AreEqual(1, b.Writes.Count);
    }
}
