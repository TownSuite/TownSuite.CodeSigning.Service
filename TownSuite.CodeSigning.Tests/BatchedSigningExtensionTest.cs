using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using TownSuite.CodeSigning.Service;

namespace TownSuite.CodeSigning.Tests
{
    [TestFixture]
    public class BatchedSigningExtensionTest
    {
        private readonly List<string> _foldersToClean = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var folder in _foldersToClean)
            {
                try
                {
                    if (Directory.Exists(folder)) Directory.Delete(folder, true);
                }
                catch { }
            }
            _foldersToClean.Clear();
        }

        private sealed class RecordingSigner : ISigner
        {
            public readonly List<string> ReceivedFiles = new();
            public bool IsSigned = true;
            public string Message = string.Empty;

            public Task<(bool IsSigned, string Message)> SignAsync(string workingDir, string[] files)
            {
                lock (ReceivedFiles) ReceivedFiles.AddRange(files);
                return Task.FromResult((IsSigned, Message));
            }

            public string GetFileName(string id, string extension) => $"{id}.workingfile{extension}";
        }

        private static Dictionary<string, StringValues> Headers(string batchId, string? extension, bool ready)
        {
            var headers = new Dictionary<string, StringValues> { ["X-BatchId"] = batchId };
            if (ready) headers["X-BatchReady"] = "true";
            if (extension is not null) headers["X-FileExtension"] = extension;
            return headers;
        }

        private async Task<(string Id, string BatchId)> Upload(RecordingSigner signer, string? extension)
        {
            string batchId = Guid.NewGuid().ToString();
            _foldersToClean.Add(Path.Combine(BatchedSigning.GetTempFolder(), batchId));

            using var body = new MemoryStream(new byte[] { 1, 2, 3, 4 });
            var result = await BatchedSigning.Sign(Headers(batchId, extension, ready: true), body,
                NSubstitute.Substitute.For<ILogger>(), signer);

            var ok = result as Microsoft.AspNetCore.Http.HttpResults.Ok<string>;
            Assert.That(ok, Is.Not.Null);
            return (ok!.Value!.Replace("\"", ""), batchId);
        }

        private static async Task WaitForSigner(RecordingSigner signer)
        {
            for (int i = 0; i < 100; i++)
            {
                lock (signer.ReceivedFiles)
                {
                    if (signer.ReceivedFiles.Count > 0) return;
                }
                await Task.Delay(100);
            }
            Assert.Fail("the background queue never invoked the signer");
        }

        [Test]
        public async Task UploadedExtensionReachesTheSigner()
        {
            var signer = new RecordingSigner();
            await Upload(signer, ".msix");
            await WaitForSigner(signer);

            Assert.That(signer.ReceivedFiles.Single(), Does.EndWith(".workingfile.msix"));
        }

        [Test]
        public async Task MissingExtensionKeepsTheLegacyWorkingFileName()
        {
            var signer = new RecordingSigner();
            await Upload(signer, null);
            await WaitForSigner(signer);

            Assert.That(signer.ReceivedFiles.Single(), Does.EndWith(".workingfile"));
        }

        [Test]
        public async Task BatchSigningFailureIsReturnedToThePollerInsteadOf425()
        {
            var signer = new RecordingSigner { IsSigned = false, Message = "signtool refused the package" };
            var (id, batchId) = await Upload(signer, ".msix");
            await WaitForSigner(signer);

            Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult? problem = null;
            for (int i = 0; i < 100; i++)
            {
                var result = await BatchedSigning.Get(Headers(batchId, ".msix", ready: false), id, signer);
                problem = result as Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult;
                if (problem?.StatusCode != 425) break;
                await Task.Delay(100);
            }

            Assert.Multiple(() =>
            {
                Assert.That(problem, Is.Not.Null);
                Assert.That(problem!.StatusCode, Is.EqualTo(500));
                Assert.That(problem.ProblemDetails.Detail, Does.Contain("signtool refused the package"));
            });
        }

        [TestCase(".msix", ".msix")]
        [TestCase(".MSIX", ".msix")]
        [TestCase("msix", ".msix")]
        [TestCase(".exe", ".exe")]
        [TestCase("", "")]
        [TestCase("   ", "")]
        [TestCase(".", "")]
        [TestCase("..", "")]
        [TestCase(".msix.exe", "")]
        [TestCase("../../etc/passwd", "")]
        [TestCase(".msix /f evil.pfx", "")]
        [TestCase(".msix\"", "")]
        [TestCase(".waytoolongextensionvalue", "")]
        public void OnlySafeExtensionsAreAccepted(string header, string expected)
        {
            Assert.That(BatchedSigning.GetSanitizedExtension(header), Is.EqualTo(expected));
        }
    }
}
