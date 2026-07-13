using NUnit.Framework;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using TownSuite.CodeSigning.Client;

namespace TownSuite.CodeSigning.ClientTests
{
    /// <summary>
    /// Exercises the full SigningClient upload/download pipeline against a fake signing
    /// service, proving that post-download verification gates success: a download only
    /// lands in the good list when the returned content actually carries a signature
    /// (embedded Authenticode for normal mode, a structurally valid side-by-side CMS
    /// .sig for detached mode).
    /// </summary>
    [TestFixture]
    public class SigningClientVerificationTest
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
            }
            catch { /* ignore cleanup errors */ }
        }

        private sealed class FakeSigningServiceHandler : HttpMessageHandler
        {
            public byte[] DownloadBytes { get; set; } = Array.Empty<byte>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string path = request.RequestUri!.AbsolutePath;

                if (request.Method == HttpMethod.Get && path == "/healthz")
                {
                    return Text("Healthy");
                }
                if (request.Method == HttpMethod.Post && path == "/sign/batch")
                {
                    return Text($"\"{Guid.NewGuid()}\"");
                }
                if (request.Method == HttpMethod.Get && path == "/sign/batch")
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(DownloadBytes)
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            private static Task<HttpResponseMessage> Text(string body) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }

        private static byte[] SignDetached(byte[] content)
        {
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest("CN=SigningClientTestCert", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

            var contentInfo = new ContentInfo(content);
            var signedCms = new SignedCms(contentInfo, detached: true);
            signedCms.ComputeSignature(new CmsSigner(cert));
            return signedCms.Encode();
        }

        private static async Task<(string FailedFile, string Message)[]> RunPipeline(
            byte[] serviceReturns, string filePath, bool detached)
        {
            var handler = new FakeSigningServiceHandler { DownloadBytes = serviceReturns };
            using var httpClient = new HttpClient(handler);
            var client = new SigningClient(httpClient, "http://localhost:5000/sign");

            // quickFail must stay false so verification failures surface as returned
            // failures instead of Environment.Exit.
            var uploadFailures = await client.UploadFiles(quickFail: false, ignoreFailures: false, new[] { filePath }, detached);
            Assert.That(uploadFailures, Is.Empty, "upload should succeed against the fake service");

            return await client.DownloadSignedFiles(quickFail: false, ignoreFailures: false, batchTimeoutInSeconds: 30);
        }

        [Test]
        public async Task Detached_ValidSignatureReturned_IsVerifiedAndSucceeds()
        {
            byte[] content = { 10, 20, 30, 40, 50 };
            string filePath = Path.Combine(_tempDir, "payload.bin");
            File.WriteAllBytes(filePath, content);

            var failures = await RunPipeline(SignDetached(content), filePath, detached: true);

            Assert.Multiple(() =>
            {
                Assert.That(failures, Is.Empty);
                Assert.That(File.Exists(filePath + ".sig"), Is.True, "side-by-side .sig file should be written");
            });
        }

        [Test]
        public async Task Detached_GarbageSignatureReturned_FailsVerification()
        {
            string filePath = Path.Combine(_tempDir, "payload.bin");
            File.WriteAllBytes(filePath, new byte[] { 1, 2, 3 });

            var failures = await RunPipeline(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, filePath, detached: true);

            Assert.Multiple(() =>
            {
                Assert.That(failures, Has.Length.EqualTo(1));
                Assert.That(failures[0].FailedFile, Is.EqualTo(filePath));
                Assert.That(failures[0].Message, Does.Contain("missing or invalid"));
            });
        }

        [Test]
        public async Task Detached_EmptySignatureReturned_FailsVerification()
        {
            string filePath = Path.Combine(_tempDir, "payload.bin");
            File.WriteAllBytes(filePath, new byte[] { 1, 2, 3 });

            var failures = await RunPipeline(Array.Empty<byte>(), filePath, detached: true);

            Assert.Multiple(() =>
            {
                Assert.That(failures, Has.Length.EqualTo(1));
                Assert.That(failures[0].Message, Does.Contain("missing or invalid"));
            });
        }

        [Test]
        public async Task Embedded_SignedFileReturned_IsVerifiedAndSucceeds()
        {
            byte[] signedBytes = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "test_already_signed.dll"));
            string filePath = Path.Combine(_tempDir, "app.dll");
            File.WriteAllBytes(filePath, signedBytes); // pre-download content gets overwritten by the response

            var failures = await RunPipeline(signedBytes, filePath, detached: false);

            Assert.That(failures, Is.Empty);
        }

        [Test]
        public async Task Embedded_UnsignedFileReturned_FailsVerification()
        {
            string filePath = Path.Combine(_tempDir, "app.dll");
            File.WriteAllBytes(filePath, new byte[] { 0x4D, 0x5A, 0x00, 0x01 });

            var failures = await RunPipeline(new byte[] { 0x4D, 0x5A, 0x00, 0x01 }, filePath, detached: false);

            Assert.Multiple(() =>
            {
                Assert.That(failures, Has.Length.EqualTo(1));
                Assert.That(failures[0].Message, Does.Contain("missing a digital signature"));
            });
        }
    }
}
