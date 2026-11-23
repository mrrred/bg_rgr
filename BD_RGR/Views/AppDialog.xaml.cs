using System.Windows;
using BD_RGR.Services;

namespace BD_RGR.Views;

internal sealed partial class AppDialog : Window
{
    private AppDialog(Window? owner, string title, string message, bool confirm)
    {
        InitializeComponent();
        Title = title;
        Message.Text = message;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        if (owner is not null)
            Owner = owner;
        AcceptButton.Content = Translation.Current[confirm ? "Delete" : "DialogOk"];
        AcceptButton.IsDefault = !confirm;
        CancelButton.IsDefault = confirm;
        CancelButton.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
    }

    public static bool Confirm(Window? owner, string title, string message) =>
        new AppDialog(owner, title, message, confirm: true).ShowDialog() == true;

    public static void ShowError(string title, string message) =>
        new AppDialog(null, title, message, confirm: false).ShowDialog();

    private void Accept_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
