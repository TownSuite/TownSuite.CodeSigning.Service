using Microsoft.Extensions.Logging;
using TownSuite.CodeSigning.Service;

namespace TownSuite.CodeSigning.Tests
{
    [TestFixture]
    public class StatusServiceTest
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

        private static Settings WithCertPaths(string? certificatePath = null, string? certificatePassword = null,
            string? openSslSignerCertPath = null, int certificateWarningDays = 0)
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
                HealthCheckQueueStallInMs = good.HealthCheckQueueStallInMs,
                CertificatePath = certificatePath,
                CertificatePassword = certificatePassword,
                CertificateWarningDays = certificateWarningDays,
                OpenSSL = new OpenSSLSettings
                {
                    OpenSslPath = good.OpenSSL.OpenSslPath,
                    OpenSslOptions = good.OpenSSL.OpenSslOptions,
                    OpenSslTimeoutInMs = good.OpenSSL.OpenSslTimeoutInMs,
                    OsslSignCodePath = good.OpenSSL.OsslSignCodePath,
                    TimestampOptions = good.OpenSSL.TimestampOptions,
                    SignerCertPath = openSslSignerCertPath,
                }
            };
        }

        [Test]
        public async Task Queue_CountersAreReflected_AndHealthIsNullWithoutHealthService()
        {
            var queue = new BackgroundQueue();
            queue.QueueThread(() => { });
            queue.QueueThread(() => { });
            queue.QueueThread(() => { });
            queue.QueueThread(() => throw new InvalidOperationException("boom"));

            await WaitUntil(() => queue.CompletedCount >= 3 && queue.FailedCount >= 1);

            var settings = WithCertPaths();
            var status = new StatusService(settings, NSubstitute.Substitute.For<ILogger>(), queue);

            var result = await status.GetStatusAsync(null, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.Queue.Completed, Is.EqualTo(3));
                Assert.That(result.Queue.Failed, Is.EqualTo(1));
                Assert.That(result.Queue.Depth, Is.EqualTo(0));
                Assert.That(result.Queue.LastActivityUtc, Is.Not.Null);
                Assert.That(result.Health, Is.Null);
                Assert.That(result.Certificates, Is.Empty);
            });
        }

        [Test]
        public async Task Certificate_ParsedFromPfx()
        {
            var settings = WithCertPaths(certificatePath: OneTimeUnitTestSetup.certPath,
                certificatePassword: OneTimeUnitTestSetup.password);
            var status = new StatusService(settings, NSubstitute.Substitute.For<ILogger>(), new BackgroundQueue());

            var result = await status.GetStatusAsync(null, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.Certificates, Has.Count.EqualTo(1));
                var cert = result.Certificates[0];
                Assert.That(cert.Role, Is.EqualTo("signtool"));
                Assert.That(cert.Subject, Is.EqualTo("CN=TestCertificate"));
                Assert.That(cert.Thumbprint, Is.Not.Null.And.Not.Empty);
                Assert.That(cert.DaysUntilExpiry, Is.GreaterThan(0));
                Assert.That(cert.State, Is.EqualTo("ok"));
                Assert.That(cert.Error, Is.Null);
            });
        }

        [Test]
        public async Task Certificate_ParsedFromPem_ForOpenSslSignerCert()
        {
            var settings = WithCertPaths(openSslSignerCertPath: "testcert.crt");
            var status = new StatusService(settings, NSubstitute.Substitute.For<ILogger>(), new BackgroundQueue());

            var result = await status.GetStatusAsync(null, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.Certificates, Has.Count.EqualTo(1));
                var cert = result.Certificates[0];
                Assert.That(cert.Role, Is.EqualTo("openssl-detached"));
                Assert.That(cert.Subject, Is.EqualTo("CN=TestCertificate"));
                Assert.That(cert.State, Is.EqualTo("ok"));
                Assert.That(cert.Error, Is.Null);
            });
        }

        [Test]
        public async Task Certificate_Unset_ProducesEmptyList()
        {
            var settings = WithCertPaths();
            var status = new StatusService(settings, NSubstitute.Substitute.For<ILogger>(), new BackgroundQueue());

            var result = await status.GetStatusAsync(null, CancellationToken.None);

            Assert.That(result.Certificates, Is.Empty);
        }

        [Test]
        public async Task Certificate_MissingFile_ReportsErrorState_AndDoesNotThrow()
        {
            var settings = WithCertPaths(certificatePath: "does-not-exist.pfx", certificatePassword: "password");
            var status = new StatusService(settings, NSubstitute.Substitute.For<ILogger>(), new BackgroundQueue());

            var result = await status.GetStatusAsync(null, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.Certificates, Has.Count.EqualTo(1));
                Assert.That(result.Certificates[0].State, Is.EqualTo("error"));
                Assert.That(result.Certificates[0].Error, Is.Not.Null);
            });
        }

        [Test]
        public async Task Certificate_WrongPassword_ReportsErrorState()
        {
            var settings = WithCertPaths(certificatePath: OneTimeUnitTestSetup.certPath, certificatePassword: "wrong-password");
            var status = new StatusService(settings, NSubstitute.Substitute.For<ILogger>(), new BackgroundQueue());

            var result = await status.GetStatusAsync(null, CancellationToken.None);

            Assert.That(result.Certificates[0].State, Is.EqualTo("error"));
        }

        [TestCase(-10, 30, "expired")]
        [TestCase(10, 30, "warning")]
        [TestCase(100, 30, "ok")]
        [TestCase(15, 10, "ok")]
        public void ComputeState_ReturnsExpectedState(int daysFromNow, int warningDays, string expectedState)
        {
            var now = DateTimeOffset.UtcNow;
            var notAfter = now.AddDays(daysFromNow);

            var state = StatusService.ComputeState(notAfter, warningDays, now);

            Assert.That(state, Is.EqualTo(expectedState));
        }

        [Test]
        public async Task Concurrency_ReflectsInitializedSemaphore()
        {
            // Queuing.SetSemaphore is called once by OneTimeUnitTestSetup for the whole assembly.
            var settings = WithCertPaths();
            var status = new StatusService(settings, NSubstitute.Substitute.For<ILogger>(), new BackgroundQueue());

            var result = await status.GetStatusAsync(null, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.Concurrency.MaxSlots, Is.Not.Null);
                Assert.That(result.Concurrency.AvailableSlots, Is.Not.Null);
                Assert.That(result.Concurrency.InUse, Is.Not.Null);
            });
        }
    }
}
