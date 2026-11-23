using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BD_RGR.Models;
using BD_RGR.Services;

namespace BD_RGR.Views;

public sealed class AnalyticsChart : FrameworkElement
{
    public static readonly DependencyProperty SnapshotProperty = Register<ChartSnapshot>(nameof(Snapshot));
    public static readonly DependencyProperty ChartTitleProperty = Register(nameof(ChartTitle), "");
    public static readonly DependencyProperty PeriodProperty = Register(nameof(Period), "");
    public static readonly DependencyProperty PaperProperty = Register<Brush>(nameof(Paper), Brushes.White);
    public static readonly DependencyProperty InkProperty = Register<Brush>(nameof(Ink), Brushes.Black);
    public static readonly DependencyProperty GridLineProperty = Register<Brush>(nameof(GridLine), Brushes.LightGray);
    public static readonly DependencyProperty BarsProperty = Register<Brush>(nameof(Bars), Brushes.SeaGreen);
    public static readonly DependencyProperty MarkerProperty = Register<Brush>(nameof(Marker), Brushes.DarkGreen);
    public static readonly DependencyProperty TextSizeProperty = Register(nameof(TextSize), 14d);

    public ChartSnapshot? Snapshot { get => (ChartSnapshot?)GetValue(SnapshotProperty); set => SetValue(SnapshotProperty, value); }
    public string ChartTitle { get => (string)GetValue(ChartTitleProperty); set => SetValue(ChartTitleProperty, value); }
    public string Period { get => (string)GetValue(PeriodProperty); set => SetValue(PeriodProperty, value); }
    public Brush Paper { get => (Brush)GetValue(PaperProperty); set => SetValue(PaperProperty, value); }
    public Brush Ink { get => (Brush)GetValue(InkProperty); set => SetValue(InkProperty, value); }
    public Brush GridLine { get => (Brush)GetValue(GridLineProperty); set => SetValue(GridLineProperty, value); }
    public Brush Bars { get => (Brush)GetValue(BarsProperty); set => SetValue(BarsProperty, value); }
    public Brush Marker { get => (Brush)GetValue(MarkerProperty); set => SetValue(MarkerProperty, value); }
    public double TextSize { get => (double)GetValue(TextSizeProperty); set => SetValue(TextSizeProperty, value); }

    protected override void OnRender(DrawingContext drawing) => Draw(drawing, RenderSize, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    public void SavePng(Stream target)
    {
        var size = RenderSize;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            Draw(drawing, size, 1);
        var image = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(size.Width)),
            Math.Max(1, (int)Math.Ceiling(size.Height)), 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(target);
    }

    private void Draw(DrawingContext drawing, Size size, double pixelsPerDip)
    {
        drawing.DrawRectangle(Paper, null, new Rect(size));
        if (Snapshot is not { } snapshot || size.Width < 225 || size.Height < 140)
            return;
        var culture = Translation.Current.Culture;
        var axisSize = TextSize - 3;
        var title = Label(ChartTitle, TextSize + 5, true);
        title.MaxTextWidth = size.Width - 32;
        drawing.DrawText(title, new Point(16, 12));
        var period = Label(Period, TextSize - 1, false);
        var periodY = 17 + title.Height;
        drawing.DrawText(period, new Point(16, periodY));
        var top = periodY + period.Height + axisSize + 14;
        var result = snapshot.Result;
        if (result.Points.Length == 0)
        {
            drawing.DrawText(Label(Translation.Current["AnalyticsEmpty"], TextSize, false), new Point(16, top));
            return;
        }

        var maximum = Math.Max(1, result.Points.Max(p => p.Value));
        var left = Math.Max(45, Label(maximum.ToString("N0", culture), axisSize, false).Width + 15);
        var right = size.Width - 16;
        var bottom = size.Height - axisSize * 2 - 16;
        var plotHeight = bottom - top;
        if (plotHeight < 30 || right - left < 115)
            return;
        var line = new Pen(GridLine, 1);
        for (var i = 0; i <= 4; i++)
        {
            var y = bottom - plotHeight * i / 4;
            drawing.DrawLine(line, new Point(left, y), new Point(right, y));
            var value = Label(((double)maximum * i / 4).ToString(maximum < 4 ? "0.##" : "N0", culture), axisSize, false);
            drawing.DrawText(value, new Point(left - value.Width - 7, y - value.Height / 2));
        }

        var days = result.To.DayNumber - result.From.DayNumber + 1;
        var step = (right - left) / days;
        var barWidth = Math.Max(1, Math.Min(54, step * .72));
        foreach (var point in result.Points)
        {
            var x = left + (point.Date.DayNumber - result.From.DayNumber + .5) * step;
            var height = plotHeight * point.Value / maximum;
            drawing.DrawRectangle(Bars, null, new Rect(x - barWidth / 2, bottom - height, barWidth, height));
            if (step >= 12)
                drawing.DrawEllipse(Marker, null, new Point(x, bottom - height), 3, 3);
            var value = Label(point.Value.ToString("N0", culture), axisSize, false);
            if (step >= value.Width + 5)
            {
                drawing.DrawText(value, new Point(x - value.Width / 2, bottom - height - value.Height - 5));
            }
        }

        var dateWidth = Math.Max(Label(result.From.ToString("d", culture), axisSize, false).Width,
            Label(result.To.ToString("d", culture), axisSize, false).Width);
        var dateLabels = Math.Min(days, Math.Max(1, (int)((right - left) / (dateWidth + 22))));
        for (var n = 0; n < dateLabels; n++)
        {
            var day = dateLabels == 1 ? (days - 1) / 2 : n * (days - 1) / (dateLabels - 1);
            var x = left + (day + .5) * step;
            var date = Label(result.From.AddDays(day).ToString("d", culture), axisSize, false);
            drawing.DrawText(date, new Point(Math.Clamp(x - date.Width / 2, left, Math.Max(left, right - date.Width)), bottom + 8));
        }

        FormattedText Label(string value, double fontSize, bool bold) =>
            new(value, culture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"),
                FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
                fontSize, Ink, pixelsPerDip);
    }

    private static DependencyProperty Register<T>(string name, T? value = default) =>
        DependencyProperty.Register(name, typeof(T), typeof(AnalyticsChart),
            new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsRender));
}
