using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace BD_RGR.Views;

public partial class HelpView : UserControl
{
    private static readonly GuideTopic[] Topics =
    [
        new("GuideGettingStarted",
            new GuideSection("GuideSignIn", "signin"),
            new("GuideRegistration", "registration"),
            new("GuideRoles", "roles"),
            new("GuidePassword", "password")),
        new("GuideNavigation",
            new GuideSection("GuideSidebar", "navigation"),
            new("GuideReturning"),
            new("GuideKeyboard"),
            new("GuideAbout")),
        new("GuideTables",
            new GuideSection("GuideSearch", "table"),
            new("GuideCard"),
            new("GuideLookup", "related-field"),
            new("GuideDatePicker"),
            new("GuideOpenRelated"),
            new("GuideDelete", "delete-confirmation")),
        new("GuideVehicles",
            new GuideSection("GuideVehiclePassport", "vehicle"),
            new("GuideVehicleStatus"),
            new("GuideRepairs", "repair"),
            new("GuideMileageLog"),
            new("GuideMileageCalculation", "mileage")),
        new("GuidePassenger",
            new GuideSection("GuideRoutes", "passenger"),
            new("GuideIntermediatePoints"),
            new("GuideRouteAssignment"),
            new("GuideTrips")),
        new("GuideFreight",
            new GuideSection("GuideCargoCatalog"),
            new("GuideShipments"),
            new("GuideCargoItems", "freight")),
        new("GuideStaff",
            new GuideSection("GuideEmployee", "staff"),
            new("GuideEmployment"),
            new("GuideCrews"),
            new("GuideStaffAssignments")),
        new("GuideDirectories",
            new GuideSection("GuideDirectoryUse", "directories"),
            new("GuideDirectoryTransport"),
            new("GuideDirectoryTraffic"),
            new("GuideDirectoryPersonnel")),
        new("GuideDocuments",
            new GuideSection("GuideReportTemplates", "documents"),
            new("GuideManualSelect", "manual"),
            new("GuideCsv")),
        new("GuideAnalytics",
            new GuideSection("GuideAnalyticsPeriod", "analytics"),
            new("GuideAnalyticsMetrics"),
            new("GuideAnalyticsExport")),
        new("GuideSettings",
            new GuideSection("GuideInterface", "settings"),
            new("GuideAccessibility"),
            new("GuideDensity"),
            new("GuideReset")),
        new("GuideTroubleshooting",
            new GuideSection("GuideConnectionProblems", "connection-error"),
            new("GuideAccountProblems"),
            new("GuideDataProblems"),
            new("GuideReportProblems"))
    ];

    private Button? _lastTopicButton;

    public HelpView()
    {
        InitializeComponent();
        TopicList.ItemsSource = Topics;
    }

    private void Topic_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: GuideTopic topic } button)
            return;

        _lastTopicButton = button;
        Article.Content = topic;
        Contents.Visibility = Visibility.Collapsed;
        Article.Visibility = Visibility.Visible;
        ContentsButton.Visibility = Visibility.Visible;
        GuideScroll.ScrollToTop();
        ContentsButton.Focus();
    }

    private void Contents_Click(object sender, RoutedEventArgs e)
    {
        Article.Content = null;
        Article.Visibility = Visibility.Collapsed;
        ContentsButton.Visibility = Visibility.Collapsed;
        Contents.Visibility = Visibility.Visible;
        GuideScroll.ScrollToTop();
        _lastTopicButton?.Focus();
    }
}

internal sealed record GuideTopic(string TitleKey, params GuideSection[] Sections);

internal sealed record GuideSection(string Key, string? Screenshot = null)
{
    public string TitleKey => Key + "Title";
    public string TextKey => Key + "Text";
    public string? CaptionKey => Screenshot is null ? null : Key + "Image";
}

public sealed class GuideScreenshotConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values[0] is not string screenshot || values[1] is not string language)
            return null;

        var path = Path.Combine(AppContext.BaseDirectory, "Help", $"{screenshot}-{language}.png");
        return File.Exists(path) ? new BitmapImage(new Uri(path)) : null;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
