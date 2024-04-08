using TownSuite.CodeSigning.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using System.Threading;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();
builder.Services.AddSingleton<Settings>(s => builder.Configuration.GetSection("Settings").Get<Settings>());
builder.WebHost.UseKestrel(o =>
{
    var settings = builder.Configuration.GetSection("Settings").Get<Settings>();
    o.Limits.MaxRequestBodySize = settings.MaxRequestBodySize;
});

var jwtSettings = builder.Configuration.GetSection("JWT").Get<JwtSettings>();

if (jwtSettings != null && !string.IsNullOrWhiteSpace(jwtSettings.Secret) &&
    !string.Equals(jwtSettings.Secret, "PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
{
    var symmetricSecurityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret));
    builder.Services.AddAuthentication(options => { options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme; })
        .AddJwtBearer(options =>
        {
            options.SaveToken = true;
            options.TokenValidationParameters = new TokenValidationParameters()
            {
                ValidIssuer = jwtSettings.ValidIssuer,
                ValidAudience = jwtSettings.ValidAudience,
                IssuerSigningKey = symmetricSecurityKey
            };
        });
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy(jwtSettings.PolicyName, policy =>
        {
            policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
            policy.RequireAuthenticatedUser();
        });
        options.DefaultPolicy = options.GetPolicy(jwtSettings.PolicyName);
        options.FallbackPolicy = options.DefaultPolicy;
    });
}

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

string workingfolder = GetTempFolder();
InitializeWorkingFolder(workingfolder);

app.UseExceptionHandler(exceptionHandlerApp
    => exceptionHandlerApp.Run(async context
        => await Results.Problem()
                     .ExecuteAsync(context)));

app.MapPost("/sign", async (HttpRequest request, Settings settings) =>
{
    string workingFilePath = Path.Combine(GetTempFolder(), Guid.NewGuid().ToString());
    try
    {
        await using (var fileStream = new FileStream(workingFilePath, FileMode.Create))
        {
            await request.Body.CopyToAsync(fileStream);
        }

        using var signer = new Signer(settings);
        await Queuing.semaphore.WaitAsync();
        var results = await signer.SignAsync(workingFilePath);

        if (results.IsSigned)
        {
            // do not change to a filestream, as the filestream will be disposed before the file is returned
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
        Queuing.semaphore.Release(); 
        Cleanup(workingFilePath);
    }
});

static string GetTempFolder()
{
    return Path.Combine(Path.GetTempPath(), "townsuite", "codesigning");
}

app.MapHealthChecks("/healthz");
app.Run();

static void Cleanup(string workingFilePath)
{
    try
    {
        File.Delete(workingFilePath);
    }
    catch
    {
        Console.WriteLine($"failed to cleanup file {workingFilePath}");
    }
}

static void InitializeWorkingFolder(string workingfolder)
{
    try
    {
        if (Directory.Exists(workingfolder))
        {
            Directory.Delete(workingfolder, true);
        }
    }
    catch (Exception)
    {
        Console.WriteLine("InitializeWorkingFolder failed to delete the old working folder");
    }

    if (!Directory.Exists(workingfolder))
    {
        Directory.CreateDirectory(workingfolder);
    }
}

static class Queuing
{
    // Allows concurrent operations up to the number of CPU cores
    public static SemaphoreSlim semaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
}