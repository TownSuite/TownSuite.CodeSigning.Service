using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using System.Threading;
using TownSuite.CodeSigning.Service;

namespace TownSuite.CodeSigning.Tests
{
    [TestFixture]
    public class SigningHealthCheckTest
    {
        private static HealthCheckContext Context() => new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("signing",
                _ => throw new NotImplementedException(), HealthStatus.Unhealthy, new[] { "ready" })
        };

        [Test]
        public async Task Ready_IsHealthy_WhenSigningWorks()
        {
            var settings = OneTimeUnitTestSetup.SignToolSettings!;
            var check = new SigningHealthCheck(settings, NSubstitute.Substitute.For<ILogger>());

            var result = await check.CheckHealthAsync(Context());

            Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy), result.Description);
        }

        private static Settings WithStallWindow(int stallMs)
        {
            var good = OneTimeUnitTestSetup.SignToolSettings!;
            return new Settings
            {
                SignToolPath = good.SignToolPath,
                SignToolOptions = good.SignToolOptions,
                SigntoolTimeoutInMs = good.SigntoolTimeoutInMs,
                MaxRequestBodySize = good.MaxRequestBodySize,
                SemaphoreSlimProcessPerCpuLimit = good.SemaphoreSlimProcessPerCpuLimit,
                HealthCheckCacheInMs = good.HealthCheckCacheInMs,
                HealthCheckQueueStallInMs = stallMs,
                OpenSSL = good.OpenSSL
            };
        }

        [Test]
        public async Task Ready_IsUnhealthy_WhenSigningQueueIsNotDraining()
        {
            // A blocked worker with jobs still queued behind it simulates a wedged signing queue.
            var queue = new BackgroundQueue();
            using var gate = new ManualResetEventSlim(false);
            queue.QueueThread(() => gate.Wait());   // in-flight, blocks the worker
            queue.QueueThread(() => { });           // stays queued -> QueueDepth > 0
            queue.QueueThread(() => { });

            var settings = WithStallWindow(50);
            var check = new SigningHealthCheck(settings, NSubstitute.Substitute.For<ILogger>(), queue);

            // Wait past the stall window while the worker is blocked and jobs remain queued.
            await Task.Delay(250);

            var result = await check.CheckHealthAsync(Context());
            gate.Set(); // release the worker so the queue drains and its thread exits

            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
                Assert.That(result.Description, Does.Contain("not draining"));
            });
        }

        [Test]
        public async Task Ready_IsUnhealthy_WhenPrivateKeyIsMissing()
        {
            var good = OneTimeUnitTestSetup.SignToolSettings!;
            // Point signtool at a certificate file that does not exist so signing fails the
            // same way a missing private key would ("No private key is available.").
            var broken = new Settings
            {
                SignToolPath = good.SignToolPath,
                SignToolOptions = "sign /fd SHA256 /f \"{BaseDirectory}does-not-exist.pfx\" /p \"password\" /v \"{FilePath}\"",
                SigntoolTimeoutInMs = good.SigntoolTimeoutInMs,
                MaxRequestBodySize = good.MaxRequestBodySize,
                SemaphoreSlimProcessPerCpuLimit = good.SemaphoreSlimProcessPerCpuLimit,
                HealthCheckCacheInMs = good.HealthCheckCacheInMs,
                OpenSSL = good.OpenSSL
            };
            var check = new SigningHealthCheck(broken, NSubstitute.Substitute.For<ILogger>());

            var result = await check.CheckHealthAsync(Context());

            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
                Assert.That(result.Description, Does.Contain("Code signing is failing"));
            });
        }
    }
}
