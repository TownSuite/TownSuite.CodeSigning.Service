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

        using var signer = new Signer(settings);
        var results = signer.Sign(workingFilePath);

        if (results.IsSigned)
        {
            var file = await File.ReadAllBytesAsync(workingFilePath);
            return Results.File(file);
        }
        return Results.Problem(title: "Failure to sign", detail: results.Message, statusCode: 500);
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
        return Results.Problem(title: "Failure to sign", detail: ex.Message ?? "", statusCode: 500); 
    }
    finally
    {
        File.Delete(workingFilePath);
    }
});

static string GetTempFolder()
{
    return Path.Combine(Path.GetTempPath(), "townsuite", "codesigning");
}

app.Run();
