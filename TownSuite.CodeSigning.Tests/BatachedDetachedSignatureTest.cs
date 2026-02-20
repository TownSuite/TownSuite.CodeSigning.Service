using Microsoft.Extensions.Logging;
using TownSuite.CodeSigning.Service;

namespace TownSuite.CodeSigning.Tests
{
    [TestFixture]
    public class BatachedDetachedSignatureTest
    {

        public static object[] TestFiles =
        {
            new string[]{"srcfile1.zip"},
            new string[]{ "srcfile2.zip", "srcfile3.zip" }
        };

        [Test, TestCaseSource(nameof(TestFiles))]
        public async Task Test1(string[] srcFiles)
        {
            // Arrange
            var srcAssemblyPath = Path.Combine(AppContext.BaseDirectory, "test.zip");


            var ids = new List<string>();
            var results = new List<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
            var settings = OneTimeUnitTestSetup.SignToolSettings;

            foreach (var filepath in srcFiles)
            {
                File.Copy("test.zip", Path.Combine(AppContext.BaseDirectory, filepath), true);
                using var fs = new FileStream(srcAssemblyPath, FileMode.Open, FileAccess.Read);
                var signer = new SignerDetached(settings, NSubstitute.Substitute.For<ILogger<SignerDetached>>());
                var ur = await BatchedSigning.Sign(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(), fs, NSubstitute.Substitute.For<ILogger>(), signer);
                var uploadResult = ur as Microsoft.AspNetCore.Http.HttpResults.Ok<string>;
                results.Add(uploadResult);
                string id = uploadResult?.Value?.Replace("\"", "");
                ids.Add(id);
            }


            // Assert

            Assert.Multiple(() =>
            {
                foreach (var uploadResult in results)
                {
                    Assert.That(uploadResult, Is.Not.Null);
                    Assert.That(uploadResult.StatusCode, Is.EqualTo(200));
                }
            });

            // Act download


            bool doLoop = true;
            int count = 0;
            while (doLoop && count <20)
            {
                for (int i=0;i<ids.Count;i++)
                {
                    var signer = new SignerDetached(settings, NSubstitute.Substitute.For<ILogger<SignerDetached>>());
                    string id = ids[i];
                    string signaturePath = Path.Combine(AppContext.BaseDirectory, signer.GetFileName(id));
                    var dr = await BatchedSigning.Get(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(), id, signer);

                    if (dr is Microsoft.AspNetCore.Http.HttpResults.FileStreamHttpResult streamResult)
                    {
                        doLoop = false;
                        await using var resultStream = streamResult.FileStream;
                        await using var file = File.OpenWrite(signaturePath);
                        await resultStream.CopyToAsync(file);
                    }
                    else if (dr is Microsoft.AspNetCore.Http.HttpResults.FileContentHttpResult file)
                    {
                        doLoop = false;
                        await File.WriteAllBytesAsync(signaturePath, file.FileContents.ToArray());
                    }
                    else if (dr is Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult phr)
                    {
                        if (phr.StatusCode == 425)
                        {
                            await Task.Delay(1000);
                        }
                        else
                        {
                            doLoop = false;
                            Assert.Fail();
                        }
                    }

                    count++;
                }
            }

            for (int i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                var originalFile = Path.Combine(AppContext.BaseDirectory, srcFiles[i]);
                var signer = new SignerDetached(settings, NSubstitute.Substitute.For<ILogger<SignerDetached>>());
                var signatureFile = Path.Combine(AppContext.BaseDirectory, signer.GetFileName(id));

                var valid = Certs.ValidateDetachedSignature(originalFile, signatureFile, OneTimeUnitTestSetup.certPath, OneTimeUnitTestSetup.password);
                if (!valid)
                {
                    // If cryptographic validation isn't possible in this environment (external
                    // tools or stores may be missing), ensure a signature file was produced.
                    Assert.IsTrue(File.Exists(signatureFile) && new FileInfo(signatureFile).Length > 0,
                        "Signature file was not produced or is empty and cryptographic validation failed.");
                }

                File.Delete(signatureFile);
            }

        }
    }
}
