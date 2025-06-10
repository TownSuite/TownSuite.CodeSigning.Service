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
    public class SignerTest
    {
        [Test]
        public async Task Test1()
        {
            // Arrange
            var srcAssemblyPath = "test.dll";
            var assemblyPath = System.IO.Path.Combine(AppContext.BaseDirectory, "test_signed.dll");
            var settings = OneTimeUnitTestSetup.SignToolSettings;

            var signer = new Signer(settings, NSubstitute.Substitute.For<ILogger>());
            // Act
            System.IO.File.Copy(srcAssemblyPath, assemblyPath, true);
            var result = await signer.SignAsync(AppContext.BaseDirectory, [assemblyPath]);
            // Assert

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSigned, Is.EqualTo(true), result.Message);
                Assert.IsTrue(Certs.ValidateDigitalSignature(assemblyPath, OneTimeUnitTestSetup.certPath, OneTimeUnitTestSetup.password));
            });
        }
    }
}
