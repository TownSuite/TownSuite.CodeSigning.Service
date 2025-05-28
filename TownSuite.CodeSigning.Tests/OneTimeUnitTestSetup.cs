using Microsoft.Extensions.Logging;
using TownSuite.CodeSigning.Service;

namespace TownSuite.CodeSigning.Tests
{
    [SetUpFixture]
    public class OneTimeUnitTestSetup
    {
        public const string certPath = "testcert.pfx";
        public const string password = "password";
        // Download signtool as part of the windows sdk https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/
        const string SignToolPath = "C:\\Program Files (x86)\\Microsoft Visual Studio\\Shared\\NuGetPackages\\microsoft.windows.sdk.buildtools\\10.0.26100.1742\\bin\\10.0.26100.0\\x86\\signtool.exe";
        public static Settings? SignToolSettings { get; private set; }

        [OneTimeSetUp]
        public void Setup()
        {
            Certs.CreateTestCert(certPath);
            SignToolSettings = new Settings()
            {
                MaxRequestBodySize = 1000000,
                SignToolOptions = "sign /fd SHA256 /f \"testcert.pfx\" /p \"password\" /t \"http://timestamp.digicert.com\" /v \"{FilePath}\"",
                SignToolPath = SignToolPath,
                SigntoolTimeoutInMs = 10000
            };
        }

        [OneTimeTearDown]
        public void Teardown()
        {
            System.IO.File.Delete(certPath);
        }
    }
}