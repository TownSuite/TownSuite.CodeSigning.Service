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
        static readonly string SignToolPath = System.Environment.GetEnvironmentVariable("SIGNTOOL_PATH")
            ?? "C:\\Program Files (x86)\\Windows Kits\\10\\bin\\10.0.26100.0\\x64\\signtool.exe";
        public static Settings? SignToolSettings { get; private set; }
        public static DetachedSignerSettings? DetachedSettings { get; private set; }

        [OneTimeSetUp]
        public void Setup()
        {
            Certs.CreateTestCert(certPath, password);
            SignToolSettings = new Settings()
            {
                MaxRequestBodySize = 1000000,
                SignToolOptions = "sign /fd SHA256 /f \"{BaseDirectory}testcert.pfx\" /p \"password\" /t \"http://timestamp.digicert.com\" /v \"{FilePath}\"",
                SignToolPath = SignToolPath,
                SigntoolTimeoutInMs = 10000,
                SemaphoreSlimProcessPerCpuLimit=1
            };
            DetachedSettings = new DetachedSignerSettings()
            {
                CertificateFilePath = "{BaseDirectory}testcert.pfx",
                CertificatePassword = password,
                DigestAlgorithm = "SHA256"
            };

            CodeSigning.Service.Queuing.SetSemaphore(SignToolSettings);
        }

        [OneTimeTearDown]
        public void Teardown()
        {
            System.IO.File.Delete(certPath);
        }
    }
}