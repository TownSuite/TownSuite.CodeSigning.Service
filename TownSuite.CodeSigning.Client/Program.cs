using System.Text;

string[] filepaths = null;
string url = string.Empty;
string token = string.Empty;
bool quickFail = false;

for (int i = 0; i < args.Length; i++)
{
    if (string.Equals(args[i], "-file", StringComparison.InvariantCultureIgnoreCase))
    {
        filepaths = args[i + 1].Split(";");
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


try
{
    var client = new HttpClient
    {
        BaseAddress = new(url),
        Timeout = TimeSpan.FromMilliseconds(10000)
    };

    bool failures = false;
    foreach (var filepath in filepaths)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        using var fs = File.OpenRead(filepath);
        request.Content = new StreamContent(fs);
        var response = await client.SendAsync(request);
        fs.Close();
        if (response.IsSuccessStatusCode)
        {
            using var resultStream = await response.Content.ReadAsStreamAsync();
            using var memoryStream = new MemoryStream();
            resultStream.CopyTo(memoryStream);
            await File.WriteAllBytesAsync(filepath, memoryStream.ToArray());
            Console.WriteLine($"Signed file: {filepath}");
        }
        else
        {
            failures = true;
            Console.WriteLine($"Failed to sign file: {filepath}");
            var failedOutput = await response.Content.ReadAsStringAsync();
            Console.WriteLine(failedOutput);
           
            if (quickFail)
            {
                Console.WriteLine("Quick fail");
                Environment.Exit(-1);
            }
        }
    }

    if (failures)
    {
        Environment.Exit(-1);
    }
}
catch (Exception ex)
{
    Console.WriteLine(ex);
    Environment.Exit(-2);
}


void PrintHelp()
{
    Console.WriteLine();
    Console.WriteLine("Options");
    Console.WriteLine("-help --help -h --h /?");
    Console.WriteLine("-file \"path to dll or exe\"");
    Console.WriteLine("    the file path can contain multiple files by ; separating them.");
    Console.WriteLine("-url \"url to signing server\"");
    Console.WriteLine("-token \"the auth token\" or -tokenfile \"path to plain text file holding token\"");
    Console.WriteLine("quickfail if this is set the program will exit on the first faliure.");
    Console.WriteLine("");
    Console.WriteLine("Example");
    Console.WriteLine(".\\TownSuite.CodeSigning.Client.exe -file \"C:\\some\file.dll\" -url \"https://localhost:5000/sign\" -token \"the token\"");
}