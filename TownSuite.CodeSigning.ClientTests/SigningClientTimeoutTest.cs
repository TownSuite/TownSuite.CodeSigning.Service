using NUnit.Framework;
using System.Net;
using TownSuite.CodeSigning.Client;

namespace TownSuite.CodeSigning.ClientTests
{
    [TestFixture]
    public class SigningClientTimeoutTest
    {
        private string _tempDir = string.Empty;

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
            catch { }
        }

        private sealed class NeverReadyHandler : HttpMessageHandler
        {
            public int PollCount;
            public readonly List<string?> UploadExtensionHeaders = new();
            public readonly List<string?> PollExtensionHeaders = new();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string path = request.RequestUri!.AbsolutePath;

                if (request.Method == HttpMethod.Get && path == "/healthz")
                {
                    return Respond(HttpStatusCode.OK, "Healthy");
                }
                if (request.Method == HttpMethod.Post && path == "/sign/batch")
                {
                    UploadExtensionHeaders.Add(ReadExtension(request));
                    return Respond(HttpStatusCode.OK, $"\"{Guid.NewGuid()}\"");
                }
                if (request.Method == HttpMethod.Get && path == "/sign/batch")
                {
                    PollExtensionHeaders.Add(ReadExtension(request));
                    Interlocked.Increment(ref PollCount);
                    return Respond((HttpStatusCode)425, "The file has not been signed yet");
                }

                return Respond(HttpStatusCode.NotFound, string.Empty);
            }

            private static string? ReadExtension(HttpRequestMessage request) =>
                request.Headers.TryGetValues("X-FileExtension", out var values) ? values.FirstOrDefault() : null;

            private static Task<HttpResponseMessage> Respond(HttpStatusCode code, string body) =>
                Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
        }

        private static (SigningClient Client, NeverReadyHandler Handler) Build()
        {
            var handler = new NeverReadyHandler();
            var httpClient = new HttpClient(handler);
            return (new SigningClient(httpClient, "http://localhost:5000/sign"), handler);
        }

        [Test]
        public async Task ServiceNeverReturnsSignedFile_TimeoutIsReportedAsFailure()
        {
            string filePath = Path.Combine(_tempDir, "installer.msix");
            File.WriteAllBytes(filePath, new byte[] { 1, 2, 3 });

            var (client, handler) = Build();

            var uploadFailures = await client.UploadFiles(quickFail: false, ignoreFailures: false, new[] { filePath }, detached: false);
            Assert.That(uploadFailures, Is.Empty, "upload should succeed against the fake service");

            var failures = await client.DownloadSignedFiles(quickFail: false, ignoreFailures: false, batchTimeoutInSeconds: 2);

            Assert.Multiple(() =>
            {
                Assert.That(handler.PollCount, Is.GreaterThan(0), "the client should have polled at least once");
                Assert.That(failures, Has.Length.EqualTo(1));
                Assert.That(failures[0].FailedFile, Is.EqualTo(filePath));
                Assert.That(failures[0].Message, Does.Contain("Timed out"));
            });
        }

        [Test]
        public async Task OriginalExtensionIsSentOnUpload()
        {
            string filePath = Path.Combine(_tempDir, "installer.msix");
            File.WriteAllBytes(filePath, new byte[] { 1, 2, 3 });

            var (client, handler) = Build();

            await client.UploadFiles(quickFail: false, ignoreFailures: false, new[] { filePath }, detached: false);
            await client.DownloadSignedFiles(quickFail: false, ignoreFailures: false, batchTimeoutInSeconds: 2);

            Assert.Multiple(() =>
            {
                Assert.That(handler.UploadExtensionHeaders, Is.Not.Empty);
                Assert.That(handler.UploadExtensionHeaders, Has.All.EqualTo(".msix"));
                Assert.That(handler.PollExtensionHeaders, Has.All.Null,
                    "the poll no longer needs the extension; the service locates the result by id");
            });
        }
    }
}
