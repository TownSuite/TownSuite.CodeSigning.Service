using TownSuite.CodeSigning.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<Settings>(s => builder.Configuration.GetSection("Settings").Get<Settings>());
var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

string workingfolder = GetTempFolder();
if (Directory.Exists(workingfolder))
{
    Directory.Delete(workingfolder, true);
}
Directory.CreateDirectory(workingfolder);

app.UseExceptionHandler(exceptionHandlerApp
    => exceptionHandlerApp.Run(async context
        => await Results.Problem()
                     .ExecuteAsync(context)));

app.MapPost("/sign", async (HttpRequest request, Settings settings) =>
{
    string workingFilePath = Path.Combine(GetTempFolder(), Guid.NewGuid().ToString());
    try
    {
        using (var memoryStream = new MemoryStream())
        {
            await request.Body.CopyToAsync(memoryStream);
            await File.WriteAllBytesAsync(workingFilePath, memoryStream.ToArray());
        }

        var signer = new Signer(settings);
        bool isSigned = signer.Sign(workingFilePath);

        if (isSigned)
        {
            var file = await File.ReadAllBytesAsync(workingFilePath);
            return Results.File(file);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
    }
    finally
    {
        File.Delete(workingFilePath);
    }
    return Results.Problem("Failure to sign", statusCode: 500);
});

static string GetTempFolder()
{
    return Path.Combine(Path.GetTempPath(), "townsuite", "codesigning");
}

app.Run();

