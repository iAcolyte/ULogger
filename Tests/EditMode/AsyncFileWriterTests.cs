using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

using NUnit.Framework;

using ULogger;

/// <summary>The background file writer: its bounded double buffer and its drop accounting.</summary>
public class AsyncFileWriterTests {
    const int Capacity = 4096;

    string directory;

    [SetUp]
    public void SetUp() {
        directory = Path.Combine(Path.GetTempPath(), "ULoggerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    [TearDown]
    public void TearDown() {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    string PathFor(string name) => Path.Combine(directory, name);

    static void Write(AsyncFileWriter writer, string text, bool urgent = false) =>
        writer.Enqueue(Encoding.UTF8.GetBytes(text), urgent);

    [Test]
    public void Constructor_RejectsATooSmallCapacity() {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AsyncFileWriter(PathFor("a.txt"), 128));
    }

    [Test]
    public void Constructor_CreatesMissingDirectories() {
        var path = PathFor(Path.Combine("nested", "deeper", "log.txt"));
        using (var writer = new AsyncFileWriter(path, Capacity)) {
            Write(writer, "message\n", urgent: true);
        }
        Assert.IsTrue(File.Exists(path));
    }

    [Test]
    public void WritesInOrderAndFlushesOnDispose() {
        var path = PathFor("log.txt");
        using (var writer = new AsyncFileWriter(path, Capacity)) {
            for (var i = 0; i < 20; i++) Write(writer, $"line {i}\n");
        }

        var lines = File.ReadAllLines(path);
        Assert.AreEqual(20, lines.Length);
        for (var i = 0; i < 20; i++) Assert.AreEqual($"line {i}", lines[i]);
    }

    [Test]
    public void AppendsToAnExistingFile() {
        var path = PathFor("log.txt");
        using (var writer = new AsyncFileWriter(path, Capacity)) Write(writer, "first\n");
        using (var writer = new AsyncFileWriter(path, Capacity)) Write(writer, "second\n");

        CollectionAssert.AreEqual(new[] { "first", "second" }, File.ReadAllLines(path));
    }

    [Test]
    public void IgnoresEmptyMessages() {
        var path = PathFor("log.txt");
        using (var writer = new AsyncFileWriter(path, Capacity)) {
            writer.Enqueue(ReadOnlySpan<byte>.Empty, urgent: true);
            Write(writer, "only\n", urgent: true);
        }
        CollectionAssert.AreEqual(new[] { "only" }, File.ReadAllLines(path));
    }

    [Test]
    public void IgnoresWritesAfterDispose() {
        var path = PathFor("log.txt");
        var writer = new AsyncFileWriter(path, Capacity);
        Write(writer, "before\n", urgent: true);
        writer.Dispose();

        Assert.DoesNotThrow(() => Write(writer, "after\n"));
        CollectionAssert.AreEqual(new[] { "before" }, File.ReadAllLines(path));
    }

    [Test]
    public void DisposeIsIdempotent() {
        var writer = new AsyncFileWriter(PathFor("log.txt"), Capacity);
        writer.Dispose();
        Assert.DoesNotThrow(writer.Dispose);
    }

    [Test]
    public void TruncatesAMessageLongerThanTheBuffer() {
        var path = PathFor("log.txt");
        using (var writer = new AsyncFileWriter(path, Capacity)) {
            Write(writer, new string('x', Capacity * 2) + "\n", urgent: true);
        }

        var text = File.ReadAllText(path);
        Assert.LessOrEqual(text.Length, Capacity);
        StringAssert.Contains("[truncated]", text);
    }

    [Test]
    public void ReportsOverflowInsteadOfBlocking() {
        var path = PathFor("log.txt");
        var writer = new AsyncFileWriter(path, Capacity);

        // Pre-encoded, and exactly half a buffer each: two fill the active buffer and the third
        // has nowhere to go. Building the message inside the loop -- an interpolation plus a
        // UTF-8 encode per iteration -- made the producer slower than the writer thread, which
        // then kept up and nothing was ever dropped.
        var payload = Encoding.UTF8.GetBytes(new string('x', Capacity / 2 - 1) + "\n");
        Assert.AreEqual(Capacity / 2, payload.Length);

        for (var i = 0; i < 2_000; i++) writer.Enqueue(payload, urgent: false);
        writer.Dispose();

        Assert.Greater(writer.DroppedCount, 0, "A flood must be dropped, not buffered without bound.");
        StringAssert.Contains("[ULogger] dropped", File.ReadAllText(path),
            "A drop must leave a trace in the log itself.");
    }

    [Test]
    public void SurvivesConcurrentProducers() {
        const int producers = 4;
        const int perProducer = 50;

        var path = PathFor("log.txt");
        var writer = new AsyncFileWriter(path, 1 << 16);
        var threads = new Thread[producers];

        for (var p = 0; p < producers; p++) {
            var id = p;
            threads[p] = new Thread(() => {
                for (var i = 0; i < perProducer; i++) Write(writer, $"p{id}-{i:D4}\n");
            });
            threads[p].Start();
        }
        foreach (var thread in threads) thread.Join();
        writer.Dispose();

        var lines = new List<string>(File.ReadAllLines(path));
        Assert.AreEqual(0, writer.DroppedCount, "The buffer was sized to hold everything.");
        Assert.AreEqual(producers * perProducer, lines.Count);

        // Interleaving is expected; a torn or overlapping reservation is not.
        foreach (var line in lines) StringAssert.IsMatch("^p[0-3]-[0-9]{4}$", line);
        CollectionAssert.AllItemsAreUnique(lines);
    }

    [Test]
    public void ExposesItsSizing() {
        using var writer = new AsyncFileWriter(PathFor("log.txt"), Capacity);
        Assert.AreEqual(Capacity, writer.Capacity);
        Assert.AreEqual(Capacity / 2, writer.FlushThreshold);
        Assert.AreEqual(0, writer.WriteErrorCount);
    }
}
