using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TownSuite.CodeSigning.Service;

namespace TownSuite.CodeSigning.Tests
{
    [TestFixture]
    public class BatachedSigningTest
    {
        [Test]
        public async Task Test1()
        {
            // Arrange
            var srcAssemblyPath = System.IO.Path.Combine(AppContext.BaseDirectory, "test.dll");
            var assemblyPath = System.IO.Path.Combine(AppContext.BaseDirectory, "test_batched.dll");
            var settings = OneTimeUnitTestSetup.SignToolSettings;

            // Act upload

            using var fs = new FileStream(srcAssemblyPath, FileMode.Open, FileAccess.Read);
            var ur = await BatchedSigning.Sign(fs, settings, NSubstitute.Substitute.For<ILogger>());
            var uploadResult = ur as Microsoft.AspNetCore.Http.HttpResults.Ok<string>;
            string id = uploadResult?.Value?.Replace("\"", "");

            // Assert

            Assert.Multiple(() =>
            {
                Assert.That(uploadResult, Is.Not.Null);
                Assert.That(uploadResult.StatusCode, Is.EqualTo(200));
            });

            // Act download

            bool doLoop = true;
            int count = 0;
            while (doLoop && count <20)
            {
                var dr = await BatchedSigning.Get(id, NSubstitute.Substitute.For<ILogger>());

                if (dr is Microsoft.AspNetCore.Http.HttpResults.FileStreamHttpResult streamResult)
                {
                    doLoop = false;
                    await using var resultStream = streamResult.FileStream;
                    await using var file = File.OpenWrite(assemblyPath);
                    resultStream.CopyTo(file);
                }
                else if (dr is Microsoft.AspNetCore.Http.HttpResults.FileContentHttpResult file)
                {
                    doLoop = false;
                    await System.IO.File.WriteAllBytesAsync(assemblyPath, file.FileContents.ToArray());
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
            Assert.IsTrue(Certs.ValidateDigitalSignature(assemblyPath, OneTimeUnitTestSetup.certPath, OneTimeUnitTestSetup.password));

        }
    }
}
