using System.IO.Compression;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using TownSuite.CodeSigning.Service;

namespace TownSuite.CodeSigning.Tests
{
    [TestFixture]
    public class MsixSigningTest
    {
        private const string PublisherMatchingTestCert = "CN=TestCertificate";

        private string _layout = string.Empty;
        private string _msix = string.Empty;
        private readonly List<string> _foldersToClean = new();

        [SetUp]
        public void SetUp()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("msix packaging requires the Windows SDK");
            }

            string makeappx = FindMakeAppx() ?? string.Empty;
            if (string.IsNullOrEmpty(makeappx))
            {
                Assert.Ignore("makeappx.exe not found; install the Windows 10/11 SDK or set MAKEAPPX_PATH");
            }

            string mainExecutable = Path.Combine(Environment.SystemDirectory, "where.exe");
            if (!File.Exists(mainExecutable))
            {
                Assert.Ignore("no sample executable available to pack");
            }

            _layout = Path.Combine(Path.GetTempPath(), "msixtest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_layout);
            _foldersToClean.Add(_layout);

            File.Copy(mainExecutable, Path.Combine(_layout, "App.exe"), true);
            WriteManifest(_layout);

            _msix = Path.Combine(Path.GetTempPath(), $"msixtest-{Guid.NewGuid():N}.msix");
            RunMakeAppx(makeappx, _layout, _msix);
        }

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

            try
            {
                if (!string.IsNullOrEmpty(_msix) && File.Exists(_msix)) File.Delete(_msix);
            }
            catch { }
        }

        private static string? FindMakeAppx() => FindSdkTool("MAKEAPPX_PATH", "makeappx.exe");

        private static string? FindSignTool() => FindSdkTool("SIGNTOOL_PATH", "signtool.exe");

        private static string? FindSdkTool(string environmentVariable, string exeName)
        {
            string? configured = Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

            string kits = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "bin");
            if (!Directory.Exists(kits)) return null;

            return Directory.EnumerateFiles(kits, exeName, SearchOption.AllDirectories)
                .Where(p => p.Contains(Path.DirectorySeparatorChar + "x64" + Path.DirectorySeparatorChar))
                .OrderByDescending(p => p)
                .FirstOrDefault();
        }

        private static Settings BuildSignToolSettings()
        {
            string? signtool = FindSignTool();
            if (string.IsNullOrEmpty(signtool))
            {
                Assert.Ignore("signtool.exe not found; install the Windows 10/11 SDK or set SIGNTOOL_PATH");
            }

            var baseSettings = OneTimeUnitTestSetup.SignToolSettings!;
            return new Settings
            {
                SignToolPath = signtool!,
                SignToolOptions = baseSettings.SignToolOptions,
                SigntoolTimeoutInMs = 60000,
                MaxRequestBodySize = baseSettings.MaxRequestBodySize,
                SemaphoreSlimProcessPerCpuLimit = baseSettings.SemaphoreSlimProcessPerCpuLimit,
                OpenSSL = baseSettings.OpenSSL
            };
        }

        private static void WriteManifest(string layout)
        {
            string manifest = $"""
                <?xml version="1.0" encoding="utf-8"?>
                <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
                         xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities">
                  <Identity Name="TownSuite.SigningTest" Publisher="{PublisherMatchingTestCert}" Version="1.0.0.0" ProcessorArchitecture="x64" />
                  <Properties>
                    <DisplayName>Signing Test</DisplayName>
                    <PublisherDisplayName>TownSuite</PublisherDisplayName>
                    <Logo>Assets\StoreLogo.png</Logo>
                  </Properties>
                  <Dependencies>
                    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.22621.0" />
                  </Dependencies>
                  <Resources><Resource Language="en-us" /></Resources>
                  <Capabilities><rescap:Capability Name="runFullTrust" /></Capabilities>
                  <Applications>
                    <Application Id="App" Executable="App.exe" EntryPoint="Windows.FullTrustApplication">
                      <uap:VisualElements DisplayName="Signing Test" Description="Signing Test" BackgroundColor="transparent"
                                          Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png" />
                    </Application>
                  </Applications>
                </Package>
                """;

            File.WriteAllText(Path.Combine(layout, "AppxManifest.xml"), manifest);

            string assets = Path.Combine(layout, "Assets");
            Directory.CreateDirectory(assets);
            foreach (var name in new[] { "StoreLogo.png", "Square150x150Logo.png", "Square44x44Logo.png" })
            {
                File.WriteAllBytes(Path.Combine(assets, name), MinimalPng);
            }
        }

        private static readonly byte[] MinimalPng =
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82
        };

        private static void RunMakeAppx(string makeappx, string layout, string outFile)
        {
            using var p = new System.Diagnostics.Process();
            p.StartInfo.FileName = makeappx;
            p.StartInfo.Arguments = $"pack /d \"{layout}\" /p \"{outFile}\" /o";
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            p.Start();
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();

            if (p.ExitCode != 0)
            {
                Assert.Ignore($"makeappx could not build a test package: {output}");
            }
        }

        private static bool HasAppxSignature(string path)
        {
            using var zip = ZipFile.OpenRead(path);
            return zip.Entries.Any(e => e.FullName.Equals("AppxSignature.p7x", StringComparison.OrdinalIgnoreCase));
        }

        private async Task<IResult> SignThroughBatchEndpoint(string? extension)
        {
            string batchId = Guid.NewGuid().ToString();
            _foldersToClean.Add(Path.Combine(BatchedSigning.GetTempFolder(), batchId));

            var headers = new Dictionary<string, StringValues>
            {
                ["X-BatchId"] = batchId,
                ["X-BatchReady"] = "true"
            };
            if (extension is not null) headers["X-FileExtension"] = extension;

            var signer = new Signer(BuildSignToolSettings(), NSubstitute.Substitute.For<ILogger<Signer>>());

            await using var body = File.OpenRead(_msix);
            var uploadResult = await BatchedSigning.Sign(headers, body, NSubstitute.Substitute.For<ILogger>(), signer);
            var ok = uploadResult as Microsoft.AspNetCore.Http.HttpResults.Ok<string>;
            Assert.That(ok, Is.Not.Null, "upload should be accepted");

            string id = ok!.Value!.Replace("\"", "");

            var pollHeaders = new Dictionary<string, StringValues> { ["X-BatchId"] = batchId };
            if (extension is not null) pollHeaders["X-FileExtension"] = extension;

            for (int i = 0; i < 200; i++)
            {
                var result = await BatchedSigning.Get(pollHeaders, id, signer);
                if (result is Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult problem
                    && problem.StatusCode == 425)
                {
                    await Task.Delay(100);
                    continue;
                }
                return result;
            }

            Assert.Fail("the signing service never reached a terminal state");
            return null!;
        }

        private static async Task AssertSignedMsixReturned(IResult result, string because)
        {
            if (result is Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult problem)
            {
                Assert.Fail($"{because} - got {problem.StatusCode}: {problem.ProblemDetails.Detail}");
            }

            Assert.That(result, Is.InstanceOf<Microsoft.AspNetCore.Http.HttpResults.FileStreamHttpResult>(), because);

            string downloaded = Path.Combine(Path.GetTempPath(), $"signed-{Guid.NewGuid():N}.msix");
            try
            {
                var streamResult = (Microsoft.AspNetCore.Http.HttpResults.FileStreamHttpResult)result;
                await using (var source = streamResult.FileStream)
                await using (var destination = File.OpenWrite(downloaded))
                {
                    await source.CopyToAsync(destination);
                }

                Assert.That(HasAppxSignature(downloaded), Is.True,
                    "a signed msix carries an AppxSignature.p7x entry");
            }
            finally
            {
                try { if (File.Exists(downloaded)) File.Delete(downloaded); } catch { }
            }
        }

        [Test]
        public async Task MsixKeepingItsExtensionIsSigned()
        {
            var result = await SignThroughBatchEndpoint(".msix");
            await AssertSignedMsixReturned(result, "signtool should accept an msix that still has its extension");
        }

        [Test]
        public async Task MsixWithNoExtensionHeaderIsStillSigned()
        {
            var result = await SignThroughBatchEndpoint(null);
            await AssertSignedMsixReturned(result,
                "a client that sends no X-FileExtension must still get a signed msix via content detection");
        }
    }
}
