using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using BD_RGR.Services;
using BD_RGR.ViewModels;
using Microsoft.Win32;

namespace BD_RGR.Views;

public partial class DocumentsView : UserControl
{
    private DocumentsViewModel? Model => DataContext as DocumentsViewModel;

    public DocumentsView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is DocumentsViewModel previous)
                PropertyChangedEventManager.RemoveHandler(previous, OnColumnsChanged, nameof(DocumentsViewModel.Columns));
            if (e.NewValue is DocumentsViewModel current)
                PropertyChangedEventManager.AddHandler(current, OnColumnsChanged, nameof(DocumentsViewModel.Columns));
            RebuildColumns();
        };
    }

    private void OnColumnsChanged(object? sender, PropertyChangedEventArgs e) => RebuildColumns();

    private void RebuildColumns()
    {
        ReportGrid.Columns.Clear();
        if (Model is not { } model)
            return;
        for (var i = 0; i < model.Columns.Length; i++)
        {
            var column = model.Columns[i];
            var header = new TextBlock { Text = column.Name };
            if (column.TitleKey is { } key)
                header.SetBinding(TextBlock.TextProperty, new Binding($"[{key}]") { Source = Translation.Current });
            ReportGrid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding($"[{i}]"), MinWidth = 80 });
        }
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (Model is { } model)
            await model.RunAsync();
    }

    private async void Csv_Click(object sender, RoutedEventArgs e)
    {
        if (Model is not { CanExport: true } model)
            return;
        var text = Translation.Current;
        var dialog = new SaveFileDialog
        {
            Title=text["ExportCsv"], Filter=$"{text["CsvFile"]}|*.csv",
            FileName=text["ReportFileName"], DefaultExt=".csv", AddExtension=true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        try
        {
            if (await model.CsvAsync(text.Language) is { } content)
                await File.WriteAllBytesAsync(dialog.FileName, content);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            model.ShowFileError();
        }
    }
}
