using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TownSuite.CodeSigning.Service;

namespace TownSuite.CodeSigning.Tests
{
    [TestFixture]
    public class DetachedBatchedSigningTests
    {
        private ILogger _logger;
        private DetachedSignerSettings _detachedSettings;

        [SetUp]
        public void Setup()
        {
            _logger = Substitute.For<ILogger>();
            _detachedSettings = OneTimeUnitTestSetup.DetachedSettings;
            Assert.IsNotNull(_detachedSettings, "DetachedSettings must be initialized by test setup");
        }

        [Test]
        public async Task Sign_SingleFile_CreatesDetachedSignature()
        {
            var testData = new byte[] { 1, 2, 3, 4, 5, 6 };
            using var bodyStream = new MemoryStream(testData);
            var headers = new Dictionary<string, StringValues>();

            var result = await DetachedBatchedSigning.Sign(headers, bodyStream, _detachedSettings, _logger);

            Assert.That(result, Is.InstanceOf<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>());
            var okResult = result as Microsoft.AspNetCore.Http.HttpResults.Ok<string>;
            Assert.That(okResult.StatusCode, Is.EqualTo(200));
            
            string id = okResult.Value;
            Assert.That(id, Is.Not.Null);
            Assert.That(id, Is.Not.Empty);
            Assert.That(Guid.TryParse(id, out _), Is.True);

            byte[] sigBytes = await WaitAndRetrieveSignature(id, headers);

            Assert.That(sigBytes.Length, Is.GreaterThan(0));
            VerifyDetachedSignature(testData, sigBytes);
        }

        [Test]
        public async Task Sign_WithBatchId_ProcessesMultipleFiles()
        {
            var batchId = Guid.NewGuid().ToString();
            var headers = new Dictionary<string, StringValues>
            {
                { "X-BatchId", batchId }
            };

            var fileIds = new List<string>();

            for (int i = 0; i < 3; i++)
            {
                var testData = new byte[] { (byte)i, 2, 3, 4, 5, 6 };
                using var bodyStream = new MemoryStream(testData);

                var result = await DetachedBatchedSigning.Sign(headers, bodyStream, _detachedSettings, _logger);
                Assert.That(result, Is.InstanceOf<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>());

                var okResult = result as Microsoft.AspNetCore.Http.HttpResults.Ok<string>;
                fileIds.Add(okResult.Value);
            }

            headers["X-BatchReady"] = "true";
            var finalTestData = new byte[] { 99, 2, 3, 4, 5, 6 };
            using var finalBodyStream = new MemoryStream(finalTestData);
            var finalResult = await DetachedBatchedSigning.Sign(headers, finalBodyStream, _detachedSettings, _logger);
            Assert.That(finalResult, Is.InstanceOf<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>());
            var finalOkResult = finalResult as Microsoft.AspNetCore.Http.HttpResults.Ok<string>;
            fileIds.Add(finalOkResult.Value);

            foreach (var id in fileIds)
            {
                byte[] sigBytes = await WaitAndRetrieveSignature(id, headers);
                Assert.That(sigBytes.Length, Is.GreaterThan(0), $"File {id} should have a signature");
            }
        }

        [Test]
        public async Task Sign_WithInvalidBatchId_ThrowsException()
        {
            var headers = new Dictionary<string, StringValues>
            {
                { "X-BatchId", "not-a-guid" }
            };

            var testData = new byte[] { 1, 2, 3 };
            using var bodyStream = new MemoryStream(testData);

            Assert.ThrowsAsync<InvalidDataException>(async () => 
                await DetachedBatchedSigning.Sign(headers, bodyStream, _detachedSettings, _logger));
        }

        [Test]
        public async Task Get_NonExistentId_Returns404()
        {
            var nonExistentId = Guid.NewGuid().ToString();
            var headers = new Dictionary<string, StringValues>();

            var result = await DetachedBatchedSigning.Get(headers, nonExistentId, _logger);

            Assert.That(result, Is.InstanceOf<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>());
            var problemResult = result as Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult;
            Assert.That(problemResult.StatusCode, Is.EqualTo(404));
            Assert.That(problemResult.ProblemDetails.Title, Is.EqualTo("Not Found"));
        }

        [Test]
        public async Task Get_BeforeSigning_Returns425()
        {
            var testData = new byte[] { 1, 2, 3, 4, 5, 6 };
            using var bodyStream = new MemoryStream(testData);
            var headers = new Dictionary<string, StringValues>();

            var result = await DetachedBatchedSigning.Sign(headers, bodyStream, _detachedSettings, _logger);
            var okResult = result as Microsoft.AspNetCore.Http.HttpResults.Ok<string>;
            string id = okResult.Value;

            var getResult = await DetachedBatchedSigning.Get(headers, id, _logger);

            Assert.That(getResult, Is.InstanceOf<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>());
            var problemResult = getResult as Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult;
            Assert.That(problemResult.StatusCode, Is.EqualTo(425));
        }

        [Test]
        public async Task Sign_EmptyStream_CompletesSuccessfully()
        {
            var testData = Array.Empty<byte>();
            using var bodyStream = new MemoryStream(testData);
            var headers = new Dictionary<string, StringValues>();

            var result = await DetachedBatchedSigning.Sign(headers, bodyStream, _detachedSettings, _logger);

            Assert.That(result, Is.InstanceOf<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>());
            var okResult = result as Microsoft.AspNetCore.Http.HttpResults.Ok<string>;
            Assert.That(okResult.Value, Is.Not.Null);
        }

        [Test]
        public async Task Sign_NonSeekableStream_HandlesCorrectly()
        {
            var testData = new byte[] { 1, 2, 3, 4, 5, 6 };
            using var bodyStream = new NonSeekableMemoryStream(testData);
            var headers = new Dictionary<string, StringValues>();

            var result = await DetachedBatchedSigning.Sign(headers, bodyStream, _detachedSettings, _logger);

            Assert.That(result, Is.InstanceOf<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>());
        }

        [Test]
        public async Task Get_WithBatchId_RetrievesCorrectFile()
        {
            var batchId = Guid.NewGuid().ToString();
            var headers = new Dictionary<string, StringValues>
            {
                { "X-BatchId", batchId }
            };

            var testData = new byte[] { 7, 8, 9 };
            using var bodyStream = new MemoryStream(testData);

            var signResult = await DetachedBatchedSigning.Sign(headers, bodyStream, _detachedSettings, _logger);
            var okResult = signResult as Microsoft.AspNetCore.Http.HttpResults.Ok<string>;
            string fileId = okResult.Value;

            headers["X-BatchReady"] = "true";
            var finalData = new byte[] { 10, 11, 12 };
            using var finalStream = new MemoryStream(finalData);
            await DetachedBatchedSigning.Sign(headers, finalStream, _detachedSettings, _logger);

            byte[] sigBytes = await WaitAndRetrieveSignature(fileId, headers);
            Assert.That(sigBytes.Length, Is.GreaterThan(0));
        }

        [Test]
        public async Task Sign_MultipleBatches_IsolatesCorrectly()
        {
            var batch1Id = Guid.NewGuid().ToString();
            var batch2Id = Guid.NewGuid().ToString();

            var headers1 = new Dictionary<string, StringValues> { { "X-BatchId", batch1Id } };
            var headers2 = new Dictionary<string, StringValues> { { "X-BatchId", batch2Id } };

            var testData1 = new byte[] { 1, 2, 3 };
            using var stream1 = new MemoryStream(testData1);
            var result1 = await DetachedBatchedSigning.Sign(headers1, stream1, _detachedSettings, _logger);
            var id1 = (result1 as Microsoft.AspNetCore.Http.HttpResults.Ok<string>).Value;

            var testData2 = new byte[] { 4, 5, 6 };
            using var stream2 = new MemoryStream(testData2);
            var result2 = await DetachedBatchedSigning.Sign(headers2, stream2, _detachedSettings, _logger);
            var id2 = (result2 as Microsoft.AspNetCore.Http.HttpResults.Ok<string>).Value;

            Assert.That(id1, Is.Not.EqualTo(id2));

            headers1["X-BatchReady"] = "true";
            var finalData1 = new byte[] { 7, 8, 9 };
            using var finalStream1 = new MemoryStream(finalData1);
            await DetachedBatchedSigning.Sign(headers1, finalStream1, _detachedSettings, _logger);

            headers2["X-BatchReady"] = "true";
            var finalData2 = new byte[] { 10, 11, 12 };
            using var finalStream2 = new MemoryStream(finalData2);
            await DetachedBatchedSigning.Sign(headers2, finalStream2, _detachedSettings, _logger);

            byte[] sig1 = await WaitAndRetrieveSignature(id1, headers1);
            byte[] sig2 = await WaitAndRetrieveSignature(id2, headers2);

            Assert.That(sig1.Length, Is.GreaterThan(0));
            Assert.That(sig2.Length, Is.GreaterThan(0));
        }

        [Test]
        public async Task Sign_ValidBatchIdAndBatchReady_ProcessesAllFiles()
        {
            var batchId = Guid.NewGuid().ToString();
            var headers = new Dictionary<string, StringValues>
            {
                { "X-BatchId", batchId }
            };

            var fileIds = new List<string>();

            for (int i = 0; i < 2; i++)
            {
                var testData = new byte[] { (byte)i, 2, 3 };
                using var bodyStream = new MemoryStream(testData);
                var result = await DetachedBatchedSigning.Sign(headers, bodyStream, _detachedSettings, _logger);
                fileIds.Add((result as Microsoft.AspNetCore.Http.HttpResults.Ok<string>).Value);
            }

            headers["X-BatchReady"] = "true";
            var finalData = new byte[] { 99, 100, 101 };
            using var finalStream = new MemoryStream(finalData);
            var finalResult = await DetachedBatchedSigning.Sign(headers, finalStream, _detachedSettings, _logger);
            fileIds.Add((finalResult as Microsoft.AspNetCore.Http.HttpResults.Ok<string>).Value);

            foreach (var fileId in fileIds)
            {
                byte[] sigBytes = await WaitAndRetrieveSignature(fileId, headers);
                Assert.That(sigBytes.Length, Is.GreaterThan(0));
            }
        }

        private async Task<byte[]> WaitAndRetrieveSignature(string id, Dictionary<string, StringValues> headers, int maxWaitSeconds = 30)
        {
            int count = 0;
            int maxAttempts = maxWaitSeconds;

            while (count < maxAttempts)
            {
                var result = await DetachedBatchedSigning.Get(headers, id, _logger);

                if (result is Microsoft.AspNetCore.Http.HttpResults.FileStreamHttpResult fileStream)
                {
                    await using var sigStream = fileStream.FileStream;
                    using var ms = new MemoryStream();
                    await sigStream.CopyToAsync(ms);
                    return ms.ToArray();
                }

                if (result is Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult problemResult)
                {
                    if (problemResult.StatusCode == 500)
                    {
                        var detail = problemResult.ProblemDetails?.Detail ?? "Unknown error";
                        Assert.Fail($"Signing failed for {id}: {detail}");
                    }

                    if (problemResult.StatusCode == 425)
                    {
                        await Task.Delay(1000);
                        count++;
                        continue;
                    }
                    
                    Assert.Fail($"Unexpected problem result for {id}: Status={problemResult.StatusCode}, Detail={problemResult.ProblemDetails?.Detail}");
                }

                Assert.Fail($"Unexpected result type for {id}: {result?.GetType().Name}");
            }

            Assert.Fail($"Signing did not complete within {maxWaitSeconds} seconds for {id}");
            return null;
        }

        private void VerifyDetachedSignature(byte[] originalData, byte[] signatureBytes)
        {
            var contentInfo = new System.Security.Cryptography.Pkcs.ContentInfo(originalData);
            var signedCms = new System.Security.Cryptography.Pkcs.SignedCms(contentInfo, detached: true);
            signedCms.Decode(signatureBytes);

            Assert.DoesNotThrow(() => signedCms.CheckSignature(true));
        }

        private class NonSeekableMemoryStream : MemoryStream
        {
            public NonSeekableMemoryStream(byte[] buffer) : base(buffer)
            {
            }

            public override bool CanSeek => false;

            public override long Position
            {
                get => base.Position;
                set => throw new NotSupportedException();
            }
        }
    }
}
