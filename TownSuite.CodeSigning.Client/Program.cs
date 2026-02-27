using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using TownSuite.CodeSigning.Client;

string[] filepaths = null;
string folder = string.Empty;
var folderFilePairs = new List<(string Folder, string[] Files)>();
var recursiveFolderFilePairs = new List<(string Folder, string[] Files)>();
string url = string.Empty;
string baseurl = string.Empty;
string token = string.Empty;
bool quickFail = false;
bool ignoreFailures = false;
bool ignoreCerts = false;
bool detached = false;
int timeoutInMs = 10000;
int batchTimeoutInSeconds = 1200;
string[] excludeFolders = Array.Empty<string>();

for (int i = 0; i < args.Length; i++)
{
    if (string.Equals(args[i], "-file", StringComparison.InvariantCultureIgnoreCase))
    {
        filepaths = args[i + 1].Split(";", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }
    else if (string.Equals(args[i], "-folders", StringComparison.InvariantCultureIgnoreCase))
    {
        string value = args[i + 1];
        int pipeIndex = value.IndexOf('|');
        if (pipeIndex < 0)
        {
            Console.WriteLine("-folders entries must use | to separate folder from files. Example: -folders \"C:\\path|*.dll;*.exe\"");
            PrintHelp();
            System.Environment.Exit(-1);
        }

        string folderPath = value[..pipeIndex].Trim();
        string[] filePatterns = value[(pipeIndex + 1)..].Split(";", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        folderFilePairs.Add((folderPath, filePatterns));
    }
    else if (string.Equals(args[i], "-rfolder", StringComparison.InvariantCultureIgnoreCase))
    {
        string value = args[i + 1];
        int pipeIndex = value.IndexOf('|');
        if (pipeIndex < 0)
        {
            Console.WriteLine("-rfolder entries must use | to separate folder from files. Example: -rfolder \"C:\\path|*.dll;*.exe\"");
            PrintHelp();
            System.Environment.Exit(-1);
        }

        string folderPath = value[..pipeIndex].Trim();
        string[] filePatterns = value[(pipeIndex + 1)..].Split(";", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        recursiveFolderFilePairs.Add((folderPath, filePatterns));
    }
    else if (string.Equals(args[i], "-folder", StringComparison.InvariantCultureIgnoreCase))
    {
        folder = args[i + 1];
    }
    else if (string.Equals(args[i], "-url", StringComparison.InvariantCultureIgnoreCase))
    {
        url = args[i + 1];
    }
    else if (string.Equals(args[i], "-baseurl", StringComparison.InvariantCultureIgnoreCase))
    {
        baseurl = args[i + 1];
    }
    else if (string.Equals(args[i], "-token", StringComparison.InvariantCultureIgnoreCase))
    {
        token = args[i + 1];
    }
    else if (string.Equals(args[i], "-tokenfile", StringComparison.InvariantCultureIgnoreCase))
    {
        string tokenFile = args[i + 1];
        token = await System.IO.File.ReadAllTextAsync(tokenFile);
    }
    else if (string.Equals(args[i], "-quickfail", StringComparison.InvariantCultureIgnoreCase))
    {
        quickFail = true;
    }
    else if (string.Equals(args[i], "-ignorefailures", StringComparison.InvariantCultureIgnoreCase))
    {
        ignoreFailures = true;
    }
    else if (string.Equals(args[i], "-detached", StringComparison.InvariantCultureIgnoreCase))
    {
        detached = true;
    }
    else if (string.Equals(args[i], "-ignorecerts", StringComparison.InvariantCultureIgnoreCase))
    {
        // Accept any HTTPS server certificate (insecure)
        ignoreCerts = true;
    }
    else if (string.Equals(args[i], "-timeout", StringComparison.InvariantCultureIgnoreCase))
    {
        if (!int.TryParse(args[i + 1], out timeoutInMs))
        {
            Console.WriteLine($"-timeout value failed to parse.  defaulting to {timeoutInMs}");
        }
    }
    else if (string.Equals(args[i], "-batchtimeout", StringComparison.InvariantCultureIgnoreCase))
    {
        if (!int.TryParse(args[i + 1], out batchTimeoutInSeconds))
        {
            Console.WriteLine($"-batchtimeout value failed to parse.  defaulting to {batchTimeoutInSeconds}");
        }
    }
    else if (string.Equals(args[i], "-excludefolders", StringComparison.InvariantCultureIgnoreCase))
    {
        excludeFolders = args[i + 1].Split(";", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }
    else if (string.Equals(args[i], "-help", StringComparison.InvariantCultureIgnoreCase)
             || string.Equals(args[i], "--help", StringComparison.InvariantCultureIgnoreCase)
             || string.Equals(args[i], "-h", StringComparison.InvariantCultureIgnoreCase)
             || string.Equals(args[i], "--h", StringComparison.InvariantCultureIgnoreCase)
             || string.Equals(args[i], "/?", StringComparison.InvariantCultureIgnoreCase)
            )
    {
        PrintHelp();
    }
}

if (string.IsNullOrWhiteSpace(baseurl))
{
    baseurl = url;
}


if ((filepaths == null || filepaths.Length == 0) && folderFilePairs.Count == 0 && recursiveFolderFilePairs.Count == 0)
{
    Console.WriteLine("-file must be set (or use -folders / -rfolder)");
    PrintHelp();
    System.Environment.Exit(-1);
}

if (string.IsNullOrWhiteSpace(url))
{
    Console.WriteLine("-url must be set");
    PrintHelp();
    System.Environment.Exit(-1);
}

if (string.IsNullOrWhiteSpace(token))
{
    Console.WriteLine("-token must be set");
    PrintHelp();
    System.Environment.Exit(-1);
}


HttpClient client;
if (ignoreCerts)
{
    var handler = new HttpClientHandler();
    // Accept any server certificate (insecure - use only for testing)
    handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
    client = new HttpClient(handler)
    {
        Timeout = TimeSpan.FromMilliseconds(timeoutInMs)
    };
}
else
{
    client = new HttpClient
    {
        Timeout = TimeSpan.FromMilliseconds(timeoutInMs)
    };
}

//client.BaseAddress = new Uri(url);
if (!string.IsNullOrWhiteSpace(token))
{
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
}


try
{
    bool failures = false;
    failures = await ProcessFiles(filepaths, url, quickFail, ignoreFailures, folder, folderFilePairs, recursiveFolderFilePairs, detached);

    if (failures && !ignoreFailures)
    {
        Environment.Exit(-3);
    }
}
catch (Exception ex)
{
    Console.WriteLine(ex);
    if (!ignoreFailures)
    {
        Environment.Exit(-2);
    }
}
finally
{
    client?.Dispose();
}


void PrintHelp()
{
    Console.WriteLine();
    Console.WriteLine("Options");
    Console.WriteLine("-help --help -h --h /?");
    Console.WriteLine("-file \"path to dll or exe\"");
    Console.WriteLine("    the file path can contain multiple files by ; separating them.");
    Console.WriteLine("-folder \"the folder that the dll or exe are located\"");
    Console.WriteLine("    If this is set -file is assumed to just be a filename instead of a full path.");
    Console.WriteLine("-folders \"folder|file1;file2;file3\"");
    Console.WriteLine("    A folder path and its file patterns separated by |. Files within are ; separated.");
    Console.WriteLine("    Can be specified multiple times, each folder gets its own file list.");
    Console.WriteLine("    Duplicate files across folders are detected by SHA-256 hash and only signed once.");
    Console.WriteLine("    After signing, the signed copy is distributed to all duplicate locations.");
    Console.WriteLine("-rfolder \"parentFolder|file1;file2;file3\"");
    Console.WriteLine("    A parent folder path and file patterns separated by |. Files within are ; separated.");
    Console.WriteLine("    Recursively scans all subdirectories for matching files.");
    Console.WriteLine("    Can be specified multiple times. Duplicates are detected and signed once.");
    Console.WriteLine("-excludefolders \"name1;name2;name3\"");
    Console.WriteLine("    Semicolon separated folder names to exclude from recursive searches (e.g. obj;bin;node_modules).");
    Console.WriteLine("-url \"url to signing server\"");
    Console.WriteLine("-token \"the auth token\" or -tokenfile \"path to plain text file holding token\"");
    Console.WriteLine("-quickfail if this is set the program will exit on the first faliure.");
    Console.WriteLine("-ignorefailures if this is set the program will ignore all errors and override quickfail.");
    Console.WriteLine("-detached");
    Console.WriteLine("    If set, use detached signatures.");
    Console.WriteLine("-ignorecerts");
    Console.WriteLine("    If set, accept any HTTPS server certificate (insecure - for testing only)");
    Console.WriteLine("-timeout \"10000\"");
    Console.WriteLine("    Timeout is in ms.  Defaults to 10000.   This is per http request.");
    Console.WriteLine("");
    Console.WriteLine("-batchtimeout");
    Console.WriteLine("    The total time permitted for the whole batch");
    Console.WriteLine("    If not set the default is 1200 seconds.");
    Console.WriteLine("");
    Console.WriteLine("Example");
    Console.WriteLine(
        ".\\TownSuite.CodeSigning.Client.exe -file \"C:\\some\\file.dll\" -url \"https://localhost:5000/sign\" -token \"the token\"");
    Console.WriteLine(
        ".\\TownSuite.CodeSigning.Client.exe -folders \"C:\\publish\\win-x64|*.dll;*.exe\" -folders \"C:\\publish\\linux-x64|*.dll;*.so\" -url \"https://localhost:5000/sign\" -token \"the token\"");
    Console.WriteLine(
        ".\\TownSuite.CodeSigning.Client.exe -rfolder \"C:\\publish|*.dll;*.exe\" -url \"https://localhost:5000/sign\" -token \"the token\"");
}


async Task<bool> ProcessFiles(string[]? filepaths, string url, bool quickFail, bool ignoreFailures,
    string folder, List<(string Folder, string[] Files)> folderFilePairs,
    List<(string Folder, string[] Files)> recursiveFolderFilePairs, bool detached = false, string[]? excludeFolderNames = null)
{
    var files = new List<string>();

    if (folderFilePairs.Count > 0)
    {
        files.AddRange(FileHelpers.CreateFileListFromFolderFilePairs(folderFilePairs, detached));
    }

    if (recursiveFolderFilePairs.Count > 0)
    {
        files.AddRange(FileHelpers.CreateFileListRecursive(recursiveFolderFilePairs, detached, excludeFolderNames));
    }

    // Also include any standalone -file/-folder files
    if (filepaths != null && filepaths.Length > 0)
    {
        files.AddRange(FileHelpers.CreateFileList(filepaths, folder, detached));
    }

    if (files.Count == 0)
    {
        Console.WriteLine("No files found to sign.");
        return false;
    }

    // Deduplicate files by content hash so each unique file is only signed once
    var (uniqueFiles, duplicateMap) = FileHelpers.DeduplicateFiles(files);

    int totalFiles = files.Count;
    int uniqueCount = uniqueFiles.Count;
    int duplicateCount = totalFiles - uniqueCount;
    if (duplicateCount > 0)
    {
        Console.WriteLine($"Found {totalFiles} files to sign, {duplicateCount} duplicates detected.");
        Console.WriteLine($"Signing {uniqueCount} unique files.");
    }

    var signer = new SigningClient(client, url);

    bool signingServiceIsOnline = await signer.HealthCheck();
    if (!signingServiceIsOnline)
    {
        Console.WriteLine("Signing service is not available.");
        Environment.Exit(-2);
    }

    var uploadFailures = await signer.UploadFiles(quickFail, ignoreFailures, uniqueFiles.ToArray(), detached);
    if (uploadFailures.Length > 0)
    {
        foreach (var result in uploadFailures)
        {
            Console.WriteLine($"Failed to sign file: {result.FailedFile}");
            Console.WriteLine(result.Message);
        }
        return true;
    }

    var downloadResults = await signer.DownloadSignedFiles(quickFail, ignoreFailures, batchTimeoutInSeconds);
    if (downloadResults.Length > 0)
    {
        foreach (var result in downloadResults)
        {
            Console.WriteLine($"Failed to download file: {result.FailedFile}");
            Console.WriteLine(result.Message);
        }
        return true;
    }

    // Copy signed files to all duplicate locations
    if (duplicateMap.Count > 0)
    {
        FileHelpers.CopySignedFilesToDuplicates(duplicateMap);
    }

    return false;
}
