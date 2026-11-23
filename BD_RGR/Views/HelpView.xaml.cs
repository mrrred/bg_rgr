using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BD_RGR.Services;

namespace BD_RGR.Views;

public partial class HelpView : UserControl
{
    public HelpView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Translation.Current.LanguageChanged += LanguageChanged;
            LoadIllustrations();
        };
        Unloaded += (_, _) => Translation.Current.LanguageChanged -= LanguageChanged;
    }

    private void LanguageChanged(object? sender, EventArgs e) => LoadIllustrations();

    private void LoadIllustrations()
    {
        var suffix = Translation.Current.Language;
        LoadImage(ReportImage, $"documents-{suffix}.png");
        LoadImage(AnalyticsImage, $"analytics-{suffix}.png");
    }

    private static void LoadImage(Image image, string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Help", file);
        image.Source = File.Exists(path) ? new BitmapImage(new Uri(path)) : null;
        image.Visibility = image.Source is ImageSource ? Visibility.Visible : Visibility.Collapsed;
    }
}
