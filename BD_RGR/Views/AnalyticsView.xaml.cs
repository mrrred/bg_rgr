using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using BD_RGR.Services;
using BD_RGR.ViewModels;
using Microsoft.Win32;

namespace BD_RGR.Views;

public partial class AnalyticsView : UserControl
{
    public AnalyticsView()
    {
        InitializeComponent();
        DataContextChanged += ModelChanged;
        ChartPanels.Tag = 1;
    }

    private AnalyticsViewModel? Model => DataContext as AnalyticsViewModel;

    private void ModelChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is AnalyticsViewModel oldModel)
        {
            PropertyChangedEventManager.RemoveHandler(oldModel, ScreenChanged, nameof(AnalyticsViewModel.ScreenTitle));
            PropertyChangedEventManager.RemoveHandler(oldModel, FiltersChanged, nameof(AnalyticsViewModel.Filters));
        }
        if (e.NewValue is AnalyticsViewModel model)
        {
            PropertyChangedEventManager.AddHandler(model, ScreenChanged, nameof(AnalyticsViewModel.ScreenTitle));
            PropertyChangedEventManager.AddHandler(model, FiltersChanged, nameof(AnalyticsViewModel.Filters));
        }
        BuildFilterBar();
    }

    private void ScreenChanged(object? sender, PropertyChangedEventArgs e) => AnalyticsScroll.ScrollToTop();

    private void FiltersChanged(object? sender, PropertyChangedEventArgs e) => BuildFilterBar();

    private void BuildFilterBar()
    {
        var fields = FilterBar.Children.OfType<ContentPresenter>().ToArray();
        foreach (var field in fields)
            FilterBar.Children.Remove(field);

        var position = FilterBar.Children.IndexOf(ApplyButton);
        var template = (DataTemplate)FindResource("DashboardFilterField");
        foreach (var filter in Model?.Filters ?? [])
        {
            FilterBar.Children.Insert(position++, new ContentPresenter
            {
                Content = filter, ContentTemplate = template, VerticalAlignment = VerticalAlignment.Bottom
            });
        }
    }

    private void Layout_Changed(object sender, SizeChangedEventArgs e)
    {
        if (ChartPanels is not null)
            ChartPanels.Tag = e.NewSize.Width >= 1010 ? 2 : 1;
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (Model is { } model)
            await model.ApplyAsync();
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (Model is { } model)
            await model.ResetAsync();
    }

    private async void Chart_DrillRequested(string filter, string choice)
    {
        if (Model is { } model)
            await model.DrillAsync(filter, choice);
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DashboardChartView { Plot.HasValues: true } chart } || Model is not { } model)
            return;
        var text = Translation.Current;
        var dialog = new SaveFileDialog
        {
            Title = text["ExportChart"], Filter = $"{text["PngFile"]} (*.png)|*.png|{text["PdfFile"]} (*.pdf)|*.pdf",
            AddExtension = true, FileName = text["AnalyticsFileName"]
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        if (extension is not (".png" or ".pdf"))
        {
            AppDialog.ShowError(text["ExportChart"], text["ChartExportFormatError"]);
            return;
        }
        try
        {
            using var file = File.Create(dialog.FileName);
            chart.Export(file, pdf: extension == ".pdf");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            model.ShowFileError();
        }
    }
}
