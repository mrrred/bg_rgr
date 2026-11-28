using System.IO;
using SkiaSharp;

namespace BD_RGR.Views;

internal static class ChartPdf
{
    public static void Write(Stream target, SKImage image)
    {
        const float width = 842;
        const float height = 595;
        const float margin = 24;
        var scale = Math.Min((width - 2 * margin) / image.Width, (height - 2 * margin) / image.Height);
        var left = (width - image.Width * scale) / 2;
        var top = (height - image.Height * scale) / 2;
        var bounds = SKRect.Create(left, top, image.Width * scale, image.Height * scale);

        using var document = SKDocument.CreatePdf(target);
        var page = document.BeginPage(width, height);
        page.DrawImage(image, bounds, new SKSamplingOptions(SKFilterMode.Linear));
        document.EndPage();
        document.Close();
    }
}
