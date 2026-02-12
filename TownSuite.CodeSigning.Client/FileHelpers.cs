using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

public static class FileHelpers
{
    static bool IsValidFile(string file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return false;
        }
        if (!System.IO.File.Exists(file))
        {
            return false;
        }
        var ext = Path.GetExtension(file);
        if (ext != ".exe" && ext != ".dll" && ext != ".msi" && ext != "msix")
        {
            return false;
        }
        return true;
    }

    static bool FileAlreadyHasDigitalSignature(string file)
    {
        try
        {
            using (var cert = new X509Certificate2(file))
            {
                if (cert != null)
                {
                    return true;
                }
            }
        }
        catch (Exception)
        {
            // Ignore any exceptions
        }

        return false;
    }

    public static List<string> CreateFileList(string[] filepaths, string folder)
    {
        var files = new List<string>();
        if (!string.IsNullOrWhiteSpace(folder))
        {
            foreach (var file in filepaths)
            {
                if (file.Contains("*"))
                {
                    // wild cards
                    string pattern = Path.GetFileName(file);
                    string[] matchingFiles = Directory.GetFiles(folder, pattern);
                    files.AddRange(matchingFiles);
                }
                else
                {
                    string fullFilePath = Path.Combine(folder, file);
                    if (System.IO.File.Exists(fullFilePath))
                    {
                        files.Add(fullFilePath);
                    }
                }
            }
        }
        else
        {
            foreach (var file in filepaths)
            {
                if (file.Contains("*"))
                {
                    // wildcards
                    string directory = Path.GetDirectoryName(file);
                    string pattern = Path.GetFileName(file);
                    string[] matchingFiles = Directory.GetFiles(directory, pattern);
                    files.AddRange(matchingFiles);
                }
                else
                {
                    if (System.IO.File.Exists(file))
                    {
                        files.Add(file);
                    }
                }
            }
        }

        var finalFiles = new List<string>();
        foreach (var file in files)
        {
            if (IsValidFile(file) && !FileAlreadyHasDigitalSignature(file))
            {
                finalFiles.Add(file);
            }
        }

        return finalFiles;
    }

    /// <summary>
    /// Builds a combined file list from multiple folder paths, each using the same file patterns.
    /// </summary>
    public static List<string> CreateFileListFromMultipleFolders(string[] filepaths, string[] folders)
    {
        var pairs = new List<(string Folder, string[] Files)>();
        foreach (string folder in folders)
        {
            string trimmed = folder.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                pairs.Add((trimmed, filepaths));
            }
        }

        return CreateFileListFromFolderFilePairs(pairs);
    }

    /// <summary>
    /// Builds a combined file list from folder/file pairs where each folder has its own file patterns.
    /// </summary>
    public static List<string> CreateFileListFromFolderFilePairs(List<(string Folder, string[] Files)> folderFilePairs)
    {
        ArgumentNullException.ThrowIfNull(folderFilePairs);

        var allFiles = new List<string>();
        foreach (var (folder, files) in folderFilePairs)
        {
            string trimmed = folder.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            allFiles.AddRange(CreateFileList(files, trimmed));
        }

        return allFiles;
    }

    /// <summary>
    /// Recursively scans parent folders for files matching the given patterns.
    /// Each entry is a parent folder paired with its own file patterns.
    /// </summary>
    public static List<string> CreateFileListRecursive(List<(string Folder, string[] Files)> folderFilePairs)
    {
        ArgumentNullException.ThrowIfNull(folderFilePairs);

        var allFiles = new List<string>();
        foreach (var (parentFolder, filePatterns) in folderFilePairs)
        {
            string trimmed = parentFolder.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || !Directory.Exists(trimmed))
            {
                continue;
            }

            foreach (string pattern in filePatterns)
            {
                string trimmedPattern = pattern.Trim();
                if (string.IsNullOrWhiteSpace(trimmedPattern))
                {
                    continue;
                }

                if (trimmedPattern.Contains('*') || trimmedPattern.Contains('?'))
                {
                    string[] matchingFiles = Directory.GetFiles(trimmed, trimmedPattern, SearchOption.AllDirectories);
                    allFiles.AddRange(matchingFiles);
                }
                else
                {
                    // Exact filename — search all subdirectories
                    string[] matchingFiles = Directory.GetFiles(trimmed, trimmedPattern, SearchOption.AllDirectories);
                    allFiles.AddRange(matchingFiles);
                }
            }
        }

        var finalFiles = new List<string>();
        foreach (var file in allFiles)
        {
            if (IsValidFile(file) && !FileAlreadyHasDigitalSignature(file))
            {
                finalFiles.Add(file);
            }
        }

        return finalFiles;
    }

    /// <summary>
    /// Computes a SHA-256 hash of a file's contents, returned as a lowercase hex string.
    /// </summary>
    public static string ComputeFileHash(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        using var stream = File.OpenRead(filePath);
        byte[] hashBytes = SHA256.HashData(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Groups files by content hash. Returns a dictionary mapping each canonical file
    /// (the first encountered with a given hash) to all other files with the same content.
    /// </summary>
    public static (List<string> UniqueFiles, Dictionary<string, List<string>> DuplicateMap) DeduplicateFiles(
        List<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        // hash -> list of file paths with that hash
        var hashGroups = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (string file in files)
        {
            string hash = ComputeFileHash(file);
            if (!hashGroups.TryGetValue(hash, out var group))
            {
                group = new List<string>();
                hashGroups[hash] = group;
            }

            group.Add(file);
        }

        var uniqueFiles = new List<string>();
        // canonical file path -> list of duplicate file paths (excluding the canonical)
        var duplicateMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var group in hashGroups.Values)
        {
            string canonical = group[0];
            uniqueFiles.Add(canonical);

            if (group.Count > 1)
            {
                duplicateMap[canonical] = group.GetRange(1, group.Count - 1);
            }
        }

        return (uniqueFiles, duplicateMap);
    }

    /// <summary>
    /// After signing, copies each signed canonical file to all its duplicate locations.
    /// </summary>
    public static void CopySignedFilesToDuplicates(Dictionary<string, List<string>> duplicateMap)
    {
        ArgumentNullException.ThrowIfNull(duplicateMap);

        foreach (var (canonical, duplicates) in duplicateMap)
        {
            foreach (string duplicate in duplicates)
            {
                File.Copy(canonical, duplicate, overwrite: true);
                Console.WriteLine($"Copied signed file to duplicate location: {duplicate}");
            }
        }
    }
}