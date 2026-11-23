using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using BD_RGR.ViewModels;

namespace BD_RGR.Services;

public enum AppTheme { Light, Dark }
public enum TextScale { Normal, Large, ExtraLarge }
public enum TableDensity { Normal, Compact }

public sealed class ProfileSettings : ViewModel, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private ResourceDictionary? _darkPalette;
    private ResourceDictionary? _colorVisionPalette;
    private AppTheme _theme;
    private TextScale _textSize;
    private TableDensity _density;
    private bool _colorVisionMode;
    private bool _applying;
    private bool _saveError;

    public ProfileSettings(string? path = null)
    {
        _path = Path.GetFullPath(path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BD_RGR", "preferences.json"));
        ResetCommand = new Command(Reset);
    }

    public AppTheme Theme { get => _theme; set { if (Set(ref _theme, value)) Changed(); } }
    public TextScale TextSize { get => _textSize; set { if (Set(ref _textSize, value)) Changed(); } }
    public TableDensity Density { get => _density; set { if (Set(ref _density, value)) Changed(); } }
    public bool ColorVisionMode { get => _colorVisionMode; set { if (Set(ref _colorVisionMode, value)) Changed(); } }
    public bool SaveError { get => _saveError; private set => Set(ref _saveError, value); }
    public Command ResetCommand { get; }

    public void Initialize()
    {
        var preferences = new Preferences();
        var migrated = false;
        try
        {
            if (File.Exists(_path))
            {
                using var json = JsonDocument.Parse(File.ReadAllText(_path));
                (preferences, migrated) = ReadPreferences(json.RootElement);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            SaveError = true;
        }

        Apply(preferences);
        Translation.Current.LanguageChanged += OnLanguageChanged;
        if (migrated)
            Save();
    }

    private void Reset()
    {
        Apply(new Preferences());
        Save();
    }

    private void Apply(Preferences preferences)
    {
        _applying = true;
        try
        {
            Theme = preferences.Theme;
            TextSize = preferences.TextSize;
            Density = preferences.Density;
            ColorVisionMode = preferences.ColorVisionMode;
            Translation.Current.Language = preferences.Language;
        }
        finally
        {
            _applying = false;
        }
        ApplyAppearance();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Changed();

    private void Changed()
    {
        if (_applying)
            return;

        ApplyAppearance();
        Save();
    }

    private void Save()
    {
        var preferences = new Preferences(Theme, TextSize, Translation.Current.Language, ColorVisionMode, Density);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_path.ToUpperInvariant())));
        try
        {
            using var gate = new Mutex(false, @"Local\BD_RGR_" + identity);
            var locked = false;
            try
            {
                try { locked = gate.WaitOne(TimeSpan.FromMilliseconds(500)); }
                catch (AbandonedMutexException) { locked = true; }
                if (!locked)
                {
                    SaveError = true;
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, JsonOptions));
                File.Move(temporary, _path, overwrite: true);
                SaveError = false;
            }
            finally
            {
                if (locked)
                    gate.ReleaseMutex();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SaveError = true;
            try { File.Delete(temporary); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
        }
    }

    private static (Preferences Values, bool Migrated) ReadPreferences(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException();

        if (root.TryGetProperty("Guest", out var guest) || root.TryGetProperty("Accounts", out _))
        {
            return (new Preferences(
                TextSize: IsTrue(guest, "LargeText") ? TextScale.Large : TextScale.Normal,
                Language: ReadLanguage(guest),
                Density: IsTrue(guest, "CompactRows") ? TableDensity.Compact : TableDensity.Normal), true);
        }

        return (new Preferences(ReadEnum(root, "Theme", AppTheme.Light), ReadEnum(root, "TextSize", TextScale.Normal),
            ReadLanguage(root), IsTrue(root, "ColorVisionMode"), ReadEnum(root, "Density", TableDensity.Normal)), false);
    }

    private static bool IsTrue(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? ReadString(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static string ReadLanguage(JsonElement data) => ReadString(data, "Language") == "en" ? "en" : "ru";

    private static T ReadEnum<T>(JsonElement data, string name, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(ReadString(data, name), out var value) && Enum.IsDefined(value) ? value : fallback;

    private void ApplyAppearance()
    {
        if (Application.Current is not { } app)
            return;

        var dictionaries = app.Resources.MergedDictionaries;
        if (_colorVisionPalette is not null)
            dictionaries.Remove(_colorVisionPalette);
        if (Theme == AppTheme.Dark)
        {
            _darkPalette ??= Palette("Dark");
            if (!dictionaries.Contains(_darkPalette))
                dictionaries.Add(_darkPalette);
        }
        else if (_darkPalette is not null)
            dictionaries.Remove(_darkPalette);
        if (ColorVisionMode)
        {
            _colorVisionPalette ??= Palette("ColorVision");
            dictionaries.Add(_colorVisionPalette);
        }

        var font = TextSize switch { TextScale.ExtraLarge => 21d, TextScale.Large => 17d, _ => 14d };
        app.Resources["UiFontSize"] = font;
        app.Resources["UiLabelSize"] = font - 1;
        app.Resources["UiHeadingSize"] = font + 16;
        app.Resources["UiSubheadingSize"] = font + 3;
        app.Resources["SidebarWidth"] = new GridLength(TextSize == TextScale.ExtraLarge ? 288d : 252d);
        app.Resources["TableRowHeight"] = 2 * font + (Density == TableDensity.Compact ? 2 : 14);
        app.Resources["TableHeaderHeight"] = 2 * font + 14;
    }

    private static ResourceDictionary Palette(string name) =>
        new() { Source = new Uri($"/BD_RGR;component/Styles/{name}.xaml", UriKind.Relative) };

    public void Dispose() => Translation.Current.LanguageChanged -= OnLanguageChanged;

    private sealed record Preferences(AppTheme Theme = AppTheme.Light, TextScale TextSize = TextScale.Normal,
        string Language = "ru", bool ColorVisionMode = false, TableDensity Density = TableDensity.Normal);
}
