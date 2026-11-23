using System.Net;
using BD_RGR.Api.Auth;
using BD_RGR.Api.Data;
using BD_RGR.Api.Reports;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(FindLocalSettings(builder.Environment.ContentRootPath),
    optional: true, reloadOnChange: false);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Loopback, builder.Configuration.GetValue("ApiPort", 5080));
    options.Limits.MaxRequestBodySize = 64 * 1024;
});

builder.Services.AddSingleton<AccountsDatabase>();
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<IPasswordHasher<string>, PasswordHasher<string>>();
builder.Services.AddSingleton<AuthEndpoints>();
builder.Services.AddSingleton<BusinessDatabase>();
builder.Services.AddSingleton<DataEndpoints>();
builder.Services.AddSingleton<ReportingDatabase>();
builder.Services.AddSingleton<ReportResults>();
builder.Services.AddSingleton<CsvReport>();
builder.Services.AddSingleton<ReportEndpoints>();

var app = builder.Build();
app.MapAuthEndpoints();
app.MapDataEndpoints();
app.MapReportEndpoints();
await app.RunAsync();

static string FindLocalSettings(string contentRoot)
{
    const string fileName = "appsettings.Local.json";
    var configuredPath = Path.Combine(contentRoot, fileName);
    if (File.Exists(configuredPath))
        return configuredPath;

    var executablePath = Path.Combine(AppContext.BaseDirectory, fileName);
    if (File.Exists(executablePath))
        return executablePath;

    // A direct launch from bin can use the project settings without copying credentials into build output.
    for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
    {
        if (File.Exists(Path.Combine(folder.FullName, "BD_RGR.Api.csproj")))
            return Path.Combine(folder.FullName, fileName);
    }

    return configuredPath;
}
