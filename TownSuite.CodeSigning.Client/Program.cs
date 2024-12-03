using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using TownSuite.CodeSigning.Client;

string[] filepaths = null;
string folder = string.Empty;
string url = string.Empty;
string baseurl = string.Empty;
string token = string.Empty;
bool quickFail = false;
bool ignoreFailures = false;
int timeoutInMs = 10000;

for (int i = 0; i < args.Length; i++)
{
    if (string.Equals(args[i], "-file", StringComparison.InvariantCultureIgnoreCase))
    {
        filepaths = args[i + 1].Split(";");
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
    else if (string.Equals(args[i], "-timeout", StringComparison.InvariantCultureIgnoreCase))
    {
        if (!int.TryParse(args[i + 1], out timeoutInMs))
        {
            Console.WriteLine($"-timeout value failed to parse.  defaulting to {timeoutInMs}");
        }
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


if (filepaths == null || filepaths.Length == 0)
{
    Console.WriteLine("-file must be set");
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


var client = new HttpClient
{
    Timeout = TimeSpan.FromMilliseconds(timeoutInMs)
};

//client.BaseAddress = new Uri(url);
if (!string.IsNullOrWhiteSpace(token))
{
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
}


try
{
    bool failures = false;
    failures = await ProcessFiles(filepaths, url, quickFail, ignoreFailures);

    if (failures && !ignoreFailures)
    {
        Environment.Exit(-1);
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
    Console.WriteLine("-url \"url to signing server\"");
    Console.WriteLine("-token \"the auth token\" or -tokenfile \"path to plain text file holding token\"");
    Console.WriteLine("-quickfail if this is set the program will exit on the first faliure.");
    Console.WriteLine("-ignorefailures if this is set the program will ignore all errors and override quickfail.");
    Console.WriteLine("-timeout \"10000\"");
    Console.WriteLine("    Timeout is in ms.  Defaults to 10000.");
    Console.WriteLine("");
    Console.WriteLine("-poll");
    Console.WriteLine("    Poll the server for the status of the file.");
    Console.WriteLine("    If the file is ready it is downloaded, otherwise poll every second for 240 seconds.");
    Console.WriteLine("");
    Console.WriteLine("Example");
    Console.WriteLine(
        ".\\TownSuite.CodeSigning.Client.exe -file \"C:\\some\file.dll\" -url \"https://localhost:5000/sign\" -token \"the token\"");
}


async Task<bool> ProcessFiles(string[] filepaths, string url, bool quickFail, bool ignoreFailures)
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
                files.Add(Path.Combine(folder, file));
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
                files.Add(file);
            }
        }

    }


    var signer = new SigningClient(client, url);

    bool signingServiceIsOnline = await signer.HealthCheck();
    if (!signingServiceIsOnline)
    {
        Environment.Exit(-1);
    }

    var uploadFailures = await signer.UploadFiles(quickFail, ignoreFailures, filepaths);
    if (uploadFailures.Length > 0)
    {
        foreach (var result in uploadFailures)
        {
            Console.WriteLine($"Failed to sign file: {result.FailedFile}");
            Console.WriteLine(result.Message);
        }
        return false;
    }

    var downloadResults = await signer.DownloadSignedFiles(quickFail, ignoreFailures);
    if (downloadResults.Length > 0)
    {
        foreach (var result in downloadResults)
        {
            Console.WriteLine($"Failed to download file: {result.FailedFile}");
            Console.WriteLine(result.Message);
        }
        return false;
    }

    return true;
}


