using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;

string[] filepaths = null;
string folder = string.Empty;
string url = string.Empty;
string token = string.Empty;
bool quickFail = false;
bool ignoreFailures = false;
int timeoutInMs = 10000;
int concurrent = 1;

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
    else if (string.Equals(args[i], "-token", StringComparison.InvariantCultureIgnoreCase))
    {
        token = args[i + 1];
    }
    else if (string.Equals(args[i], "-tokenfile", StringComparison.InvariantCultureIgnoreCase))
    {
        string tokenFile = args[i + 1];
        token = System.IO.File.ReadAllText(tokenFile);
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
    else if (string.Equals(args[i], "-concurrent", StringComparison.InvariantCultureIgnoreCase))
    {
        if (!int.TryParse(args[i + 1], out concurrent))
        {
            Console.WriteLine($"-concurrent value failed to parse.  defaulting to {concurrent}");
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
    bool signingServiceIsOnline = await HealthCheck(url);
    if (!signingServiceIsOnline)
    {
        Environment.Exit(-1);
    }

    bool failures = false;
    failures = await ProcessFiles(filepaths, url, quickFail, ignoreFailures, concurrent);

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
    Console.WriteLine("-concurrent \"4\"");
    Console.WriteLine("    How many files to process concurrently.  Defaults to 1.");
    Console.WriteLine("");
    Console.WriteLine("Example");
    Console.WriteLine(
        ".\\TownSuite.CodeSigning.Client.exe -file \"C:\\some\file.dll\" -url \"https://localhost:5000/sign\" -token \"the token\"");
}


async Task<bool> ProcessFiles(string[] filepaths, string url, bool quickFail, bool ignoreFailures, int concurrentt)
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

    var tasks = new ConcurrentBag<Task<bool>>();
    bool failures = false;
    int count = 0;
    foreach (var filepath in files)
    {
        tasks.Add(CallSigningService(url, quickFail, ignoreFailures, filepath));
        count = count + 1;
        if (count % concurrent == 0)
        {
            bool fail = (await Task.WhenAll(tasks)).Any(p => p == true);
            if (fail) failures = true;
            tasks.Clear();
        }
    }

    if (tasks.Count > 0)
    {
        bool fail = (await Task.WhenAll(tasks)).Any(p => p == true);
        if (fail) failures = true;
    }

    return failures;
}

async Task<bool> HealthCheck(string url)
{
    try
    {
        var urk = new Uri(url);
        string healthCheckUrl = $"{urk.Scheme}://{urk.Host}:{urk.Port}/healthz";

        var response = await client.GetAsync(healthCheckUrl);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }
        else
        {
            Console.WriteLine("Health check failed");
            var failedOutput = await response.Content.ReadAsStringAsync();
            Console.WriteLine(failedOutput);
            return false;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("Health check failed" + ex.Message);
        return false;
    }

}


async Task<bool> CallSigningService(string url, bool quickFail, bool ignoreFailures,
    string filepath)
{
    bool failures = false;
    try
    {
        var isHealthy = await HealthCheck(url);
        if (!isHealthy)
        {
            return false;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        using var fs = File.OpenRead(filepath);
        request.Content = new StreamContent(fs);
        var response = await client.SendAsync(request);
        fs.Close();
        if (response.IsSuccessStatusCode)
        {
            using var resultStream = await response.Content.ReadAsStreamAsync();
            using var memoryStream = new MemoryStream();
            await resultStream.CopyToAsync(memoryStream);
            await File.WriteAllBytesAsync(filepath, memoryStream.ToArray());
            Console.WriteLine($"Signed file: {filepath}");
        }
        else
        {
            failures = true;
            Console.WriteLine($"Failed to sign file: {filepath}");
            var failedOutput = await response.Content.ReadAsStringAsync();
            Console.WriteLine(failedOutput);

            if (quickFail && !ignoreFailures)
            {
                Console.WriteLine("Quick fail");
                Environment.Exit(-1);
            }
        }
    }
    catch (Exception)
    {
        Console.WriteLine($"Failed to sign file: {filepath}");
        throw;
    }

    return failures;
}
