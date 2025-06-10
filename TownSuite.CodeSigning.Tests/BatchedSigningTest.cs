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

        public static object[] TestFiles =
        {
            new string[]{"srcfile1.dll"},
            new string[]{"srcfile2.dll", "srcfile3.dll"}
        };

        [Test, TestCaseSource(nameof(TestFiles))]
        public async Task Test1(string[] srcFiles)
        {
            // Arrange
            var srcAssemblyPath = Path.Combine(AppContext.BaseDirectory, "test.dll");


            var ids = new List<string>();
            var results = new List<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
            var settings = OneTimeUnitTestSetup.SignToolSettings;

            foreach (var filepath in srcFiles)
            {
                File.Copy("test.dll", Path.Combine(AppContext.BaseDirectory, filepath), true);
                using var fs = new FileStream(srcAssemblyPath, FileMode.Open, FileAccess.Read);
                var ur = await BatchedSigning.Sign(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(), fs, settings, NSubstitute.Substitute.For<ILogger>());
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
                foreach (var id in ids)
                {
                    string assemblyPath = id;
                    var dr = await BatchedSigning.Get(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(), id, NSubstitute.Substitute.For<ILogger>());

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
                        await File.WriteAllBytesAsync(assemblyPath, file.FileContents.ToArray());
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

            foreach (var id in ids)
            {
                Assert.IsTrue(Certs.ValidateDigitalSignature(id, OneTimeUnitTestSetup.certPath, OneTimeUnitTestSetup.password));
                File.Delete(id);
            }

        }
    }
}
