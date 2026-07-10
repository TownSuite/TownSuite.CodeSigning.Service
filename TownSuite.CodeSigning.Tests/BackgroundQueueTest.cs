using System;
using System.Threading;
using System.Threading.Tasks;
using TownSuite.CodeSigning.Service;

namespace TownSuite.CodeSigning.Tests
{
    [TestFixture]
    public class BackgroundQueueTest
    {
        private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 5000)
        {
            var waited = 0;
            while (!condition() && waited < timeoutMs)
            {
                await Task.Delay(25);
                waited += 25;
            }
        }

        [Test]
        public async Task ThrowingJob_DoesNotKillWorker_AndLaterJobsStillRun()
        {
            var queue = new BackgroundQueue();
            var ranAfterThrow = new ManualResetEventSlim(false);

            // A job that throws must be swallowed so the worker survives.
            queue.QueueThread(() => throw new InvalidOperationException("boom"));
            // A job queued after the throwing one must still be executed.
            queue.QueueThread(() => ranAfterThrow.Set());

            var ran = ranAfterThrow.Wait(5000);

            Assert.Multiple(() =>
            {
                Assert.That(ran, Is.True, "job queued after a throwing job did not run - worker likely died");
                Assert.That(queue.FailedCount, Is.EqualTo(1));
                Assert.That(queue.CompletedCount, Is.GreaterThanOrEqualTo(1));
            });
        }

        [Test]
        public async Task Counters_ReflectCompletedWork()
        {
            var queue = new BackgroundQueue();
            for (int i = 0; i < 3; i++)
            {
                queue.QueueThread(() => { });
            }

            await WaitUntil(() => queue.CompletedCount >= 3);

            Assert.Multiple(() =>
            {
                Assert.That(queue.CompletedCount, Is.EqualTo(3));
                Assert.That(queue.QueueDepth, Is.EqualTo(0));
                Assert.That(queue.LastActivityUtc, Is.Not.Null);
            });
        }
    }
}
