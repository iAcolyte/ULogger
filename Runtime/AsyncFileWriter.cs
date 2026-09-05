using System;
using System.IO;
using System.Threading;

using Unity.Android.Gradle.Manifest;

using UnityEngine;

namespace ULogger {
    public sealed class AsyncFileWriter: IDisposable {
        private readonly LogBuffer[] buffers;
        private volatile int activeIndex;
        private volatile bool running;
        private int dropped;

        private readonly ManualResetEventSlim signal = new(false, 200);
        private readonly Thread thread;
        private readonly FileStream stream;
        private readonly int capacity;
        public readonly int flushThreshold;

        public AsyncFileWriter(string path, int capacity = 1 << 20) {
            this.capacity = capacity;
            this.flushThreshold = capacity / 2;
            buffers = new[] {
                new LogBuffer{Data = new byte[capacity] },
                new LogBuffer{Data = new byte[capacity] }
            };
            var info = new FileInfo(path);
            info.Directory?.Create();
            var mode = info.Exists ? FileMode.Append : FileMode.Create;
            stream = new FileStream(path, mode,
                FileAccess.Write, FileShare.ReadWrite,
                1, FileOptions.None);
            thread = new Thread(WriterLoop) {
                IsBackground = true,
                Priority = System.Threading.ThreadPriority.BelowNormal,
                Name = "ULogger.FileWriter"
            };
            thread.Start();
        }

        public void Enqueue(ReadOnlySpan<byte> utf8, bool urgent) {
            int size = utf8.Length;
            if (size > capacity) { Interlocked.Increment(ref dropped); return; }

            while (true) {
                int idx = activeIndex;
                var buf = buffers[idx];

                Interlocked.Increment(ref buf.Writers);
                if (activeIndex != idx) {
                    Interlocked.Decrement(ref buf.Writers);
                    continue;
                }

                int end = Interlocked.Add(ref buf.Length, size);
                int start = end - size;

                if (end <= capacity) {
                    utf8.CopyTo(new Span<byte>(buf.Data, start, size));
                    Interlocked.Decrement(ref buf.Writers);

                    if (urgent || end >= flushThreshold) signal.Set();
                    return;
                }

                Interlocked.Decrement(ref buf.Writers);
                Interlocked.Increment(ref dropped);
                signal.Set();
                return;
            }
        }

        private void WriterLoop() {
            while (running) {
                signal.Wait(200);
                signal.Reset();
                SwapAndFlush(fsync: false);
            }
            SwapAndFlush(fsync: true);
            SwapAndFlush(fsync: true);
        }

        void SwapAndFlush(bool fsync) {
            int idx = activeIndex;
            var buf = buffers[idx];

            activeIndex = idx ^ 1;

            var spin = new SpinWait();
            while (Volatile.Read(ref buf.Writers) != 0) spin.SpinOnce();

            int len = Math.Min(buf.Length, capacity);
            if (len > 0) {
                try { stream.Write(buf.Data, 0, len); } catch { }
            }
            buf.Length = 0;

            if (len > 0) stream.Flush(fsync);
        }

        public void Dispose() {
            running = false;
            SwapAndFlush(true);
            signal.Set();
            thread.Join(2000);
            stream.Dispose();
            signal.Dispose();
        }

        sealed class LogBuffer {
            public byte[] Data;
            public int Length;
            public int Writers;
        }
    }
}
