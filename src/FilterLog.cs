using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace KeyboardRepeatFilter
{
    // The app's single log writer. Every log line in the process goes through here.
    //
    // The reason this class exists is the low-level hook threads. While a hook
    // callback has not returned, win32k's raw input thread is blocked waiting on it
    // (up to LowLevelHooksTimeout, 300ms by default), and that thread is what
    // services the rest of the session's input work, including HID device attach.
    // A synchronous File.AppendAllText on a hook thread is an open + write + close
    // that antivirus inspects every time, so a busy disk (a USB device install is
    // exactly that) can stall the hook long enough to be felt well outside this app.
    //
    // Write() therefore does no I/O at all: it stamps the time, enqueues, and
    // returns. The background thread below does every file access, batching a whole
    // drain into one append instead of one append per event.
    //
    // The file is deliberately not held open between batches. A persistent write
    // handle would lock out KeyboardHeatmap.exe, which reads the same log with
    // ordinary share-read semantics while the app is running.
    internal static class FilterLog
    {
        // Deep enough to swallow a long stall without growing without bound. Past
        // this, entries are dropped and counted rather than queued forever; the
        // count is reported in the log so a gap is never silent.
        private const int QueueCapacity = 4096;
        private const int MaxBatchLines = 512;

        // KeyboardHeatmap's LogParser matches on this exact shape. Do not change it
        // without updating the regexes in KeyboardHeatmap/LogParser.cs.
        private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";

        private struct Entry
        {
            public DateTime When;
            public string Message;
        }

        private static readonly object _gate = new object();
        private static readonly ManualResetEventSlim _idle = new ManualResetEventSlim(true);
        private static BlockingCollection<Entry> _queue;
        private static Thread _writer;
        private static volatile string _path;
        private static int _dropped;

        // Points the log at a file and starts the writer thread if it is not already
        // running. Called at startup and again whenever a profile switch swaps in a
        // config with a different LogFilePath; entries still queued at that moment
        // land in the new file, which is a fair reading of "the log moved".
        public static void Configure(string logFilePath)
        {
            lock (_gate)
            {
                _path = string.IsNullOrWhiteSpace(logFilePath) ? null : logFilePath;

                if (_path == null)
                {
                    return;
                }

                try
                {
                    var directory = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                }
                catch
                {
                    // Flush() retries the directory once per failed batch; a bad path
                    // must never take the app down at startup.
                }

                if (_queue != null)
                {
                    return;
                }

                _queue = new BlockingCollection<Entry>(QueueCapacity);
                _writer = new Thread(WriterLoop)
                {
                    IsBackground = true,
                    Name = "KeyboardRepeatFilter.Log"
                };

                // The queue is passed in rather than read from the field so a later
                // Shutdown/Configure pair can never point this thread at a new queue.
                _writer.Start(_queue);
            }
        }

        // Safe to call from a hook callback: no I/O, no allocation beyond the entry
        // itself, and no lock held longer than a queue insert. `message` is the line
        // without its timestamp, which is stamped here so ordering reflects when the
        // event happened rather than when the disk got around to it.
        public static void Write(string message)
        {
            var queue = _queue;
            if (queue == null || _path == null || message == null)
            {
                return;
            }

            var entry = new Entry { When = DateTime.Now, Message = message };

            try
            {
                if (queue.TryAdd(entry))
                {
                    _idle.Reset();
                }
                else
                {
                    Interlocked.Increment(ref _dropped);
                }
            }
            catch (InvalidOperationException)
            {
                // CompleteAdding ran while we were enqueuing; the app is shutting
                // down. Covers ObjectDisposedException too, which derives from this.
            }
        }

        // Best-effort wait for the queue to reach disk. Used on the shutdown paths so
        // a final lifecycle line is not lost when the process is about to die, without
        // permanently stopping the writer the way Shutdown does.
        public static void WaitForDrain(int timeoutMs)
        {
            if (_queue == null)
            {
                return;
            }

            try
            {
                _idle.Wait(timeoutMs);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        // Stops the writer for good after letting it finish what is queued. Anything
        // logged after this is silently dropped, so it belongs at the tail of Main and
        // nowhere else.
        public static void Shutdown()
        {
            BlockingCollection<Entry> queue;
            Thread writer;

            lock (_gate)
            {
                queue = _queue;
                writer = _writer;
                _queue = null;
                _writer = null;
            }

            if (queue == null)
            {
                return;
            }

            try
            {
                queue.CompleteAdding();
            }
            catch
            {
                // Already completed; nothing to do.
            }

            try
            {
                writer?.Join(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // A writer that will not finish in two seconds is not worth blocking
                // exit for; it is a background thread and dies with the process.
            }
        }

        private static void WriterLoop(object state)
        {
            var queue = (BlockingCollection<Entry>)state;
            var batch = new List<Entry>(MaxBatchLines);
            var builder = new StringBuilder(16 * 1024);

            while (true)
            {
                Entry first;

                try
                {
                    // Returns false only once the collection is completed and empty,
                    // which is the shutdown signal.
                    if (!queue.TryTake(out first, Timeout.Infinite))
                    {
                        break;
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                batch.Add(first);

                Entry next;
                while (batch.Count < MaxBatchLines && queue.TryTake(out next))
                {
                    batch.Add(next);
                }

                Flush(batch, builder);
                batch.Clear();

                if (queue.Count == 0)
                {
                    _idle.Set();
                }
            }

            _idle.Set();
        }

        private static void Flush(List<Entry> batch, StringBuilder builder)
        {
            var path = _path;
            if (path == null || batch.Count == 0)
            {
                return;
            }

            builder.Clear();

            var dropped = Interlocked.Exchange(ref _dropped, 0);
            if (dropped > 0)
            {
                builder.Append(batch[0].When.ToString(TimestampFormat))
                       .Append(" - LogOverflow: ")
                       .Append(dropped)
                       .Append(" line(s) dropped")
                       .Append(Environment.NewLine);
            }

            foreach (var entry in batch)
            {
                builder.Append(entry.When.ToString(TimestampFormat))
                       .Append(" - ")
                       .Append(entry.Message)
                       .Append(Environment.NewLine);
            }

            var text = builder.ToString();

            try
            {
                File.AppendAllText(path, text);
            }
            catch
            {
                // The folder may have been deleted, or another process may hold the
                // file for a moment. Recreate the folder and try once more; if that
                // fails too the batch is dropped rather than retried forever, because
                // this thread must never become the reason the queue backs up.
                try
                {
                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.AppendAllText(path, text);
                }
                catch
                {
                }
            }
        }
    }
}
