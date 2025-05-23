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
        if (ext != ".exe" && ext != ".dll")
        {
            return false;
        }
        return true;
    }

    static bool FileAlreayHasDigitalSignature(string file)
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
            if (IsValidFile(file) && !FileAlreayHasDigitalSignature(file))
            {
                finalFiles.Add(file);
            }
        }

        return finalFiles;
    }
}