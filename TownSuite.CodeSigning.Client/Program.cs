string filepath = string.Empty;
string url = string.Empty;
string token = string.Empty;

for (int i = 0; i < args.Length; i++)
{
    if (string.Equals(args[i], "-file", StringComparison.InvariantCultureIgnoreCase))
    {
        filepath = args[i + 1];
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

if (string.IsNullOrWhiteSpace(filepath))
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
    BaseAddress = new(url)
};

var request = new HttpRequestMessage(HttpMethod.Post, url);
using var fs = File.OpenRead(filepath);
request.Content = new StreamContent(fs);
var response = await client.SendAsync(request);
fs.Close();
if (response.IsSuccessStatusCode)
{
    using var resultStream = await  response.Content.ReadAsStreamAsync();
    using var memoryStream = new MemoryStream();
    resultStream.CopyTo(memoryStream);
    await File.WriteAllBytesAsync(filepath, memoryStream.ToArray());
    Console.WriteLine($"Signed file: {filepath}");
}
else
{
    Console.WriteLine("Failed to sign file");
}

void PrintHelp()
{
    Console.WriteLine();
    Console.WriteLine("Options");
    Console.WriteLine("-help --help -h --h /?");
    Console.WriteLine("-file \"path to dll or exe\"");
    Console.WriteLine("-url \"url to signing server\"");
    Console.WriteLine("-token \"the auth token\" or -tokenfile \"path to plain text file holding token\"");
    Console.WriteLine("");
    Console.WriteLine("Example");
    Console.WriteLine(".\\TownSuite.CodeSigning.Client.exe -file \"C:\\some\file.dll\" -url \"https://localhost:5000/sign\" -token \"the token\"");
}