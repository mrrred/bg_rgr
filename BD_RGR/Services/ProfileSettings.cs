using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using BD_RGR.ViewModels;

namespace BD_RGR.Services;

public sealed class ProfileSettings : ViewModel, IDisposable
{
    private readonly string _path;
    private ProfileFile _profile = new();
    private string _defaultLanguage = "ru";
    private string? _username;
    private bool _highContrast;
    private bool _largeText;
    private bool _compactRows;
    private bool _applying;
    private bool _saveError;
    private ResourceDictionary? _contrast;

    public ProfileSettings(string? path = null)
    {
        _path = Path.GetFullPath(path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BD_RGR", "preferences.json"));
    }

    public bool HighContrast { get => _highContrast; set { if (Set(ref _highContrast, value)) Changed(); } }
    public bool LargeText { get => _largeText; set { if (Set(ref _largeText, value)) Changed(); } }
    public bool CompactRows { get => _compactRows; set { if (Set(ref _compactRows, value)) Changed(); } }
    public bool SaveError { get => _saveError; private set => Set(ref _saveError, value); }

    public void Initialize(string defaultLanguage)
    {
        _defaultLanguage = defaultLanguage;
        try
        {
            _profile = ReadProfile();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            SaveError = true;
        }

        Translation.Current.LanguageChanged += OnLanguageChanged;
        Activate(null);
    }

    public void Activate(string? username)
    {
        _username = username;
        var preferences = username is not null && _profile.Accounts.TryGetValue(username, out var account)
            ? account : _profile.Guest;
        _applying = true;
        try
        {
            HighContrast = preferences.HighContrast;
            LargeText = preferences.LargeText;
            CompactRows = preferences.CompactRows;
            Translation.Current.Language = preferences.Language is "ru" or "en" ? preferences.Language : _defaultLanguage;
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
        var updated = new Preferences(Translation.Current.Language, HighContrast, LargeText, CompactRows);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_path.ToUpperInvariant())));
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

            // Merge with the last saved profile so two clients do not overwrite each other's account settings.
            try { _profile = ReadProfile(); }
            catch (JsonException) { _profile = new(); }
            if (_username is null)
                _profile.Guest = updated;
            else
                _profile.Accounts[_username] = updated;

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(_profile, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, overwrite: true);
            SaveError = false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SaveError = true;
            try { File.Delete(temporary); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
        }
        finally
        {
            if (locked)
                gate.ReleaseMutex();
        }
    }

    private ProfileFile ReadProfile()
    {
        var result = File.Exists(_path) ? JsonSerializer.Deserialize<ProfileFile>(File.ReadAllText(_path)) ?? new() : new();
        result.Guest ??= new();
        result.Accounts ??= new(StringComparer.Ordinal);
        return result;
    }

    private void ApplyAppearance()
    {
        if (Application.Current is not { } app)
            return;

        var dictionaries = app.Resources.MergedDictionaries;
        if (HighContrast)
        {
            _contrast ??= new ResourceDictionary { Source = new Uri("/BD_RGR;component/Styles/Contrast.xaml", UriKind.Relative) };
            if (!dictionaries.Contains(_contrast))
                dictionaries.Add(_contrast);
        }
        else if (_contrast is not null)
            dictionaries.Remove(_contrast);

        app.Resources["UiFontSize"] = LargeText ? 17d : 14d;
        app.Resources["UiLabelSize"] = LargeText ? 16d : 13d;
        app.Resources["TableRowHeight"] = CompactRows ? LargeText ? 34d : 30d : LargeText ? 48d : 42d;
    }

    public void Dispose() => Translation.Current.LanguageChanged -= OnLanguageChanged;

    private sealed class ProfileFile
    {
        public Preferences Guest { get; set; } = new();
        public Dictionary<string, Preferences> Accounts { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed record Preferences(string? Language = null, bool HighContrast = false,
        bool LargeText = false, bool CompactRows = false);
}
