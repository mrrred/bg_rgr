using System.IO;
using System.Text.Json;

namespace BD_RGR.Services;

internal sealed record ClientSettings(Uri ApiBaseUrl, string DefaultLanguage)
{
    public static ClientSettings Load()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "clientsettings.json");
        SettingsFile? settings;
        try
        {
            settings = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(file));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new ClientSettingsException("clientsettings.json");
        }

        if (settings is null)
            throw new ClientSettingsException("clientsettings.json");

        if (!Uri.TryCreate(settings.ApiBaseUrl, UriKind.Absolute, out var url)
            || url.Scheme is not ("http" or "https") || !url.IsLoopback
            || url.PathAndQuery != "/" || url.Fragment.Length > 0 || url.UserInfo.Length > 0)
        {
            throw new ClientSettingsException("ApiBaseUrl");
        }

        if (settings.DefaultLanguage is not ("ru" or "en"))
            throw new ClientSettingsException("DefaultLanguage");

        return new ClientSettings(url, settings.DefaultLanguage);
    }

    private sealed record SettingsFile(string? ApiBaseUrl, string? DefaultLanguage);
}

internal sealed class ClientSettingsException(string field) : Exception
{
    public string Field { get; } = field;
}
