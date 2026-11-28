using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using BD_RGR.Models;
using BD_RGR.Services;
using BD_RGR.ViewModels;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.SKCharts;
using LiveChartsCore.SkiaSharpView.VisualElements;
using LiveChartsCore.SkiaSharpView.WPF;
using SkiaSharp;
using ChartMargin = LiveChartsCore.Measure.Margin;

namespace BD_RGR.Views;

public sealed class DashboardChartView : ContentControl
{
    private sealed record Slice(int Index, double Value);

    public static readonly DependencyProperty PlotProperty = Register<DashboardPlot>(nameof(Plot));
    public static readonly DependencyProperty HeatTableProperty = Register<DashboardTable>(nameof(HeatTable));
    public static readonly DependencyProperty ScopeProperty = Register(nameof(Scope), "");
    private static readonly DependencyProperty[] Accents =
    [
        Register<Brush>("ChartBar"), Register<Brush>("ChartSecond"),
        Register<Brush>("ChartThird"), Register<Brush>("ChartFourth")
    ];
    private static readonly SKTypeface Typeface = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
    private DispatcherOperation? _update;

    public DashboardChartView()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        SetResourceReference(BackgroundProperty, "PaperBrush");
        SetResourceReference(ForegroundProperty, "InkBrush");
        SetResourceReference(BorderBrushProperty, "BorderBrush");
        SetResourceReference(FontSizeProperty, "UiFontSize");
        SetResourceReference(Accents[0], "ChartBarBrush");
        SetResourceReference(Accents[1], "ChartSecondBrush");
        SetResourceReference(Accents[2], "ChartThirdBrush");
        SetResourceReference(Accents[3], "ChartFourthBrush");
        Loaded += (_, _) => ScheduleUpdate();
    }

    public DashboardPlot? Plot { get => (DashboardPlot?)GetValue(PlotProperty); set => SetValue(PlotProperty, value); }
    public DashboardTable? HeatTable { get => (DashboardTable?)GetValue(HeatTableProperty); set => SetValue(HeatTableProperty, value); }
    public string Scope { get => (string)GetValue(ScopeProperty); set => SetValue(ScopeProperty, value); }
    public event Action<string, string>? DrillRequested;

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == LanguageProperty || e.Property == BackgroundProperty || e.Property == ForegroundProperty ||
            e.Property == BorderBrushProperty || e.Property == FontSizeProperty)
            ScheduleUpdate();
    }

    private void ScheduleUpdate()
    {
        if (!IsLoaded || _update?.Status == DispatcherOperationStatus.Pending)
            return;
        _update = Dispatcher.InvokeAsync(() =>
        {
            if (IsLoaded)
                BuildChart();
        }, DispatcherPriority.Loaded);
    }

    private void BuildChart()
    {
        if (Plot is { HasValues: true } plot)
        {
            ContentControl control = plot.Kind == "donut" ? new PieChart() : new CartesianChart();
            control.Background = Background;
            var chart = (IChartView)control;
            ConfigurePlot(chart, plot);
            chart.DataPointerDown += PointClicked;
            Content = control;
        }
        else if (HeatTable is { IsEmpty: false } heat)
        {
            Content = BuildHeatChart(heat);
        }
        else
        {
            var empty = new StackPanel { Margin = new Thickness(8) };
            if ((Plot?.TitleKey ?? HeatTable?.TitleKey) is { } title)
                empty.Children.Add(new TextBlock { Text = Translation.Current[title], FontWeight = FontWeights.SemiBold });
            empty.Children.Add(new TextBlock { Text = Translation.Current["AnalyticsEmpty"], TextWrapping = TextWrapping.Wrap });
            Content = empty;
        }
    }

    private void PointClicked(IChartView chart, IEnumerable<ChartPoint> points)
    {
        if (Plot is not { Filter: { } filter } plot || points.FirstOrDefault() is not { } point)
            return;
        var index = point.Context.DataSource is Slice slice ? slice.Index : point.Index;
        if ((uint)index < (uint)plot.Labels.Length && plot.Labels[index].Choice is { } choice)
            DrillRequested?.Invoke(filter, choice);
    }

    public void Export(Stream target, bool pdf)
    {
        if (Plot is not { HasValues: true } plot)
            return;
        var width = Math.Max(660, (int)Math.Ceiling(ActualWidth));
        var height = Math.Max(340, (int)Math.Ceiling(ActualHeight));
        InMemorySkiaSharpChart imageChart;
        if (plot.Kind == "donut")
            imageChart = new SKPieChart { Width = width, Height = height, Background = Paper };
        else
            imageChart = new SKCartesianChart { Width = width, Height = height, Background = Paper };
        ConfigurePlot((IChartView)imageChart, plot);
        using var image = imageChart.GetImage();
        if (pdf)
            ChartPdf.Write(target, image);
        else
        {
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            png.SaveTo(target);
        }
    }

    private void ConfigurePlot(IChartView chart, DashboardPlot plot)
    {
        var text = Translation.Current;
        var unit = text[plot.UnitKey];
        Configure(chart, text[plot.TitleKey], string.IsNullOrEmpty(Scope) ? unit : $"{Scope} · {unit}",
            plot.Kind == "donut" || plot.Series.Length > 1);
        if (chart is ICartesianChartView cartesian)
        {
            var labels = plot.Labels.Select(label => DashboardFormatting.Label(label, shortDate: true)).ToArray();
            var axis = CategoryAxis(labels.Select(label => Shorten(label, 15)).ToArray());
            var longLabels = labels.Any(label => label.Length > 10);
            axis.LabelsRotation = labels.Length > 8 || (labels.Length >= 4 && longLabels) ? -35 : 0;
            if (axis.LabelsRotation != 0 && longLabels)
                chart.DrawMargin = new ChartMargin((float)(LabelSize * 6.5), ChartMargin.Auto, ChartMargin.Auto, ChartMargin.Auto);
            axis.LabelsDensity = 1.4f;
            cartesian.FindingStrategy = FindingStrategy.CompareAllTakeClosest;
            cartesian.XAxes = [axis];
            cartesian.YAxes =
            [
                new Axis
                {
                    TextSize = LabelSize, LabelsPaint = TextPaint(), SeparatorsPaint = new SolidColorPaint(GridColor),
                    Labeler = value => DashboardFormatting.Number((decimal)value)
                }
            ];
            chart.Series = plot.Series.Select((series, index) => CartesianSeries(plot, series, index, labels, unit)).ToArray();
        }
        else
        {
            // Group all slices at X = 0 and retain the original label index for click-through.
            chart.Series = plot.Labels.Select((label, index) => (label, index, value: plot.Series[0].Values[index] ?? 0))
                .Where(slice => slice.value > 0)
                .Select(slice =>
                {
                    var label = DashboardFormatting.Label(slice.label);
                    var value = DashboardFormatting.Number(slice.value);
                    return new PieSeries<Slice>
                    {
                        Name = $"{Shorten(label, 20)} · {value}",
                        Values = [new Slice(slice.index, (double)slice.value)],
                        Mapping = (point, _) => new Coordinate(0, point.Value),
                        Fill = new SolidColorPaint(Accent(slice.index)),
                        InnerRadius = 42,
                        HoverPushout = 4,
                        ToolTipLabelFormatter = _ => $"{label} · {value} {unit}"
                    };
                }).ToArray();
        }
    }

    private ISeries CartesianSeries(DashboardPlot plot, DashboardSeries series, int index, string[] labels, string unit)
    {
        var accent = Accent(index);
        var name = series.TitleKey is { } key ? Translation.Current[key] : unit;
        var values = series.Values.Select(value => (double?)value).ToArray();
        string Caption(ChartPoint point) => $"{name} · {DashboardFormatting.Number((decimal)point.Coordinate.PrimaryValue)}";
        string Category(ChartPoint point) => (uint)point.Index < (uint)labels.Length ? labels[point.Index] : "";
        return plot.Kind switch
        {
            "line" => new LineSeries<double?>
            {
                Name = name, Values = values, LineSmoothness = 0, Fill = null, Stroke = new SolidColorPaint(accent, 2),
                GeometrySize = labels.Length <= 45 ? 6 : 0, GeometryFill = new SolidColorPaint(accent), GeometryStroke = null,
                EnableNullSplitting = true, XToolTipLabelFormatter = Category, YToolTipLabelFormatter = Caption
            },
            "stack" => new StackedColumnSeries<double?>
            {
                Name = name, Values = values, Fill = new SolidColorPaint(accent), MaxBarWidth = 45,
                XToolTipLabelFormatter = Category, YToolTipLabelFormatter = Caption
            },
            _ => new ColumnSeries<double?>
            {
                Name = name, Values = values, Fill = new SolidColorPaint(accent), MaxBarWidth = 45,
                XToolTipLabelFormatter = Category, YToolTipLabelFormatter = Caption
            }
        };
    }

    private FrameworkElement BuildHeatChart(DashboardTable table)
    {
        var chart = new CartesianChart
        {
            Background = Background,
            Height = Math.Max(200, 95 + table.Rows.Length * Math.Max(26, FontSize + 10))
        };
        Configure(chart, Translation.Current[table.TitleKey], "", false);

        var dayCount = table.Columns.Length - 1;
        var peak = Math.Max(1, table.Rows.SelectMany(row => row.Skip(1)).Max(value => Convert.ToDouble(value)));
        var scroll = new ScrollBar { Orientation = Orientation.Horizontal, SmallChange = 1, Visibility = Visibility.Collapsed };
        var panel = new DockPanel();
        DockPanel.SetDock(scroll, Dock.Bottom);
        panel.Children.Add(scroll);
        panel.Children.Add(new ScrollViewer
        {
            Content = chart,
            Background = Background,
            MaxHeight = 360,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        var visibleDays = 0;
        void Refresh() => ConfigureHeat(chart, table, (int)scroll.Value, visibleDays, peak);

        scroll.ValueChanged += (_, _) => Refresh();
        chart.SizeChanged += (_, e) =>
        {
            var fittingDays = (int)((e.NewSize.Width - 105) / Math.Max(39, FontSize * 3));
            var count = Math.Clamp(fittingDays, 1, dayCount);
            if (count == visibleDays)
                return;
            visibleDays = count;
            scroll.ViewportSize = scroll.LargeChange = count;
            scroll.Maximum = dayCount - count;
            scroll.Visibility = scroll.Maximum > 0 ? Visibility.Visible : Visibility.Collapsed;
            Refresh();
        };
        return panel;
    }

    private void ConfigureHeat(CartesianChart chart, DashboardTable table, int start, int count, double peak)
    {
        var days = table.Columns.Skip(start + 1).Take(count)
            .Select(label => DashboardFormatting.Label(label, shortDate: true)).ToArray();
        var fullDates = table.Columns.Skip(start + 1).Take(count).Select(label => DashboardFormatting.Label(label)).ToArray();
        var trucks = table.Rows.Select(row => row[0]?.ToString() ?? "").ToArray();
        var points = table.Rows.SelectMany((row, y) => row.Skip(start + 1).Take(count).Select((value, x) =>
            new WeightedPoint(x, y, Convert.ToDouble(value)))).ToArray();
        chart.XAxes = [CategoryAxis(days, true)];
        var truckAxis = CategoryAxis(trucks, true);
        truckAxis.IsInverted = true;
        chart.YAxes = [truckAxis];
        chart.Series =
        [
            new HeatSeries<WeightedPoint>
            {
                Values = points,
                MinValue = 0,
                MaxValue = peak,
                HeatMap = [Color(Blend(Paper, Accent(0), .04f)), Color(Blend(Paper, Accent(0), .46f))],
                PointPadding = new LiveChartsCore.Drawing.Padding(2),
                DataLabelsPaint = TextPaint(),
                DataLabelsSize = LabelSize,
                DataLabelsFormatter = point => DashboardFormatting.Number((decimal)point.Coordinate.TertiaryValue),
                XToolTipLabelFormatter = point => fullDates[(int)point.Coordinate.SecondaryValue],
                YToolTipLabelFormatter = point => $"{trucks[(int)point.Coordinate.PrimaryValue]} · {DashboardFormatting.Number((decimal)point.Coordinate.TertiaryValue)}"
            }
        ];
    }

    private void Configure(IChartView chart, string title, string subtitle, bool legend)
    {
        chart.AnimationsSpeed = TimeSpan.Zero;
        chart.LegendPosition = legend ? LegendPosition.Bottom : LegendPosition.Hidden;
        chart.LegendTextSize = LabelSize;
        chart.LegendTextPaint = TextPaint();
        chart.TooltipTextSize = LabelSize;
        chart.TooltipTextPaint = TextPaint();
        chart.TooltipBackgroundPaint = new SolidColorPaint(Paper);
        chart.Title = new DrawnLabelVisual
        {
            Label =
            {
                Text = string.IsNullOrEmpty(subtitle) ? title : $"{title}\n{subtitle}",
                TextSize = (float)FontSize, Paint = TextPaint(),
                Padding = new LiveChartsCore.Drawing.Padding(8)
            }
        };
    }

    private Axis CategoryAxis(string[] labels, bool forceStep = false) => new()
    {
        Labels = labels, TextSize = LabelSize, LabelsPaint = TextPaint(),
        MinStep = 1, ForceStepToMin = forceStep, ShowSeparatorLines = false
    };

    private double LabelSize => Math.Max(11, FontSize - 2);
    private SKColor Paper => BrushColor(Background, SKColors.White);
    private SKColor GridColor => BrushColor(BorderBrush, SKColors.LightGray);
    private SKColor Accent(int index) => BrushColor(GetValue(Accents[index % Accents.Length]) as Brush, SKColors.SeaGreen);
    private SolidColorPaint TextPaint() => new(BrushColor(Foreground, SKColors.Black)) { SKTypeface = Typeface };
    private static SKColor BrushColor(Brush? brush, SKColor fallback) =>
        brush is SolidColorBrush solid ? new SKColor(solid.Color.R, solid.Color.G, solid.Color.B, solid.Color.A) : fallback;
    private static LvcColor Color(SKColor color) => new(color.Red, color.Green, color.Blue, color.Alpha);
    private static SKColor Blend(SKColor background, SKColor accent, float fraction) => new(
        (byte)(background.Red + (accent.Red - background.Red) * fraction),
        (byte)(background.Green + (accent.Green - background.Green) * fraction),
        (byte)(background.Blue + (accent.Blue - background.Blue) * fraction));
    private static string Shorten(string value, int length) => value.Length <= length ? value : value[..(length - 1)] + "…";

    private static DependencyProperty Register<T>(string name, T? initial = default) =>
        DependencyProperty.Register(name, typeof(T), typeof(DashboardChartView),
            new PropertyMetadata(initial, (sender, _) => ((DashboardChartView)sender).ScheduleUpdate()));
}
