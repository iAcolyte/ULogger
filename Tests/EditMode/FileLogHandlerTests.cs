using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

using NUnit.Framework;

using ULogger;

using UnityEngine;

/// <summary>The UTF-8 file sink: entry layout, tag format, truncation and level filtering.</summary>
public class FileLogHandlerTests {
    string directory;
    readonly List<UnityEngine.Object> created = new();

    [SetUp]
    public void SetUp() {
        directory = Path.Combine(Path.GetTempPath(), "ULoggerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    [TearDown]
    public void TearDown() {
        foreach (var instance in created) TestConfig.Destroy(instance);
        created.Clear();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    string LogPath => Path.Combine(directory, "log.txt");

    FileLogHandler Make(params (string field, object value)[] fields) {
        var all = new List<(string, object)> { ("path", LogPath) };
        all.AddRange(fields);
        var handler = TestConfig.Create<FileLogHandler>(all.ToArray());
        created.Add(handler);
        return handler;
    }

    /// <summary>Closes the writer (which flushes synchronously) and returns what landed on disk.</summary>
    string[] Flush(FileLogHandler handler) {
        ((IDisposable)handler).Dispose();
        Assert.IsTrue(File.Exists(LogPath), "The handler never opened its log file.");
        return File.ReadAllLines(LogPath, Encoding.UTF8);
    }

    // ------------------------------------------------------------------ path

    [Test]
    public void ExpandsPersistentDataPathToken() {
        var handler = Make(("path", "%pdp/sub/log.txt"));
        var resolved = (string)TestConfig.Get(handler, "_path");

        StringAssert.StartsWith(Application.persistentDataPath, resolved);
        StringAssert.EndsWith("/sub/log.txt", resolved);
    }

    [Test]
    public void ExpandsDataPathToken() {
        var handler = Make(("path", "%dp/log.txt"));
        StringAssert.StartsWith(Application.dataPath, (string)TestConfig.Get(handler, "_path"));
    }

    [Test]
    public void ExpandsDateTimeToken() {
        var handler = Make(("path", Path.Combine(directory, "%dt.txt")));
        var resolved = Path.GetFileNameWithoutExtension((string)TestConfig.Get(handler, "_path"));

        Assert.IsTrue(DateTime.TryParseExact(resolved, "yyyy_MM_dd_HH_mm_ss", null,
            System.Globalization.DateTimeStyles.None, out _), $"'{resolved}' is not a timestamp.");
    }

    // ------------------------------------------------------------------ entry layout

    [Test]
    public void WritesOneLinePerEntry() {
        var handler = Make(("appendTimeFormat", ""), ("appendLogLevel", false));

        handler.Write(LogLevel.Info, default, "one".AsSpan(), null);
        handler.Write(LogLevel.Info, default, "two".AsSpan(), null);

        CollectionAssert.AreEqual(new[] { "\"one\"", "\"two\"" }, Flush(handler));
    }

    [Test]
    public void PrependsTheLevelLabel() {
        var handler = Make(("appendTimeFormat", ""));

        handler.Write(LogLevel.Info, default, "i".AsSpan(), null);
        handler.Write(LogLevel.Warning, default, "w".AsSpan(), null);
        handler.Write(LogLevel.Error, default, "e".AsSpan(), null);
        handler.Write(LogLevel.Critical, default, "c".AsSpan(), null);

        var lines = Flush(handler);
        StringAssert.StartsWith("INFO\t", lines[0]);
        StringAssert.StartsWith("WARNING\t", lines[1]);
        StringAssert.StartsWith("ERROR\t", lines[2]);
        StringAssert.StartsWith("FATAL\t", lines[3]);
    }

    [Test]
    public void PrependsTheTimestamp() {
        var handler = Make(("appendTimeFormat", "yyyy-MM-dd"), ("appendLogLevel", false));

        handler.Write(LogLevel.Info, default, "message".AsSpan(), null);

        var line = Flush(handler)[0];
        // The compiled formatter emits the timestamp followed by a single space.
        StringAssert.StartsWith(DateTime.Now.ToString("yyyy-MM-dd") + " ", line);
    }

    [Test]
    public void OmitsTheTimestampWhenTheFormatIsEmpty() {
        var handler = Make(("appendTimeFormat", ""), ("appendLogLevel", false));

        handler.Write(LogLevel.Info, default, "message".AsSpan(), null);

        Assert.AreEqual("\"message\"", Flush(handler)[0]);
    }

    // ------------------------------------------------------------------ tags

    [Test]
    public void AppliesTagFormatOnTheSpanPath() {
        var handler = Make(("appendTimeFormat", ""), ("appendLogLevel", false), ("tagFormat", "<{0}|{1}>"));

        handler.Write(LogLevel.Info, "net".AsSpan(), "message".AsSpan(), null);

        Assert.AreEqual("<net|message>", Flush(handler)[0]);
    }

    [Test]
    public void AppliesTheSameTagFormatOnBothPaths() {
        var handler = Make(("appendTimeFormat", ""), ("appendLogLevel", false), ("tagFormat", "<{0}|{1}>"));

        // The span path used to hardcode [tag] "message" and diverged as soon as tagFormat changed.
        handler.Write(LogLevel.Info, "net".AsSpan(), "message".AsSpan(), null);
        handler.LogFormat(LogType.Log, null, "{0}: {1}", "net", "message");

        var lines = Flush(handler);
        Assert.AreEqual(lines[0], lines[1]);
    }

    [Test]
    public void EmptyTagFormatFallsBackToUnitysFormOnBothPaths() {
        var handler = Make(("appendTimeFormat", ""), ("appendLogLevel", false), ("tagFormat", ""));

        // Empty used to mean "net: message" on one path and [net] "message" on the other.
        handler.Write(LogLevel.Info, "net".AsSpan(), "message".AsSpan(), null);
        handler.LogFormat(LogType.Log, null, "{0}: {1}", "net", "message");

        CollectionAssert.AreEqual(new[] { "net: message", "net: message" }, Flush(handler));
    }

    // ------------------------------------------------------------------ formats

    [Test]
    public void KeepsTheCallersOwnFormat() {
        var handler = Make(("appendTimeFormat", ""), ("appendLogLevel", false));

        // Substituting tagFormat here threw away the literal text of every real LogFormat call.
        handler.LogFormat(LogType.Log, null, "hp {0} of {1}", 3, 10);

        Assert.AreEqual("hp 3 of 10", Flush(handler)[0]);
    }

    [Test]
    public void QuotesThePlainDebugLogShape() {
        var handler = Make(("appendTimeFormat", ""), ("appendLogLevel", false));

        // Debug.Log(message) reaches every handler as LogFormat(type, ctx, "{0}", message).
        handler.LogFormat(LogType.Log, null, "{0}", "message");

        Assert.AreEqual("\"message\"", Flush(handler)[0]);
    }

    [Test]
    public void FormatsEnumsByName() {
        var handler = Make(("appendTimeFormat", ""), ("appendLogLevel", false));

        handler.LogFormat(LogType.Log, null, "level {0}", LogLevel.Warning);

        Assert.AreEqual("level Warning", Flush(handler)[0]);
    }

    [Test]
    public void FormatsFloatsAtFloatPrecision() {
        var handler = Make(("appendTimeFormat", ""), ("appendLogLevel", false));

        handler.LogFormat(LogType.Log, null, "{0} {1}", 0.1f, 0.1d);

        Assert.AreEqual("0.1 0.1", Flush(handler)[0]);
    }

    // ------------------------------------------------------------------ text handling

    [Test]
    public void FlattensMultilineMessages() {
        var handler = Make(("appendTimeFormat", ""), ("appendLogLevel", false), ("flattenMultiline", true));

        handler.Write(LogLevel.Info, default, "a\r\nb\nc".AsSpan(), null);

        var lines = Flush(handler);
        Assert.AreEqual(1, lines.Length, "A flattened entry must occupy exactly one line.");
        Assert.AreEqual("\"a  b c\"", lines[0]);
    }

    [Test]
    public void RoundTripsNonAsciiText() {
        var handler = Make(("appendTimeFormat", ""), ("appendLogLevel", false));

        handler.Write(LogLevel.Info, default, "привет 🌍".AsSpan(), null);

        Assert.AreEqual("\"привет 🌍\"", Flush(handler)[0]);
    }

    [Test]
    public void TruncatesOversizedMessagesWithoutSplittingAUtf8Sequence() {
        var handler = Make(("appendTimeFormat", ""), ("appendLogLevel", false), ("maxMessageBytes", 64));

        // Two bytes per character, so a naive cut lands in the middle of one of them.
        handler.Write(LogLevel.Info, default, new string('я', 200).AsSpan(), null);

        var line = Flush(handler)[0];
        StringAssert.EndsWith("...[truncated]", line);
        Assert.IsFalse(line.Contains('�'), "The cut split a UTF-8 sequence.");
    }

    // ------------------------------------------------------------------ filtering

    [Test]
    public void DropsEntriesBelowMinLevel() {
        var handler = Make(("appendTimeFormat", ""), ("appendLogLevel", false), ("minLevel", LogLevel.Warning));

        handler.Write(LogLevel.Info, default, "dropped".AsSpan(), null);
        handler.Write(LogLevel.Warning, default, "kept".AsSpan(), null);

        CollectionAssert.AreEqual(new[] { "\"kept\"" }, Flush(handler));
    }

    [Test]
    public void KeepsExceptionsAtAnyMinLevelBelowOff() {
        var handler = Make(("appendTimeFormat", ""), ("minLevel", LogLevel.Critical));

        handler.WriteException(new InvalidOperationException("boom"), null);

        var lines = Flush(handler);
        Assert.AreEqual(1, lines.Length);
        StringAssert.Contains("boom", lines[0]);
    }

    [Test]
    public void DeduplicatesByPathRatherThanByType() {
        var a = Make(("appendTimeFormat", ""), ("appendLogLevel", false));
        var b = Make(("appendTimeFormat", ""), ("appendLogLevel", false));
        var composite = TestConfig.Create<CompositeLogHandler>(
            ("logHandlers", new List<ULogHandler> { a, b }));
        created.Add(composite);

        // Two handlers pointing at the same file are one destination, so the entry must be written
        // once, not twice.
        composite.LogFormat(LogType.Log, null, "{0}", "once");

        ((IDisposable)a).Dispose();
        ((IDisposable)b).Dispose();
        CollectionAssert.AreEqual(new[] { "\"once\"" }, File.ReadAllLines(LogPath, Encoding.UTF8));
    }

    [Test]
    public void SurvivesConcurrentWriters() {
        const int threads = 4;
        const int perThread = 250;

        var handler = Make(("appendTimeFormat", ""), ("appendLogLevel", false));
        var workers = new Thread[threads];

        for (var t = 0; t < threads; t++) {
            var id = t;
            workers[t] = new Thread(() => {
                for (var i = 0; i < perThread; i++)
                    handler.Write(LogLevel.Info, default, $"t{id}-{i}".AsSpan(), null);
            });
            workers[t].Start();
        }
        foreach (var worker in workers) worker.Join();

        var lines = Flush(handler);
        Assert.AreEqual(threads * perThread, lines.Length);
        foreach (var line in lines) StringAssert.IsMatch("^\"t[0-3]-[0-9]+\"$", line);
    }
}
