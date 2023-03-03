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
}

if (string.IsNullOrWhiteSpace(filepath))
{
    Console.WriteLine("-file must be set");
    System.Environment.Exit(-1);
}

if (string.IsNullOrWhiteSpace(url))
{
    Console.WriteLine("-url must be set");
    System.Environment.Exit(-1);
}
if (string.IsNullOrWhiteSpace(token))
{
    Console.WriteLine("-token must be set");
    System.Environment.Exit(-1);
}

var client = new HttpClient
{
    BaseAddress = new(url)
};

await using var inputStream = System.IO.File.OpenRead(filepath);
using var request = new HttpRequestMessage(HttpMethod.Post, "sign");
using var content = new MultipartFormDataContent
{
    { new StreamContent(inputStream), "file", System.IO.Path.GetFileName(filepath) }
};

request.Content = content;

var response = await client.SendAsync(request);
inputStream.Close();

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
