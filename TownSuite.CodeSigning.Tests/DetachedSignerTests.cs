using NUnit.Framework;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TownSuite.CodeSigning.Service;

namespace TownSuite.CodeSigning.Tests
{
    [TestFixture]
    public class DetachedSignerTests
    {
        [Test]
        public async Task SignDetached_WithPfxFallback_CreatesValidDetachedSignature()
        {
            // Arrange - settings and test cert are prepared by OneTimeUnitTestSetup
            var settings = OneTimeUnitTestSetup.SignToolSettings;
            Assert.IsNotNull(settings, "SignToolSettings must be initialized by test setup");

            var logger = Substitute.For<ILogger>();

            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            var inputPath = Path.Combine(tempDir, "detached_test_input.bin");
            var sigPath = inputPath + ".sig";

            try
            {
                File.WriteAllBytes(inputPath, new byte[] { 1, 2, 3, 4, 5, 6 });

                var signer = new DetachedSigner(settings, logger);
                var result = await signer.SignDetachedAsync(inputPath, sigPath);

                Assert.IsTrue(result.IsSigned, result.Message);
                Assert.IsTrue(File.Exists(sigPath), "Signature file was not created");

                // Verify the detached signature using SignedCms
                var content = File.ReadAllBytes(inputPath);
                var sigBytes = File.ReadAllBytes(sigPath);

                var contentInfo = new System.Security.Cryptography.Pkcs.ContentInfo(content);
                var signedCms = new System.Security.Cryptography.Pkcs.SignedCms(contentInfo, detached: true);
                signedCms.Decode(sigBytes);

                // Should not throw when checking signature (throws on failure)
                Assert.DoesNotThrow(() => signedCms.CheckSignature(true));
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }
}
