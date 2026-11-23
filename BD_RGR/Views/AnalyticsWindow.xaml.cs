using System.IO;
using System.Windows;
using BD_RGR.Services;
using BD_RGR.ViewModels;
using Microsoft.Win32;

namespace BD_RGR.Views;

public partial class AnalyticsWindow : Window
{
    public AnalyticsWindow(AnalyticsViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        UpdateLanguage();
        Translation.Current.LanguageChanged += OnLanguageChanged;
        Loaded += async (_, _) => await model.StartAsync();
        Closed += (_, _) =>
        {
            Translation.Current.LanguageChanged -= OnLanguageChanged;
            model.Dispose();
        };
    }

    private AnalyticsViewModel Model => (AnalyticsViewModel)DataContext;

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await Model.RefreshAsync();

    private void Png_Click(object sender, RoutedEventArgs e)
    {
        if (!Model.CanExport)
            return;
        var text = Translation.Current;
        var dialog = new SaveFileDialog
        {
            Title=text["ExportPng"], Filter=$"{text["PngFile"]}|*.png", DefaultExt=".png",
            AddExtension=true, FileName=text["AnalyticsFileName"]
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            using var file = File.Create(dialog.FileName);
            Chart.SavePng(file);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Model.ShowFileError();
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => UpdateLanguage();
    private void UpdateLanguage() =>
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(Translation.Current.Culture.Name);
}
