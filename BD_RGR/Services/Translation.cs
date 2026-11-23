using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Markup;

namespace BD_RGR.Services;

public sealed class Translation : INotifyPropertyChanged
{
    public static Translation Current { get; } = new();

    private readonly Dictionary<string, Dictionary<string, string>> _translations = new()
    {
        ["ru"] = Load("ru"),
        ["en"] = Load("en")
    };

    private string _language = "ru";

    private Translation()
    {
        Languages = [new("ru", this["LanguageRussian"]), new("en", this["LanguageEnglish"])];
    }

    public IReadOnlyList<LanguageOption> Languages { get; }

    public string Language
    {
        get => _language;
        set
        {
            if (value is null || value == _language || !_translations.ContainsKey(value))
                return;

            _language = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public CultureInfo Culture => GetCulture(Language);
    public string this[string key] => _translations[Language][key];
    public string Get(string language, string key) => _translations[language][key];
    public static CultureInfo GetCulture(string language) =>
        CultureInfo.GetCultureInfo(language == "ru" ? "ru-RU" : "en-US");

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    private static Dictionary<string, string> Load(string language)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Localization", language + ".json");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
            ?? throw new InvalidDataException(path);
    }
}

public sealed record LanguageOption(string Code, string Name);

public sealed class LocExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{key}]") { Source = Translation.Current, Mode = BindingMode.OneWay }
            .ProvideValue(serviceProvider);
}

public sealed class TranslateKeyConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values[0] is not string key)
            return "";

        var translation = Translation.Current;
        var message = translation[key];
        return values.Length > 2 && values[2] is string field
            ? string.Format(translation.Culture, message, field)
            : message;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
