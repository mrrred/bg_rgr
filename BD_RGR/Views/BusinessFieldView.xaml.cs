using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BD_RGR.ViewModels;

namespace BD_RGR.Views;

public partial class BusinessFieldView : UserControl
{
    public BusinessFieldView() => InitializeComponent();

    private async void Find_Click(object sender, RoutedEventArgs e) => await FindAsync();

    private async void Find_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        await FindAsync();
    }

    private async Task FindAsync()
    {
        if (DataContext is not FieldEditor field)
            return;
        await field.SearchAsync(force: true);
        if (IsVisible)
            Options.IsDropDownOpen = true;
    }

    private async void Options_Opened(object sender, EventArgs e)
    {
        if (DataContext is FieldEditor field)
            await field.SearchAsync();
    }
}
