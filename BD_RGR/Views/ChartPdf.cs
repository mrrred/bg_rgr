using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BD_RGR.Views;

internal static class ChartPdf
{
    public static void Write(Stream target, BitmapSource chart)
    {
        var rgb = new FormatConvertedBitmap(chart, PixelFormats.Rgb24, null, 0);
        var stride = rgb.PixelWidth * 3;
        var pixels = new byte[stride * rgb.PixelHeight];
        rgb.CopyPixels(pixels, stride, 0);
        using var image = new MemoryStream();
        using (var compressor = new ZLibStream(image, CompressionLevel.Optimal, leaveOpen: true))
            compressor.Write(pixels);

        // Fit the chart proportionally on a landscape A4 page, in points.
        const int pageWidth = 842;
        const int pageHeight = 595;
        const int margin = 24;
        var scale = Math.Min((double)(pageWidth - 2 * margin) / rgb.PixelWidth,
            (double)(pageHeight - 2 * margin) / rgb.PixelHeight);
        var width = rgb.PixelWidth * scale;
        var height = rgb.PixelHeight * scale;
        var x = (pageWidth - width) / 2;
        var y = (pageHeight - height) / 2;
        var content = FormattableString.Invariant($"q\n{width:0.###} 0 0 {height:0.###} {x:0.###} {y:0.###} cm\n/Chart Do\nQ\n");
        var offsets = new long[6];

        Print("%PDF-1.4\n%");
        target.Write([0xE2, 0xE3, 0xCF, 0xD3, 0x0A]);
        BeginObject(1);
        Print("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        BeginObject(2);
        Print("<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        BeginObject(3);
        Print($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageWidth} {pageHeight}] " +
              "/Resources << /XObject << /Chart 5 0 R >> >> /Contents 4 0 R >>\nendobj\n");
        BeginObject(4);
        Print($"<< /Length {content.Length} >>\nstream\n{content}endstream\nendobj\n");
        BeginObject(5);
        Print($"<< /Type /XObject /Subtype /Image /Width {rgb.PixelWidth} /Height {rgb.PixelHeight} " +
              $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length {image.Length} >>\nstream\n");
        image.WriteTo(target);
        Print("\nendstream\nendobj\n");

        var xref = target.Position;
        Print($"xref\n0 {offsets.Length}\n0000000000 65535 f\r\n");
        foreach (var offset in offsets.AsSpan(1))
            Print(FormattableString.Invariant($"{offset:D10} 00000 n\r\n"));
        Print($"trailer\n<< /Size {offsets.Length} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");

        void Print(string value) => target.Write(Encoding.ASCII.GetBytes(value));

        void BeginObject(int id)
        {
            offsets[id] = target.Position;
            Print($"{id} 0 obj\n");
        }
    }
}
