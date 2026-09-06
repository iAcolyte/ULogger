using System;
using System.IO;
using System.Text;
using System.Threading;

namespace ULogger {
    /// <summary>
    /// Writes UTF-8 log messages to a file from a background thread.
    /// The hot path (<see cref="Enqueue"/>) is allocation-free and non-blocking: a message reserves
    /// a range in the active buffer via Interlocked and copies itself into it. The writer thread
    /// periodically swaps the active and shadow buffers and flushes the filled one to disk.
    /// </summary>
    internal sealed class AsyncFileWriter : IDisposable {
        private const int MinCapacity = 4096;
        private const int IdleWaitMs = 200;
        private const int JoinTimeoutMs = 2000;

        private static readonly byte[] TruncationMarker = Encoding.UTF8.GetBytes("…[truncated]\n");

        private readonly LogBuffer[] buffers;
        private readonly ManualResetEventSlim signal = new(false, 0);
        private readonly Thread thread;
        private readonly FileStream stream;
        private readonly int capacity;
        private readonly int flushThreshold;

        private volatile int activeIndex;
        private volatile bool running;
        private int disposed;
        private int dropped;
        private int writeErrors;
        private int reportedDrops;

        /// <summary>Size of each of the two buffers, in bytes.</summary>
        public int Capacity => capacity;

        /// <summary>Fill level of the active buffer at which the writer thread is woken up.</summary>
        public int FlushThreshold => flushThreshold;

        /// <summary>Number of messages lost to buffer overflow.</summary>
        public int DroppedCount => Volatile.Read(ref dropped);

        /// <summary>Number of times a write to the file threw.</summary>
        public int WriteErrorCount => Volatile.Read(ref writeErrors);

        public AsyncFileWriter(string path, int capacity = 1 << 16) {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (capacity < MinCapacity)
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity,
                    $"Capacity must be at least {MinCapacity} bytes.");

            this.capacity = capacity;
            flushThreshold = capacity / 2;
            buffers = new[] {
                new LogBuffer(capacity),
                new LogBuffer(capacity)
            };

            var info = new FileInfo(path);
            info.Directory?.Create();
            stream = new FileStream(path, FileMode.Append,
                FileAccess.Write, FileShare.ReadWrite,
                bufferSize: 1, FileOptions.None);

            running = true;
            thread = new Thread(WriterLoop) {
                IsBackground = true,
                Priority = System.Threading.ThreadPriority.BelowNormal,
                Name = "ULogger.FileWriter"
            };
            thread.Start();
        }

        /// <summary>
        /// Queues a UTF-8 message for writing. Allocation-free and never waits on I/O.
        /// Messages longer than <see cref="Capacity"/> are truncated and marked as such.
        /// </summary>
        /// <param name="urgent">Wake the writer thread immediately instead of waiting for the threshold.</param>
        public void Enqueue(ReadOnlySpan<byte> utf8, bool urgent) {
            if (utf8.IsEmpty || Volatile.Read(ref disposed) != 0) return;

            var payload = utf8;
            bool truncated = false;
            if (payload.Length > capacity) {
                payload = payload.Slice(0, capacity - TruncationMarker.Length);
                truncated = true;
            }
            int size = payload.Length + (truncated ? TruncationMarker.Length : 0);

            while (true) {
                int idx = activeIndex;
                var buf = buffers[idx];

                // Claim writer status BEFORE re-reading activeIndex. The swapper writes activeIndex
                // before reading Writers, so one of the two is guaranteed to observe the other
                // (Interlocked and volatile both issue full fences).
                Interlocked.Increment(ref buf.Writers);
                if (activeIndex != idx) {
                    Interlocked.Decrement(ref buf.Writers);
                    continue;
                }

                int end = Interlocked.Add(ref buf.Length, size);
                int start = end - size;

                if (end <= capacity) {
                    var dst = new Span<byte>(buf.Data, start, size);
                    payload.CopyTo(dst);
                    if (truncated) new ReadOnlySpan<byte>(TruncationMarker).CopyTo(dst.Slice(payload.Length));
                    Interlocked.Decrement(ref buf.Writers);

                    if (urgent || end >= flushThreshold) Signal();
                    return;
                }

                // Buffer is full: undo the reservation, otherwise Length grows without bound
                // and eventually overflows int.
                Interlocked.Add(ref buf.Length, -size);
                Interlocked.Decrement(ref buf.Writers);
                Interlocked.Increment(ref dropped);
                Signal();
                return;
            }
        }

        private void Signal() {
            // Races with Dispose: the event may already be disposed.
            try { signal.Set(); } catch (ObjectDisposedException) { }
        }

        private void WriterLoop() {
            while (running) {
                try {
                    signal.Wait(IdleWaitMs);
                    // Reset BEFORE flushing so a Set() arriving during the flush is not lost.
                    signal.Reset();
                } catch (ObjectDisposedException) {
                    // Dispose gave up on Join and disposed the event; drain and exit.
                    break;
                }
                SwapAndFlush(fsync: false);
            }
            // Final drain of both buffers; this thread is the only one allowed to swap.
            SwapAndFlush(fsync: true);
            SwapAndFlush(fsync: true);
        }

        private void SwapAndFlush(bool fsync) {
            int idx = activeIndex;
            var buf = buffers[idx];

            activeIndex = idx ^ 1;

            // Wait out writers that entered the buffer before the swap.
            var spin = new SpinWait();
            while (Volatile.Read(ref buf.Writers) != 0) spin.SpinOnce();

            int len = Math.Min(Volatile.Read(ref buf.Length), capacity);
            bool wrote = false;

            if (len > 0) {
                wrote = TryWrite(buf.Data, 0, len);
                Volatile.Write(ref buf.Length, 0);
            }

            wrote |= ReportDrops();

            if (wrote) {
                try { stream.Flush(fsync); } catch { Interlocked.Increment(ref writeErrors); }
            }
        }

        private bool TryWrite(byte[] data, int offset, int count) {
            try {
                stream.Write(data, offset, count);
                return true;
            } catch {
                Interlocked.Increment(ref writeErrors);
                return false;
            }
        }

        private bool ReportDrops() {
            int total = Volatile.Read(ref dropped);
            int delta = total - reportedDrops;
            if (delta <= 0) return false;

            reportedDrops = total;
            byte[] notice = Encoding.UTF8.GetBytes(
                $"[ULogger] dropped {delta} message(s) due to buffer overflow (total {total})\n");
            return TryWrite(notice, 0, notice.Length);
        }

        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;

            running = false;
            Signal();
            thread.Join(JoinTimeoutMs);

            stream.Dispose();
            signal.Dispose();
        }

        private sealed class LogBuffer {
            public readonly byte[] Data;
            public int Length;
            public int Writers;

            public LogBuffer(int capacity) => Data = new byte[capacity];
        }
    }
}
