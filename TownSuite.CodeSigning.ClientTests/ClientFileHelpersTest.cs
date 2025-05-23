using NUnit.Framework;
using System.IO;
using System.Linq;

namespace TownSuite.CodeSigning.ClientTests
{
    [TestFixture]
    public class FileHelpersTests
    {
        private string _tempDir;
        private string _signedDllSrc;
        private string _unsignedDllSrc;
        private string _signedDllCopy;
        private string _unsignedDllCopy;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(_tempDir);

            // Adjust these paths as needed to point to your test asset DLLs
            // For example, if you have them in a TestAssets folder and set to "Copy if newer" in project
            var testDir = TestContext.CurrentContext.TestDirectory;
            _signedDllSrc = Path.Combine(testDir, "test_already_signed.dll");
            _unsignedDllSrc = Path.Combine(testDir, "test_unsigned.dll");

            // Copy to temp dir for isolation
            _signedDllCopy = Path.Combine(_tempDir, "test_already_signed.dll");
            _unsignedDllCopy = Path.Combine(_tempDir, "test_unsigned.dll");
            File.Copy(_signedDllSrc, _signedDllCopy, true);
            File.Copy(_unsignedDllSrc, _unsignedDllCopy, true);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, true);
            }
            catch { /* ignore cleanup errors */ }
        }

        [Test]
        public void ReturnsEmptyList_WhenNoFilesMatch()
        {
            var result = FileHelpers.CreateFileList(new[] { "doesnotexist.dll" }, _tempDir);
            Assert.IsEmpty(result);
        }

        [Test]
        public void ReturnsFile_WhenFileIsUnsignedDll()
        {
            var result = FileHelpers.CreateFileList(new[] { "test_unsigned.dll" }, _tempDir);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(_unsignedDllCopy));
        }

        [Test]
        public void SkipsFile_WhenFileIsAlreadySignedDll()
        {
            var result = FileHelpers.CreateFileList(new[] { "test_already_signed.dll" }, _tempDir);
            Assert.IsEmpty(result);
        }

        [Test]
        public void HandlesWildcards_FindsOnlyUnsignedDll()
        {
            var result = FileHelpers.CreateFileList(new[] { "test_*.dll" }, _tempDir);
            // Only unsigned should be returned, as signed is skipped
            Assert.That(result, Is.EquivalentTo(new[] { _unsignedDllCopy }));
        }

        [Test]
        public void HandlesFullPath_FindsUnsignedDll()
        {
            var result = FileHelpers.CreateFileList(new[] { _unsignedDllCopy }, "");
            Assert.That(result, Is.EquivalentTo(new[] { _unsignedDllCopy }));
        }
    }
}