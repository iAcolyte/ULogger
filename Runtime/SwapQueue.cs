#nullable enable

using System;
using System.Threading;

namespace ULogger {
    /// <summary>
    /// A bounded multi-producer / single-consumer queue. Producers never block and never allocate:
    /// a slot is reserved with a single Interlocked increment. The consumer swaps the two buckets
    /// and drains the filled one, which is why exactly one thread may drain at a time.
    /// <para>
    /// Same shape as <see cref="AsyncFileWriter"/>'s double buffer, for the same reason: a log call
    /// must never wait, and losing an entry under a flood beats stalling the caller.
    /// </para>
    /// </summary>
    internal sealed class SwapQueue<T> {
        readonly Bucket[] buckets;
        readonly int capacity;

        volatile int activeIndex;
        int dropped;
        Bucket? draining;

        /// <summary>Number of entries lost because the active bucket was full.</summary>
        public int DroppedCount => Volatile.Read(ref dropped);

        /// <summary>Approximate fill level of the active bucket. Only ever grows stale, never lies about emptiness.</summary>
        public bool HasEntries => Volatile.Read(ref buckets[activeIndex].Count) > 0;

        public SwapQueue(int capacity) {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.capacity = capacity;
            buckets = new[] { new Bucket(capacity), new Bucket(capacity) };
        }

        /// <summary>Queues an entry. Returns false if the bucket was full and the entry was dropped.</summary>
        public bool Enqueue(in T item) {
            while (true) {
                var idx = activeIndex;
                var bucket = buckets[idx];

                // Claim writer status BEFORE re-reading activeIndex. The consumer writes activeIndex
                // before reading Writers, so one of the two is guaranteed to observe the other.
                Interlocked.Increment(ref bucket.Writers);
                if (activeIndex != idx) {
                    Interlocked.Decrement(ref bucket.Writers);
                    continue;
                }

                var slot = Interlocked.Increment(ref bucket.Count) - 1;
                if (slot < capacity) {
                    bucket.Items[slot] = item;
                    Interlocked.Decrement(ref bucket.Writers);
                    return true;
                }

                // Full: undo the reservation, otherwise Count grows without bound and overflows int.
                Interlocked.Decrement(ref bucket.Count);
                Interlocked.Decrement(ref bucket.Writers);
                Interlocked.Increment(ref dropped);
                return false;
            }
        }

        /// <summary>
        /// Swaps buckets and hands the filled one to the caller. Must be paired with <see cref="EndDrain"/>,
        /// and only one thread may be inside a drain at a time.
        /// </summary>
        public int BeginDrain(out T[] items) {
            var idx = activeIndex;
            var bucket = buckets[idx];
            activeIndex = idx ^ 1;

            // Wait out producers that entered this bucket before the swap.
            var spin = new SpinWait();
            while (Volatile.Read(ref bucket.Writers) != 0) spin.SpinOnce();

            draining = bucket;
            items = bucket.Items;
            return Math.Min(Volatile.Read(ref bucket.Count), capacity);
        }

        public void EndDrain() {
            var bucket = draining;
            if (bucket == null) return;
            draining = null;

            // Clear the slots: entries hold managed references (strings, Unity objects) that would
            // otherwise be kept alive until the bucket is refilled.
            Array.Clear(bucket.Items, 0, capacity);
            Volatile.Write(ref bucket.Count, 0);
        }

        /// <summary>Reads and resets the drop counter, so a report is emitted once per occurrence.</summary>
        public int TakeDropped() => Interlocked.Exchange(ref dropped, 0);

        sealed class Bucket {
            public readonly T[] Items;
            public int Count;
            public int Writers;

            public Bucket(int capacity) => Items = new T[capacity];
        }
    }
}
