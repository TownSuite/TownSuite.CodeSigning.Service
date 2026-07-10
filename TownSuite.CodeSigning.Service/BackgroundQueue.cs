using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TownSuite.CodeSigning.Service
{

    public class BackgroundQueue
    {

        private ConcurrentQueue<Action> _theQueueAction = new ConcurrentQueue<Action>();

        // Guards the start/stop decision so a job enqueued while the worker is exiting can
        // never be stranded (and so two workers can never start at once).
        private readonly object _threadLock = new object();
        private bool threadStarted = false;

        // Status counters (read by the readiness health check).
        private long _completedCount = 0;
        private long _failedCount = 0;
        private int _inFlight = 0;
        private long _lastCompletedTicks = 0;

        public void QueueThread(Action work)
        {
            _theQueueAction.Enqueue(work);

            lock (_threadLock)
            {
                if (!threadStarted)
                {
                    threadStarted = true;
                    // Baseline the "last completed" clock at worker start so the readiness
                    // stall check has something to measure against before the first job finishes.
                    Interlocked.Exchange(ref _lastCompletedTicks, DateTimeOffset.UtcNow.UtcTicks);
                    var t = new Thread(DoThreadness);
                    t.Start();
                }
            }
        }

        private void DoThreadness()
        {
            while (true)
            {
                // Drain everything currently queued. A throwing job must never kill the worker
                // thread, otherwise threadStarted stays true forever and signing wedges silently.
                while (_theQueueAction.TryDequeue(out Action work))
                {
                    try
                    {
                        Interlocked.Increment(ref _inFlight);
                        work();
                        Interlocked.Increment(ref _completedCount);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref _failedCount);
                        Console.Error.WriteLine($"BackgroundQueue job threw and was swallowed: {ex}");
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _inFlight);
                        Interlocked.Exchange(ref _lastCompletedTicks, DateTimeOffset.UtcNow.UtcTicks);
                    }
                }

                // Queue is empty; wait up to ten seconds for new work before letting the thread exit.
                const int oneSecond = 1000;
                const int tenSeconds = oneSecond * 10;
                var totalTimeEmpty = 0;
                while (_theQueueAction.IsEmpty)
                {
                    if (_isDisposed)
                    {
                        lock (_threadLock) { threadStarted = false; }
                        return;
                    }

                    if (totalTimeEmpty >= tenSeconds)
                    {
                        // Re-check emptiness under the lock so we don't exit in the window
                        // between a producer's Enqueue and its threadStarted check.
                        lock (_threadLock)
                        {
                            if (_theQueueAction.IsEmpty)
                            {
                                threadStarted = false;
                                return;
                            }
                        }
                        break; // work arrived during the exit decision; go drain it
                    }

                    Thread.Sleep(oneSecond);
                    totalTimeEmpty += oneSecond;
                }
            }
        }

        /// <summary>Number of jobs enqueued but not yet picked up by the worker.</summary>
        public int QueueDepth => _theQueueAction.Count;

        /// <summary>Jobs currently executing on the worker thread.</summary>
        public int InFlight => Interlocked.CompareExchange(ref _inFlight, 0, 0);

        /// <summary>Total jobs that ran to completion (including ones that reported a signing failure).</summary>
        public long CompletedCount => Interlocked.Read(ref _completedCount);

        /// <summary>Total jobs whose action threw an unhandled exception.</summary>
        public long FailedCount => Interlocked.Read(ref _failedCount);

        /// <summary>Whether the worker thread is currently alive.</summary>
        public bool IsWorkerRunning
        {
            get { lock (_threadLock) { return threadStarted; } }
        }

        /// <summary>
        /// When the worker last finished a job (or started, if it has not finished one yet).
        /// Null only before the worker has ever started.
        /// </summary>
        public DateTimeOffset? LastActivityUtc
        {
            get
            {
                var ticks = Interlocked.Read(ref _lastCompletedTicks);
                return ticks == 0 ? (DateTimeOffset?)null : new DateTimeOffset(ticks, TimeSpan.Zero);
            }
        }

        public static BackgroundQueue Instance
        {
            get
            {
                return InstanceDict("default_key");
            }
        }

        private static ConcurrentDictionary<string, BackgroundQueue> dictQueue = new ConcurrentDictionary<string, BackgroundQueue>();
        public static BackgroundQueue InstanceDict(string key)
        {

            if (!dictQueue.ContainsKey(key))
            {
                dictQueue.AddOrUpdate(key, new BackgroundQueue(), (k, o) => o);
            }

            return dictQueue[key];

        }

        public static void DisposeDict()
        {
            if (dictQueue != null)
                foreach (var queue in dictQueue.Values)
                    queue?.Dispose();
        }

        private bool _isDisposed = false;
        private void Dispose()
        {
            _isDisposed = true;
        }
    }


}
