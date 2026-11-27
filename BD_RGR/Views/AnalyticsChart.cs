using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BD_RGR.Models;
using BD_RGR.Services;
using BD_RGR.ViewModels;

namespace BD_RGR.Views;

public sealed class DashboardDrillEventArgs(string filter, string choice) : EventArgs
{
    public string Filter { get; } = filter;
    public string Choice { get; } = choice;
}

public sealed class AnalyticsChart : FrameworkElement
{
    public static readonly DependencyProperty PlotProperty = Register<DashboardPlot?>(nameof(Plot), null);
    public static readonly DependencyProperty ScopeProperty = Register(nameof(Scope), "");
    public static readonly DependencyProperty PaperProperty = Register<Brush>(nameof(Paper), Brushes.White);
    public static readonly DependencyProperty InkProperty = Register<Brush>(nameof(Ink), Brushes.Black);
    public static readonly DependencyProperty GridLineProperty = Register<Brush>(nameof(GridLine), Brushes.LightGray);
    public static readonly DependencyProperty BarsProperty = Register<Brush>(nameof(Bars), Brushes.SeaGreen);
    public static readonly DependencyProperty SecondProperty = Register<Brush>(nameof(Second), Brushes.SteelBlue);
    public static readonly DependencyProperty ThirdProperty = Register<Brush>(nameof(Third), Brushes.DarkOrange);
    public static readonly DependencyProperty FourthProperty = Register<Brush>(nameof(Fourth), Brushes.MediumPurple);
    public static readonly DependencyProperty TextSizeProperty = Register(nameof(TextSize), 14d);
    private readonly List<(Rect Bounds, int Index)> _hits = [];

    public DashboardPlot? Plot { get => (DashboardPlot?)GetValue(PlotProperty); set => SetValue(PlotProperty, value); }
    public string Scope { get => (string)GetValue(ScopeProperty); set => SetValue(ScopeProperty, value); }
    public Brush Paper { get => (Brush)GetValue(PaperProperty); set => SetValue(PaperProperty, value); }
    public Brush Ink { get => (Brush)GetValue(InkProperty); set => SetValue(InkProperty, value); }
    public Brush GridLine { get => (Brush)GetValue(GridLineProperty); set => SetValue(GridLineProperty, value); }
    public Brush Bars { get => (Brush)GetValue(BarsProperty); set => SetValue(BarsProperty, value); }
    public Brush Second { get => (Brush)GetValue(SecondProperty); set => SetValue(SecondProperty, value); }
    public Brush Third { get => (Brush)GetValue(ThirdProperty); set => SetValue(ThirdProperty, value); }
    public Brush Fourth { get => (Brush)GetValue(FourthProperty); set => SetValue(FourthProperty, value); }
    public double TextSize { get => (double)GetValue(TextSizeProperty); set => SetValue(TextSizeProperty, value); }
    public event EventHandler<DashboardDrillEventArgs>? DrillRequested;

    protected override void OnRender(DrawingContext drawing)
    {
        _hits.Clear();
        Draw(drawing, RenderSize, VisualTreeHelper.GetDpi(this).PixelsPerDip, _hits);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == LanguageProperty)
            InvalidateVisual();
        if (e.Property == PlotProperty)
            ToolTip = null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Plot is not { } plot)
            return;
        var index = Hit(e.GetPosition(this));
        Cursor = index >= 0 && plot.Filter is not null && plot.Labels[index].Choice is not null ? Cursors.Hand : null;
        var label = index < 0 ? null : Describe(plot, index);
        if (!Equals(ToolTip, label))
            ToolTip = label;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var index = Hit(e.GetPosition(this));
        if (index >= 0 && Plot is { Filter: { } filter } plot && plot.Labels[index].Choice is { } choice)
            DrillRequested?.Invoke(this, new(filter, choice));
    }

    public void SavePng(Stream target)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(RenderBitmap(96)));
        encoder.Save(target);
    }

    public void SavePdf(Stream target) => ChartPdf.Write(target, RenderBitmap(192));

    private BitmapSource RenderBitmap(double dpi)
    {
        var size = new Size(Math.Max(660, RenderSize.Width), Math.Max(340, RenderSize.Height));
        var scale = dpi / 96;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            Draw(drawing, size, scale, null);
        var image = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale),
            dpi, dpi, PixelFormats.Pbgra32);
        image.Render(visual);
        return image;
    }

    private int Hit(Point point)
    {
        foreach (var hit in _hits)
            if (hit.Bounds.Contains(point))
                return hit.Index;
        return -1;
    }

    private void Draw(DrawingContext drawing, Size size, double dpi, List<(Rect, int)>? hits)
    {
        drawing.DrawRectangle(Paper, null, new Rect(size));
        if (Plot is not { } plot || size.Width < 130 || size.Height < 100)
            return;

        var title = Text(Translation.Current[plot.TitleKey], TextSize + 1, dpi, bold: true, width: size.Width - 20);
        drawing.DrawText(title, new Point(10, 10));
        var unit = Translation.Current[plot.UnitKey];
        var axis = string.IsNullOrEmpty(Scope) ? unit : $"{Scope} · {unit}";
        var caption = Text(axis, TextSize - 2, dpi, width: size.Width - 20);
        var captionY = 15 + title.Height;
        drawing.DrawText(caption, new Point(10, captionY));
        var top = captionY + caption.Height + (plot.Kind == "donut" ? 6 : Math.Max(10, TextSize - 3));
        var bottom = size.Height - 38;
        if (bottom - top < 32)
            return;
        if (!plot.HasValues)
        {
            drawing.DrawText(Text(Translation.Current["AnalyticsEmpty"], TextSize - 1, dpi, width: size.Width - 20),
                new Point(10, top + 10));
            return;
        }
        if (plot.Kind == "donut")
            DrawDonut(drawing, plot, size.Width, top, bottom + 24, dpi, hits);
        else
            DrawAxes(drawing, plot, size.Width, top, bottom, dpi, hits);
    }

    private void DrawAxes(DrawingContext drawing, DashboardPlot plot, double width, double top, double bottom, double dpi,
        List<(Rect, int)>? hits)
    {
        var numbers = plot.Series.SelectMany(series => series.Values).OfType<decimal>().ToArray();
        var minimum = plot.TitleKey == "DashboardOdometer" ? numbers.Min() : Math.Min(0, numbers.Min());
        var peak = plot.Kind == "stack"
            ? Enumerable.Range(0, plot.Labels.Length).Max(index => plot.Series.Sum(series => series.Values[index] ?? 0))
            : numbers.Max();
        var wholeNumbers = plot.UnitKey == "DashboardCountUnit" ||
            (plot.UnitKey == "DashboardPeopleUnit" && plot.TitleKey != "DashboardAveragePassengers");
        var range = peak - minimum;
        var tick = wholeNumbers ? Math.Max(1, Math.Ceiling(range / 4)) : Math.Max(.001m, range / 4);
        var maximum = minimum + tick * 4;
        var axisSize = Math.Max(10, TextSize - 3);
        var left = Math.Max(38, new[] { minimum, maximum }.Max(value => Text(DashboardFormatting.Number(value), axisSize, dpi).Width) + 12);
        var right = width - 14;
        if (right - left < 50)
            return;
        double Y(decimal value) => bottom - (double)((value - minimum) / (maximum - minimum)) * (bottom - top);
        var area = new Rect(left, top, right - left, bottom - top);
        var pen = new Pen(GridLine, 1);
        for (var i = 0; i <= 4; i++)
        {
            var value = minimum + (maximum - minimum) * i / 4;
            var y = Y(value);
            drawing.DrawLine(pen, new Point(left, y), new Point(right, y));
            var label = Text(DashboardFormatting.Number(value), axisSize, dpi);
            drawing.DrawText(label, new Point(left - label.Width - 7, y - label.Height / 2));
        }

        var count = plot.Labels.Length;
        var spacing = area.Width / count;
        var axisLabelWidth = Math.Max(55, TextSize * 4);
        var stride = Math.Max(1, (int)Math.Ceiling(axisLabelWidth / spacing));
        var barWidth = Math.Min(46, spacing * .72);
        var seriesSize = plot.Kind == "stack" ? barWidth : barWidth / plot.Series.Length;
        var previous = new Point?[plot.Series.Length];

        for (var i = 0; i < count; i++)
        {
            var x = left + (i + .5) * spacing;
            var stack = 0m;
            for (var s = 0; s < plot.Series.Length; s++)
            {
                if (plot.Series[s].Values[i] is not { } value)
                {
                    previous[s] = null;
                    continue;
                }
                var brush = Palette(s);
                if (plot.Kind == "line")
                {
                    var point = new Point(x, Y(value));
                    if (previous[s] is { } start)
                        drawing.DrawLine(new Pen(brush, 2), start, point);
                    if (spacing > 6 || count == 1)
                        drawing.DrawEllipse(brush, null, point, 2.5, 2.5);
                    previous[s] = point;
                    continue;
                }
                var baseValue = plot.Kind == "stack" ? stack : 0;
                var bound = new Rect(x - barWidth / 2 + (plot.Kind == "stack" ? 0 : s * seriesSize),
                    Math.Min(Y(baseValue), Y(baseValue + value)), Math.Max(1, seriesSize),
                    Math.Max(1, Math.Abs(Y(baseValue) - Y(baseValue + value))));
                drawing.DrawRectangle(brush, null, bound);
                stack += value;
            }
            hits?.Add((new Rect(x - spacing / 2, top, spacing, bottom - top + 24), i));
            if (i % stride == 0)
            {
                var label = Text(DashboardFormatting.Label(plot.Labels[i], shortDate: true), axisSize, dpi,
                    width: Math.Min(stride * spacing - 2, right - left));
                drawing.DrawText(label, new Point(Math.Clamp(x - label.Width / 2, left, Math.Max(left, right - label.Width)), bottom + 7));
            }
        }
        if (plot.Series.Length > 1)
            DrawLegend(drawing, plot, width - 20, bottom + 23, dpi);
    }

    private void DrawLegend(DrawingContext drawing, DashboardPlot plot, double width, double y, double dpi)
    {
        var columnWidth = width / plot.Series.Length;
        for (var i = 0; i < plot.Series.Length; i++)
        {
            var x = 10 + columnWidth * i;
            drawing.DrawRectangle(Palette(i), null, new Rect(x, y + 3, 9, 9));
            var title = Translation.Current[plot.Series[i].TitleKey!];
            drawing.DrawText(Text(title, TextSize - 3, dpi, width: columnWidth - 14), new Point(x + 13, y));
        }
    }

    private void DrawDonut(DrawingContext drawing, DashboardPlot plot, double width, double top, double bottom, double dpi,
        List<(Rect, int)>? hits)
    {
        var values = plot.Series[0].Values;
        var total = values.Sum() ?? 0;
        if (total <= 0)
        {
            drawing.DrawText(Text(Translation.Current["AnalyticsEmpty"], TextSize - 1, dpi, width: width - 20),
                new Point(10, top + 10));
            return;
        }
        var radius = Math.Min(width * .16, (bottom - top) * .34);
        var center = new Point(12 + radius * 1.25, (top + bottom) / 2);
        var angle = -Math.PI / 2;
        Point At(double a) => new(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a));
        var y = top + 5;
        var legendX = center.X + radius * 1.32 + 12;
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i] ?? 0;
            var sweep = 2 * Math.PI * (double)(value / total);
            var brush = Palette(i);
            var stroke = new Pen(brush, radius * .40);
            if (sweep >= 2 * Math.PI - .00001)
                drawing.DrawEllipse(null, stroke, center, radius, radius);
            else if (sweep > 0)
            {
                var arc = new StreamGeometry();
                using (var geometry = arc.Open())
                {
                    geometry.BeginFigure(At(angle), false, false);
                    geometry.ArcTo(At(angle + sweep), new Size(radius, radius), 0, sweep > Math.PI,
                        SweepDirection.Clockwise, true, false);
                }
                drawing.DrawGeometry(null, stroke, arc);
            }
            angle += sweep;
            if (y + TextSize + 4 > bottom)
                continue;
            drawing.DrawRectangle(brush, null, new Rect(legendX, y + 3, 10, 10));
            drawing.DrawText(Text(Describe(plot, i), TextSize - 2, dpi, width: width - legendX - 25), new Point(legendX + 16, y));
            hits?.Add((new Rect(legendX, y, Math.Max(1, width - legendX - 10), TextSize + 4), i));
            y += TextSize + 4;
        }
    }

    private static string Describe(DashboardPlot plot, int index)
    {
        var label = DashboardFormatting.Label(plot.Labels[index]);
        var values = plot.Series.Select(series =>
        {
            var value = series.Values[index] is { } number ? DashboardFormatting.Number(number) : "—";
            return series.TitleKey is { } key ? $"{Translation.Current[key]} {value}" : value;
        });
        return $"{label} · {string.Join(" / ", values)}";
    }

    private Brush Palette(int index) => (index % 4) switch { 1 => Second, 2 => Third, 3 => Fourth, _ => Bars };

    private FormattedText Text(string value, double font, double dpi, bool bold = false, double? width = null)
    {
        var text = new FormattedText(value, Translation.Current.Culture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal,
                FontStretches.Normal), font, Ink, dpi);
        if (width is { } maximum)
        {
            text.MaxTextWidth = Math.Max(1, maximum);
            text.MaxLineCount = bold ? 2 : 1;
            text.Trimming = TextTrimming.CharacterEllipsis;
        }
        return text;
    }

    private static DependencyProperty Register<T>(string name, T value) =>
        DependencyProperty.Register(name, typeof(T), typeof(AnalyticsChart),
            new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsRender));
}
