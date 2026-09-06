using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using ULogger;

/// <summary>
/// The lock-free multi-producer queue behind background console logging. Pure C#, no Unity.
/// </summary>
public class SwapQueueTests {
    static List<T> Drain<T>(SwapQueue<T> queue) {
        var result = new List<T>();
        var count = queue.BeginDrain(out var items);
        try {
            for (var i = 0; i < count; i++) result.Add(items[i]);
        } finally {
            queue.EndDrain();
        }
        return result;
    }

    [Test]
    public void Constructor_RejectsNonPositiveCapacity() {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SwapQueue<int>(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SwapQueue<int>(-1));
    }

    [Test]
    public void Enqueue_AcceptsExactlyCapacity() {
        var queue = new SwapQueue<int>(3);

        Assert.IsTrue(queue.Enqueue(1));
        Assert.IsTrue(queue.Enqueue(2));
        Assert.IsTrue(queue.Enqueue(3));
        Assert.IsFalse(queue.Enqueue(4), "The fourth entry must not fit into a capacity of three.");

        Assert.AreEqual(3, queue.Capacity);
        Assert.AreEqual(1, queue.DroppedCount);
    }

    [Test]
    public void Drain_PreservesOrderAndStopsAtCapacity() {
        var queue = new SwapQueue<int>(3);
        for (var i = 0; i < 10; i++) queue.Enqueue(i);

        // Not 10, and not the inflated Count that the failed reservations briefly produced.
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, Drain(queue));
        Assert.AreEqual(7, queue.DroppedCount);
    }

    [Test]
    public void Drain_LeavesQueueEmptyAndReusable() {
        var queue = new SwapQueue<int>(2);
        queue.Enqueue(1);

        Assert.IsTrue(queue.HasEntries);
        CollectionAssert.AreEqual(new[] { 1 }, Drain(queue));
        Assert.IsFalse(queue.HasEntries);

        // The buckets swap, so the full capacity must be available again.
        Assert.IsTrue(queue.Enqueue(2));
        Assert.IsTrue(queue.Enqueue(3));
        CollectionAssert.AreEqual(new[] { 2, 3 }, Drain(queue));
    }

    [Test]
    public void OverflowDoesNotCorruptTheNextRound() {
        var queue = new SwapQueue<int>(2);

        // Repeated overflow is where an unbalanced Count would accumulate and make a later
        // BeginDrain report more entries than were ever written.
        for (var round = 0; round < 5; round++) {
            for (var i = 0; i < 50; i++) queue.Enqueue(i);
            CollectionAssert.AreEqual(new[] { 0, 1 }, Drain(queue));
        }
    }

    [Test]
    public void TakeDropped_ResetsTheCounter() {
        var queue = new SwapQueue<int>(1);
        queue.Enqueue(1);
        queue.Enqueue(2);
        queue.Enqueue(3);

        Assert.AreEqual(2, queue.DroppedCount);
        Assert.AreEqual(2, queue.TakeDropped());
        Assert.AreEqual(0, queue.DroppedCount);
        Assert.AreEqual(0, queue.TakeDropped(), "A drop must be reported once, not every frame.");
    }

    [Test]
    public void EndDrain_ReleasesReferencesToDrainedEntries() {
        var queue = new SwapQueue<object>(4);

        var reference = EnqueueTemporary(queue);

        // Drained without materializing the entries into a list: that list would itself hold the
        // reference and the assertion below would prove nothing.
        queue.BeginDrain(out _);
        queue.EndDrain();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Without the Array.Clear in EndDrain the slot pins the entry until the bucket is
        // refilled, which for a rarely used queue means forever.
        Assert.IsFalse(reference.IsAlive, "A drained entry is still reachable from the bucket.");
    }

    // Separate method so the local holding the object is out of scope (and out of the frame's
    // GC roots) by the time the collection runs.
    static WeakReference EnqueueTemporary(SwapQueue<object> queue) {
        var item = new object();
        queue.Enqueue(item);
        return new WeakReference(item);
    }

    [Test]
    public void HasEntries_IsFalseWhileEmpty() {
        var queue = new SwapQueue<int>(4);
        Assert.IsFalse(queue.HasEntries);
        queue.Enqueue(1);
        Assert.IsTrue(queue.HasEntries);
    }

    [Test]
    public void ConcurrentProducers_LoseNothingUnaccountedFor() {
        const int producers = 4;
        const int perProducer = 5_000;
        const int total = producers * perProducer;

        var queue = new SwapQueue<int>(64);
        var delivered = new List<int>(total);
        var accepted = 0;
        var done = 0;

        var writers = new Task[producers];
        for (var p = 0; p < producers; p++) {
            var offset = p * perProducer;
            writers[p] = Task.Run(() => {
                for (var i = 0; i < perProducer; i++)
                    if (queue.Enqueue(offset + i)) Interlocked.Increment(ref accepted);
                Interlocked.Increment(ref done);
            });
        }

        DrainUntilProducersFinish(queue, delivered, () => Volatile.Read(ref done) == producers);
        Assert.IsTrue(Task.WaitAll(writers, TimeSpan.FromSeconds(30)), "A producer never finished.");
        delivered.AddRange(Drain(queue));

        Assert.AreEqual(total, accepted + queue.DroppedCount,
            "Every entry must be either accepted or counted as dropped.");
        Assert.AreEqual(accepted, delivered.Count,
            "Every accepted entry must come out of exactly one drain.");
        CollectionAssert.AllItemsAreUnique(delivered, "A slot was handed to two producers.");
    }

    [Test]
    public void ConcurrentProducers_NeverSeeATornSlot() {
        // A reference type makes a lost write visible as a null rather than as a plausible int.
        const int producers = 4;
        const int perProducer = 5_000;

        var queue = new SwapQueue<string>(32);
        var delivered = new List<string>();
        var done = 0;

        var writers = new Task[producers];
        for (var p = 0; p < producers; p++) {
            var name = "p" + p;
            writers[p] = Task.Run(() => {
                for (var i = 0; i < perProducer; i++) queue.Enqueue(name + ":" + i);
                Interlocked.Increment(ref done);
            });
        }

        DrainUntilProducersFinish(queue, delivered, () => Volatile.Read(ref done) == producers);
        Assert.IsTrue(Task.WaitAll(writers, TimeSpan.FromSeconds(30)), "A producer never finished.");
        delivered.AddRange(Drain(queue));

        CollectionAssert.DoesNotContain(delivered, null,
            "A drain read a slot whose Count was already published but whose item was not.");
        foreach (var entry in delivered) StringAssert.Contains(":", entry);
    }

    /// <summary>
    /// Drains on a slow cadence until the producers are done, then returns.
    /// <para>
    /// The pause is not politeness, it is required. Every BeginDrain flips activeIndex, and a
    /// producer that observes the flip mid-reservation gives up its slot and retries; draining in
    /// a tight loop flips faster than the producers can ever complete a reservation, and they
    /// livelock. The real consumer drains once per frame, which is why this never shows up in
    /// production -- but a test must fail on a deadline rather than hang the editor.
    /// </para>
    /// </summary>
    static void DrainUntilProducersFinish<T>(SwapQueue<T> queue, List<T> delivered, Func<bool> finished) {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (!finished()) {
            delivered.AddRange(Drain(queue));
            Assert.Less(DateTime.UtcNow, deadline, "The producers made no progress within 30 seconds.");
            Thread.Sleep(1);
        }
    }
}
