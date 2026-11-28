using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using BD_RGR.Models;
using BD_RGR.Services;
using BD_RGR.ViewModels;

namespace BD_RGR.Views;

public sealed class DashboardGrid : DataGrid
{
    public static readonly DependencyProperty TableProperty = DependencyProperty.Register(nameof(Table),
        typeof(DashboardTable), typeof(DashboardGrid), new PropertyMetadata(null, (source, _) => ((DashboardGrid)source).Build()));

    public DashboardTable? Table { get => (DashboardTable?)GetValue(TableProperty); set => SetValue(TableProperty, value); }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == LanguageProperty && Table is not null)
            Build();
    }

    private void Build()
    {
        Columns.Clear();
        if (Table is not { } table)
        {
            ItemsSource = null;
            return;
        }

        ItemsSource = table.Rows.Select(row => row.Select(Format).ToArray()).ToArray();

        for (var index = 0; index < table.Columns.Length; index++)
        {
            Columns.Add(new DataGridTextColumn
            {
                Header = DashboardFormatting.Label(table.Columns[index]),
                Binding = new Binding($"[{index}]"), MinWidth = 72,
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
        }
    }

    private static string Format(object? cell) => cell switch
    {
        null => "—",
        int or long or decimal => DashboardFormatting.Number(Convert.ToDecimal(cell, CultureInfo.InvariantCulture)),
        DashboardLabel label => DashboardFormatting.Label(label),
        DateOnly date => date.ToString("d", Translation.Current.Culture),
        _ => cell.ToString() ?? ""
    };
}
