using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using BD_RGR.Services;
using BD_RGR.ViewModels;

namespace BD_RGR.Views;

public partial class BusinessTableView : UserControl
{
    private BusinessTableViewModel? Model => DataContext as BusinessTableViewModel;

    public BusinessTableView()
    {
        InitializeComponent();
        DataContextChanged += ModelChanged;
    }

    private void ModelChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is BusinessTableViewModel oldModel)
            PropertyChangedEventManager.RemoveHandler(oldModel, RefreshColumns, "");
        if (e.NewValue is BusinessTableViewModel model)
            PropertyChangedEventManager.AddHandler(model, RefreshColumns, "");
        BuildColumns();
    }

    private void RefreshColumns(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BusinessTableViewModel.Resource))
            BuildColumns();
        if (e.PropertyName == nameof(BusinessTableViewModel.SortKey) || e.PropertyName == nameof(BusinessTableViewModel.Descending))
            ShowSort();
    }

    private void BuildColumns()
    {
        RowsGrid.Columns.Clear();
        if (Model?.Resource is not { } resource)
            return;
        RowsGrid.Columns.Add(Column("id", "FieldId", new Binding(nameof(DisplayRow.Id)), false));
        foreach (var field in resource.Fields)
            RowsGrid.Columns.Add(Column(field.Name, field.TitleKey, new Binding($"[{field.Name}]"), field.Target is not null));
        ShowSort();
    }

    private DataGridColumn Column(string name, string title, Binding binding, bool reference)
    {
        var header = new TextBlock();
        header.SetBinding(TextBlock.TextProperty, new Binding($"[{title}]") { Source = Translation.Current });
        if (name == "status")
            return new DataGridTemplateColumn
            {
                Header = header, SortMemberPath = name, CellTemplate = (DataTemplate)FindResource("VehicleStatusCell"),
                MinWidth = 85, ClipboardContentBinding = binding
            };
        var column = new DataGridTextColumn { Header = header, SortMemberPath = name, Binding = binding, MinWidth = 65 };
        if (reference)
        {
            var style = new Style(typeof(TextBlock), column.ElementStyle);
            var link = new DataTrigger { Binding = new Binding($"CanFollow[{name}]"), Value = true };
            link.Setters.Add(new Setter(TextBlock.TextDecorationsProperty, TextDecorations.Underline));
            link.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("LinkBrush")));
            link.Setters.Add(new Setter(CursorProperty, Cursors.Hand));
            style.Triggers.Add(link);
            column.ElementStyle = style;
        }
        return column;
    }

    private void ShowSort()
    {
        foreach (var column in RowsGrid.Columns)
            column.SortDirection = column.SortMemberPath == Model?.SortKey
                ? Model.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending
                : null;
    }

    private async void RowsGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (Model is { } model)
            await model.SortAsync(e.Column.SortMemberPath);
    }

    private async void RowsGrid_MouseUp(object sender, MouseButtonEventArgs e)
    {
        for (var element = e.OriginalSource as DependencyObject; element is Visual visual; element = VisualTreeHelper.GetParent(visual))
        {
            if (element is not DataGridCell cell)
                continue;
            if (Model is { } model && cell.DataContext is DisplayRow row
                && row.CanFollow.GetValueOrDefault(cell.Column.SortMemberPath))
            {
                e.Handled = true;
                model.Selection = row;
                await model.FollowAsync(row, cell.Column.SortMemberPath);
            }
            break;
        }
    }

    private async void Search_Click(object sender, RoutedEventArgs e) { if (Model is { } model) await model.SearchAsync(); }

    private async void RowsGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Model is { } model && RowsGrid.CurrentCell.Item is DisplayRow row
            && RowsGrid.CurrentColumn?.SortMemberPath is { } field && row.CanFollow.GetValueOrDefault(field))
        {
            e.Handled = true;
            model.Selection = row;
            await model.FollowAsync(row, field);
        }
    }

    private async void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Model is { } model)
        {
            e.Handled = true;
            await model.SearchAsync();
        }
    }
    private void Add_Click(object sender, RoutedEventArgs e) => Model?.NewRow();
    private void Cancel_Click(object sender, RoutedEventArgs e) => Model?.Cancel();
    private async void Save_Click(object sender, RoutedEventArgs e) { if (Model is { } model) await model.SaveAsync(); }
    private async void Back_Click(object sender, RoutedEventArgs e) { if (Model is { } model) await model.BackAsync(); }
    private async void Previous_Click(object sender, RoutedEventArgs e) { if (Model is { } model) await model.PageAsync(-1); }
    private async void Next_Click(object sender, RoutedEventArgs e) { if (Model is { } model) await model.PageAsync(1); }
    private async void Calculate_Click(object sender, RoutedEventArgs e) { if (Model is { } model) await model.CalculateAsync(); }
    private async void Related_Click(object sender, RoutedEventArgs e)
    {
        if (Model is { } model && sender is Button { DataContext: RelatedAction action })
            await model.RelatedAsync(action);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Model is not { CanDelete: true, Selection: { } row } model)
            return;
        var text = Translation.Current;
        var key = model.Resource?.IsVehicleSubtype == true ? "ConfirmVehicleDelete" : "ConfirmRecordDelete";
        var message = string.Format(text.Culture, text[key], row.Id);
        if (AppDialog.Confirm(Window.GetWindow(this), text["Delete"], message))
            await model.DeleteAsync();
    }
}
