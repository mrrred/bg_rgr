using System.IO;
using System.Windows;
using System.Windows.Controls;
using BD_RGR.Services;
using BD_RGR.ViewModels;
using Microsoft.Win32;

namespace BD_RGR.Views;

public partial class AnalyticsView : UserControl
{
    public AnalyticsView() => InitializeComponent();

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
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
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
}
