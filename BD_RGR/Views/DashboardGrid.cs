using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Shapes;
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

        var peak = table.Heat ? table.Rows.SelectMany(row => row.Skip(1)).Select(Number)
            .DefaultIfEmpty(1).Max() : 0;
        ItemsSource = table.Rows.Select(row => new DashboardCellRow(row.Select((cell, index) =>
            new DashboardCell(Format(cell), table.Heat && index > 0 && peak > 0
                ? (double)(Number(cell) / peak) * .38 : 0)).ToArray())).ToArray();

        for (var index = 0; index < table.Columns.Length; index++)
        {
            var header = DashboardFormatting.Label(table.Columns[index], table.Heat);
            var binding = new Binding($"[{index}].Text");
            Columns.Add(table.Heat && index > 0
                ? HeatColumn(index, header, binding)
                : new DataGridTextColumn
                {
                    Header = header, Binding = binding, MinWidth = table.Heat ? 100 : 72,
                    Width = table.Heat ? DataGridLength.Auto : new DataGridLength(1, DataGridLengthUnitType.Star)
                });
        }
    }

    private static DataGridTemplateColumn HeatColumn(int index, string header, Binding text)
    {
        var root = new FrameworkElementFactory(typeof(Grid));
        var shade = new FrameworkElementFactory(typeof(Rectangle));
        shade.SetResourceReference(Shape.FillProperty, "ChartBarBrush");
        shade.SetBinding(OpacityProperty, new Binding($"[{index}].Intensity"));
        root.AppendChild(shade);

        var caption = new FrameworkElementFactory(typeof(TextBlock));
        caption.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        caption.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        caption.SetBinding(TextBlock.TextProperty, text);
        root.AppendChild(caption);
        return new DataGridTemplateColumn
        {
            Header = header, Width = new DataGridLength(68),
            CellTemplate = new DataTemplate { VisualTree = root }
        };
    }

    private static decimal Number(object? value) => value is IConvertible number
        ? number.ToDecimal(CultureInfo.InvariantCulture) : 0;

    private static string Format(object? cell) => cell switch
    {
        null => "—",
        int or long or decimal => DashboardFormatting.Number(Number(cell)),
        DashboardLabel label => DashboardFormatting.Label(label),
        DateOnly date => date.ToString("d", Translation.Current.Culture),
        _ => cell.ToString() ?? ""
    };
}

public sealed record DashboardCell(string Text, double Intensity);

public sealed class DashboardCellRow(DashboardCell[] cells)
{
    public DashboardCell this[int index] => cells[index];
}
