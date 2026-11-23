using System.Globalization;
using BD_RGR.Models;
using BD_RGR.Services;

namespace BD_RGR.ViewModels;

public sealed class DisplayRow : ViewModel
{
    private readonly IReadOnlyDictionary<string, BusinessField> _fields;
    public BusinessRow Row { get; }
    public int Id => Row.Id;
    public IReadOnlyDictionary<string, bool> CanFollow { get; }

    public DisplayRow(BusinessRow row, BusinessField[] fields, BusinessResource[] catalog)
    {
        Row = row;
        _fields = fields.ToDictionary(field => field.Name);
        CanFollow = fields.ToDictionary(field => field.Name, field =>
            row.Labels.GetValueOrDefault(field.Name) is not null && (field.Target == "vehicle"
                ? catalog.Any(t => t.IsVehicleSubtype)
                : catalog.Any(t => t.Name == field.Target)));
    }

    public string this[string name]
    {
        get
        {
            if (!_fields.TryGetValue(name, out var field) || !Row.Values.TryGetValue(name, out var value))
                return "";
            if (field.Target is not null)
                return Row.Labels.GetValueOrDefault(name) ?? (value is null ? Translation.Current["Unassigned"] : "");
            return FieldEditor.Format(field, value);
        }
    }

    public void RefreshText() => Notify("Item[]");
}

public sealed class FieldEditor : ViewModel
{
    private readonly Func<string, string?, Task<LookupRow[]>> _lookup;
    private readonly Action<AuthNotice> _error;
    private bool _loading;
    private bool _loaded;
    private string _query = "";
    private string? _text;
    private FieldOption[] _options = [];
    private FieldOption? _selected;
    private string? _value;
    private bool _hasError;

    public FieldEditor(BusinessField field, string? value, string? label, bool editable,
        Func<string, string?, Task<LookupRow[]>> lookup, Action<AuthNotice> error, string? section = null)
    {
        Field = field;
        Editable = editable;
        _lookup = lookup;
        _error = error;
        _value = value;
        _text = editable && field.Kind == "date" ? value : Format(field, value);
        Section = section;
        if (IsReference)
        {
            var selected = value is null ? Array.Empty<FieldOption>() : [new(value, label ?? value)];
            ApplyOptions(selected);
        }
        else if (field.Choices is { } choices)
            ApplyOptions(choices.Select(choice => new FieldOption(choice.Value, TitleKey: choice.TitleKey)));
        else if (field.Kind == "boolean")
            ApplyOptions([new("true", TitleKey: "Yes"), new("false", TitleKey: "No")]);
    }

    public BusinessField Field { get; }
    public bool Editable { get; }
    public bool IsReadOnly => !Editable;
    public string? Section { get; }
    public bool HasSection => Section is not null;
    public bool IsReference => Field.Target is not null;
    public bool IsChoice => IsReference || Field.Choices is not null || Field.Kind == "boolean";
    public bool EditableDate => Editable && Field.Kind == "date";
    public bool IsText => !IsChoice && !EditableDate;
    public bool CanSearch => IsReference && Editable;
    public bool HasError { get => _hasError; set => Set(ref _hasError, value); }
    public string? Value => _value;
    public string Query { get => _query; set => Set(ref _query, value); }
    public string? Text { get => _text; set => Set(ref _text, value); }
    public DateTime? DateValue
    {
        get => DateOnly.TryParseExact(Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToDateTime(TimeOnly.MinValue)
            : null;
        set => Text = value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
    public FieldOption[] Options => _options;

    public FieldOption? Selected
    {
        get => _selected;
        set
        {
            if (value is not null && Set(ref _selected, value))
                _value = value.Value;
        }
    }

    public async Task SearchAsync(bool force = false)
    {
        if (!CanSearch || _loading || _loaded && !force)
            return;
        _loading = true;
        try
        {
            var options = await _lookup(Query, Value);
            ApplyOptions(options.Select(row => new FieldOption(row.Id.ToString(CultureInfo.InvariantCulture), row.Label)));
            _loaded = true;
        }
        catch (AuthApiException error)
        {
            _error(error.Notice);
        }
        finally
        {
            _loading = false;
        }
    }

    public string? StoredValue()
    {
        if (IsChoice)
            return Value;
        var value = Text?.Trim();
        if (string.IsNullOrEmpty(value))
            return null;

        var culture = Translation.Current.Culture;
        if (Field.Kind == "date"
            && (DateOnly.TryParse(value, culture, DateTimeStyles.None, out var date)
                || DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)))
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        const NumberStyles numeric = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        if (Field.Kind == "numeric"
            && (decimal.TryParse(value, numeric, culture, out var number)
                || decimal.TryParse(value.Replace(',', '.'), numeric, CultureInfo.InvariantCulture, out number)))
            return number.ToString(CultureInfo.InvariantCulture);
        return value;
    }

    private void ApplyOptions(IEnumerable<FieldOption> options)
    {
        var value = Value;
        var list = options.ToList();
        if (_selected is { Value: not null } current && !list.Any(option => option.Value == value))
            list.Insert(0, current);
        if (Field.Nullable)
            list.Insert(0, new FieldOption(null, TitleKey: "Unassigned"));
        _options = list.ToArray();
        _selected = _options.FirstOrDefault(option => option.Value == value);
        Notify(nameof(Options));
        Notify(nameof(Selected));
    }

    public void RefreshText()
    {
        foreach (var option in Options)
            option.RefreshText();
        if (!Editable)
            Text = Format(Field, Value);
    }

    internal static string Format(BusinessField field, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        var text = Translation.Current;
        if (field.Choices?.FirstOrDefault(option => option.Value == value) is { } choice)
            return text[choice.TitleKey];
        return field.Kind switch
        {
            "boolean" => text[value == "true" ? "Yes" : "No"],
            "date" when DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                => date.ToString("d", text.Culture),
            "numeric" when decimal.TryParse(value, CultureInfo.InvariantCulture, out var number) => number.ToString(text.Culture),
            _ => value
        };
    }
}

public sealed class FieldOption(string? value, string? label = null, string? TitleKey = null) : ViewModel
{
    public string? Value { get; } = value;
    public string Label => TitleKey is null ? label ?? "" : Translation.Current[TitleKey];
    public void RefreshText() => Notify(nameof(Label));
}
