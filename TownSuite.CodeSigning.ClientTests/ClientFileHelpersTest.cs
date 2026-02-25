using NUnit.Framework;
using System.Collections.Generic;
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
            var result = FileHelpers.CreateFileList(new[] { "doesnotexist.dll" }, _tempDir, isDetached: false);
            Assert.IsEmpty(result);
        }

        [Test]
        public void ReturnsFile_WhenFileIsUnsignedDll()
        {
            var result = FileHelpers.CreateFileList(new[] { "test_unsigned.dll" }, _tempDir, isDetached: false);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(_unsignedDllCopy));
        }

        [Test]
        public void SkipsFile_WhenFileIsAlreadySignedDll()
        {
            var result = FileHelpers.CreateFileList(new[] { "test_already_signed.dll" }, _tempDir, isDetached: false);
            Assert.IsEmpty(result);
        }

        [Test]
        public void HandlesWildcards_FindsOnlyUnsignedDll()
        {
            var result = FileHelpers.CreateFileList(new[] { "test_*.dll" }, _tempDir, isDetached: false);
            // Only unsigned should be returned, as signed is skipped
            Assert.That(result, Is.EquivalentTo(new[] { _unsignedDllCopy }));
        }

        [Test]
        public void HandlesFullPath_FindsUnsignedDll()
        {
            var result = FileHelpers.CreateFileList(new[] { _unsignedDllCopy }, "", isDetached: false);
            Assert.That(result, Is.EquivalentTo(new[] { _unsignedDllCopy }));
        }
    }

    [TestFixture]
    public class FileHelpersMultiFolderTests
    {
        private string _tempDir1;
        private string _tempDir2;
        private string _tempDir3;
        private string _unsignedDllSrc;

        [SetUp]
        public void SetUp()
        {
            var testDir = TestContext.CurrentContext.TestDirectory;
            _unsignedDllSrc = Path.Combine(testDir, "test_unsigned.dll");

            _tempDir1 = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            _tempDir2 = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            _tempDir3 = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(_tempDir1);
            Directory.CreateDirectory(_tempDir2);
            Directory.CreateDirectory(_tempDir3);

            File.Copy(_unsignedDllSrc, Path.Combine(_tempDir1, "test_unsigned.dll"), true);
            File.Copy(_unsignedDllSrc, Path.Combine(_tempDir2, "test_unsigned.dll"), true);
            File.Copy(_unsignedDllSrc, Path.Combine(_tempDir3, "test_unsigned.dll"), true);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDir1)) Directory.Delete(_tempDir1, true);
                if (Directory.Exists(_tempDir2)) Directory.Delete(_tempDir2, true);
                if (Directory.Exists(_tempDir3)) Directory.Delete(_tempDir3, true);
            }
            catch { /* ignore cleanup errors */ }
        }

        [Test]
        public void CreateFileListFromMultipleFolders_ReturnsFilesFromAllFolders()
        {
            var result = FileHelpers.CreateFileListFromMultipleFolders(
                new[] { "test_unsigned.dll" },
                new[] { _tempDir1, _tempDir2, _tempDir3 }, isDetached: false);

            Assert.That(result, Has.Count.EqualTo(3));
        }

        [Test]
        public void CreateFileListFromMultipleFolders_SkipsEmptyFolderEntries()
        {
            var result = FileHelpers.CreateFileListFromMultipleFolders(
                new[] { "test_unsigned.dll" },
                new[] { _tempDir1, "  ", _tempDir2 }, isDetached: false);

            Assert.That(result, Has.Count.EqualTo(2));
        }

        [Test]
        public void CreateFileListFromFolderFilePairs_EachFolderGetsOwnFiles()
        {
            // Folder1 has test_unsigned.dll, Folder2 also has test_unsigned.dll
            // We ask folder1 for *.dll and folder2 for a non-existent pattern
            var pairs = new List<(string Folder, string[] Files)>
            {
                (_tempDir1, new[] { "test_unsigned.dll" }),
                (_tempDir2, new[] { "nonexistent.dll" }),
                (_tempDir3, new[] { "test_unsigned.dll" })
            };

            var result = FileHelpers.CreateFileListFromFolderFilePairs(pairs, isDetached: false);

            // Only folder1 and folder3 should produce results
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result, Does.Contain(Path.Combine(_tempDir1, "test_unsigned.dll")));
            Assert.That(result, Does.Contain(Path.Combine(_tempDir3, "test_unsigned.dll")));
        }

        [Test]
        public void CreateFileListFromFolderFilePairs_DifferentPatternsPerFolder()
        {
            // Copy the dll with a different name into folder2 to simulate a unique file
            File.Copy(_unsignedDllSrc, Path.Combine(_tempDir2, "special.dll"), true);

            var pairs = new List<(string Folder, string[] Files)>
            {
                (_tempDir1, new[] { "test_unsigned.dll" }),
                (_tempDir2, new[] { "special.dll" })
            };

            var result = FileHelpers.CreateFileListFromFolderFilePairs(pairs, isDetached: false);

            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result, Does.Contain(Path.Combine(_tempDir1, "test_unsigned.dll")));
            Assert.That(result, Does.Contain(Path.Combine(_tempDir2, "special.dll")));
        }

        [Test]
        public void CreateFileListFromFolderFilePairs_SkipsEmptyFolderEntries()
        {
            var pairs = new List<(string Folder, string[] Files)>
            {
                (_tempDir1, new[] { "test_unsigned.dll" }),
                ("  ", new[] { "test_unsigned.dll" }),
                (_tempDir2, new[] { "test_unsigned.dll" })
            };

            var result = FileHelpers.CreateFileListFromFolderFilePairs(pairs, isDetached: false);

            Assert.That(result, Has.Count.EqualTo(2));
        }

        [Test]
        public void CreateFileListFromFolderFilePairs_WildcardsWorkPerFolder()
        {
            var pairs = new List<(string Folder, string[] Files)>
            {
                (_tempDir1, new[] { "*.dll" }),
                (_tempDir2, new[] { "nonexistent_pattern_*.exe" })
            };

            var result = FileHelpers.CreateFileListFromFolderFilePairs(pairs, isDetached: false);

            // Only folder1 should match with its wildcard
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(Path.Combine(_tempDir1, "test_unsigned.dll")));
        }
    }

    [TestFixture]
    public class FileHelpersDeduplicationTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
            }
            catch { /* ignore cleanup errors */ }
        }

        [Test]
        public void ComputeFileHash_ReturnsSameHashForIdenticalFiles()
        {
            string file1 = Path.Combine(_tempDir, "a.dll");
            string file2 = Path.Combine(_tempDir, "b.dll");
            byte[] content = new byte[] { 0x4D, 0x5A, 0x00, 0x01, 0x02, 0x03 };
            File.WriteAllBytes(file1, content);
            File.WriteAllBytes(file2, content);

            string hash1 = FileHelpers.ComputeFileHash(file1);
            string hash2 = FileHelpers.ComputeFileHash(file2);

            Assert.That(hash1, Is.EqualTo(hash2));
        }

        [Test]
        public void ComputeFileHash_ReturnsDifferentHashForDifferentFiles()
        {
            string file1 = Path.Combine(_tempDir, "a.dll");
            string file2 = Path.Combine(_tempDir, "b.dll");
            File.WriteAllBytes(file1, new byte[] { 0x4D, 0x5A, 0x00 });
            File.WriteAllBytes(file2, new byte[] { 0x4D, 0x5A, 0x01 });

            string hash1 = FileHelpers.ComputeFileHash(file1);
            string hash2 = FileHelpers.ComputeFileHash(file2);

            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        [Test]
        public void DeduplicateFiles_ReturnsAllFilesWhenNoDuplicates()
        {
            string file1 = Path.Combine(_tempDir, "a.dll");
            string file2 = Path.Combine(_tempDir, "b.dll");
            File.WriteAllBytes(file1, new byte[] { 0x01 });
            File.WriteAllBytes(file2, new byte[] { 0x02 });

            var (unique, dupeMap) = FileHelpers.DeduplicateFiles(new List<string> { file1, file2 });

            Assert.That(unique, Has.Count.EqualTo(2));
            Assert.That(dupeMap, Is.Empty);
        }

        [Test]
        public void DeduplicateFiles_IdentifiesDuplicatesCorrectly()
        {
            string file1 = Path.Combine(_tempDir, "a.dll");
            string file2 = Path.Combine(_tempDir, "b.dll");
            string file3 = Path.Combine(_tempDir, "c.dll");
            byte[] content = new byte[] { 0x4D, 0x5A, 0x00, 0x01 };
            File.WriteAllBytes(file1, content);
            File.WriteAllBytes(file2, content);
            File.WriteAllBytes(file3, new byte[] { 0xFF });

            var (unique, dupeMap) = FileHelpers.DeduplicateFiles(new List<string> { file1, file2, file3 });

            Assert.That(unique, Has.Count.EqualTo(2));
            Assert.That(dupeMap, Has.Count.EqualTo(1));

            // The canonical file for the duplicate group should map to the other copy
            string canonical = unique.First(f => dupeMap.ContainsKey(f));
            Assert.That(dupeMap[canonical], Has.Count.EqualTo(1));
        }

        [Test]
        public void DeduplicateFiles_HandlesThreeDuplicates()
        {
            string file1 = Path.Combine(_tempDir, "a.dll");
            string file2 = Path.Combine(_tempDir, "b.dll");
            string file3 = Path.Combine(_tempDir, "c.dll");
            byte[] content = new byte[] { 0x4D, 0x5A, 0xAA, 0xBB };
            File.WriteAllBytes(file1, content);
            File.WriteAllBytes(file2, content);
            File.WriteAllBytes(file3, content);

            var (unique, dupeMap) = FileHelpers.DeduplicateFiles(new List<string> { file1, file2, file3 });

            Assert.That(unique, Has.Count.EqualTo(1));
            Assert.That(dupeMap[unique[0]], Has.Count.EqualTo(2));
        }

        [Test]
        public void CopySignedFilesToDuplicates_CopiesContentCorrectly()
        {
            string canonical = Path.Combine(_tempDir, "signed.dll");
            string dup1 = Path.Combine(_tempDir, "dup1.dll");
            string dup2 = Path.Combine(_tempDir, "dup2.dll");

            byte[] signedBytes = new byte[] { 0x4D, 0x5A, 0xAA, 0x01 };
            byte[] originalBytes = new byte[] { 0x4D, 0x5A, 0x00, 0x00 };

            File.WriteAllBytes(canonical, signedBytes);
            File.WriteAllBytes(dup1, originalBytes);
            File.WriteAllBytes(dup2, originalBytes);

            var dupeMap = new Dictionary<string, List<string>>
            {
                { canonical, new List<string> { dup1, dup2 } }
            };

            FileHelpers.CopySignedFilesToDuplicates(dupeMap);

            Assert.That(File.ReadAllBytes(dup1), Is.EqualTo(signedBytes));
            Assert.That(File.ReadAllBytes(dup2), Is.EqualTo(signedBytes));
        }
    }

    [TestFixture]
    public class FileHelpersRecursiveTests
    {
        private string _rootDir;
        private string _unsignedDllSrc;

        [SetUp]
        public void SetUp()
        {
            var testDir = TestContext.CurrentContext.TestDirectory;
            _unsignedDllSrc = Path.Combine(testDir, "test_unsigned.dll");

            _rootDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(_rootDir);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_rootDir)) Directory.Delete(_rootDir, true);
            }
            catch { /* ignore cleanup errors */ }
        }

        [Test]
        public void CreateFileListRecursive_FindsFilesInSubdirectories()
        {
            string sub1 = Path.Combine(_rootDir, "win-x64");
            string sub2 = Path.Combine(_rootDir, "linux-x64");
            string sub3 = Path.Combine(_rootDir, "linux-x64", "nested");
            Directory.CreateDirectory(sub1);
            Directory.CreateDirectory(sub2);
            Directory.CreateDirectory(sub3);

            File.Copy(_unsignedDllSrc, Path.Combine(sub1, "test_unsigned.dll"), true);
            File.Copy(_unsignedDllSrc, Path.Combine(sub2, "test_unsigned.dll"), true);
            File.Copy(_unsignedDllSrc, Path.Combine(sub3, "test_unsigned.dll"), true);

            var pairs = new List<(string Folder, string[] Files)>
            {
                (_rootDir, new[] { "test_unsigned.dll" })
            };

            var result = FileHelpers.CreateFileListRecursive(pairs, isDetached: false);

            Assert.That(result, Has.Count.EqualTo(3));
        }

        [Test]
        public void CreateFileListRecursive_WildcardMatchesAcrossTree()
        {
            string sub1 = Path.Combine(_rootDir, "win-x64");
            string sub2 = Path.Combine(_rootDir, "linux-x64");
            Directory.CreateDirectory(sub1);
            Directory.CreateDirectory(sub2);

            File.Copy(_unsignedDllSrc, Path.Combine(sub1, "test_unsigned.dll"), true);
            File.Copy(_unsignedDllSrc, Path.Combine(sub2, "test_unsigned.dll"), true);

            var pairs = new List<(string Folder, string[] Files)>
            {
                (_rootDir, new[] { "*.dll" })
            };

            var result = FileHelpers.CreateFileListRecursive(pairs, isDetached: false);

            Assert.That(result, Has.Count.EqualTo(2));
        }

        [Test]
        public void CreateFileListRecursive_OnlyMatchesRequestedPattern()
        {
            string sub1 = Path.Combine(_rootDir, "pub");
            Directory.CreateDirectory(sub1);

            File.Copy(_unsignedDllSrc, Path.Combine(sub1, "test_unsigned.dll"), true);
            File.Copy(_unsignedDllSrc, Path.Combine(sub1, "other.dll"), true);

            var pairs = new List<(string Folder, string[] Files)>
            {
                (_rootDir, new[] { "test_unsigned.dll" })
            };

            var result = FileHelpers.CreateFileListRecursive(pairs, isDetached: false);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0], Does.EndWith("test_unsigned.dll"));
        }

        [Test]
        public void CreateFileListRecursive_MultiplePatternsPerFolder()
        {
            string sub1 = Path.Combine(_rootDir, "pub");
            Directory.CreateDirectory(sub1);

            File.Copy(_unsignedDllSrc, Path.Combine(sub1, "app.dll"), true);
            File.Copy(_unsignedDllSrc, Path.Combine(sub1, "app.exe"), true);
            // .txt should not be picked up
            File.WriteAllText(Path.Combine(sub1, "readme.txt"), "hello");

            var pairs = new List<(string Folder, string[] Files)>
            {
                (_rootDir, new[] { "*.dll", "*.exe" })
            };

            var result = FileHelpers.CreateFileListRecursive(pairs, isDetached: false);

            Assert.That(result, Has.Count.EqualTo(2));
        }

        [Test]
        public void CreateFileListRecursive_SkipsNonExistentParentFolder()
        {
            var pairs = new List<(string Folder, string[] Files)>
            {
                (Path.Combine(_rootDir, "does_not_exist"), new[] { "*.dll" })
            };

            var result = FileHelpers.CreateFileListRecursive(pairs, isDetached: false);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void CreateFileListRecursive_DuplicatesAcrossSubfoldersDetectedByDedup()
        {
            string sub1 = Path.Combine(_rootDir, "win-x64");
            string sub2 = Path.Combine(_rootDir, "linux-x64");
            Directory.CreateDirectory(sub1);
            Directory.CreateDirectory(sub2);

            File.Copy(_unsignedDllSrc, Path.Combine(sub1, "test_unsigned.dll"), true);
            File.Copy(_unsignedDllSrc, Path.Combine(sub2, "test_unsigned.dll"), true);

            var pairs = new List<(string Folder, string[] Files)>
            {
                (_rootDir, new[] { "*.dll" })
            };

            var files = FileHelpers.CreateFileListRecursive(pairs, isDetached: false);
            var (unique, dupeMap) = FileHelpers.DeduplicateFiles(files);

            Assert.That(unique, Has.Count.EqualTo(1));
            Assert.That(dupeMap[unique[0]], Has.Count.EqualTo(1));
        }

        [Test]
        public void CreateFileListRecursive_MultipleParentFoldersEachWithOwnPatterns()
        {
            string root2 = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                string sub1 = Path.Combine(_rootDir, "sub");
                string sub2 = Path.Combine(root2, "sub");
                Directory.CreateDirectory(sub1);
                Directory.CreateDirectory(sub2);

                File.Copy(_unsignedDllSrc, Path.Combine(sub1, "test_unsigned.dll"), true);
                File.Copy(_unsignedDllSrc, Path.Combine(sub1, "test_unsigned.exe"), true);
                File.Copy(_unsignedDllSrc, Path.Combine(sub2, "test_unsigned.exe"), true);

                var pairs = new List<(string Folder, string[] Files)>
                {
                    (_rootDir, new[] { "*.dll" }),
                    (root2, new[] { "*.exe" })
                };

                var result = FileHelpers.CreateFileListRecursive(pairs, isDetached: false);

                Assert.That(result, Has.Count.EqualTo(2));
                Assert.That(result.Any(f => f.EndsWith(".dll")), Is.True);
                Assert.That(result.Any(f => f.EndsWith(".exe")), Is.True);
            }
            finally
            {
                try { if (Directory.Exists(root2)) Directory.Delete(root2, true); } catch { }
            }
        }
    }
}