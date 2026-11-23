using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace BD_RGR.Views;

public sealed class LocalizedDatePicker : DatePicker
{
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == LanguageProperty && SelectedDate is { } date)
        {
            var culture = CultureInfo.GetCultureInfo(Language.IetfLanguageTag);
            SetCurrentValue(TextProperty, date.ToString(SelectedDateFormat == DatePickerFormat.Short ? "d" : "D", culture));
        }
    }
}
