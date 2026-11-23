using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BD_RGR.Services;
using BD_RGR.ViewModels;

namespace BD_RGR.Views;

public partial class BusinessFieldView : UserControl
{
    private CancellationTokenSource? _search;
    private bool _settingText;

    public BusinessFieldView()
    {
        InitializeComponent();
        DataContextChanged += FieldChanged;
        IsVisibleChanged += (_, _) => { if (!IsVisible) CloseReferences(); };
        Unloaded += (_, _) => CloseReferences();
    }

    private void FieldChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is FieldEditor previous)
            PropertyChangedEventManager.RemoveHandler(previous, SelectedChanged, nameof(FieldEditor.Selected));
        if (e.NewValue is FieldEditor { CanSearch: true } current)
            PropertyChangedEventManager.AddHandler(current, SelectedChanged, nameof(FieldEditor.Selected));
        CloseReferences();
    }

    private void SelectedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferencePopup.IsOpen)
            RestoreSelectionText();
        else if (ReferenceOptions.ItemsSource is FieldOption[] options)
            foreach (var option in options)
                option.RefreshText();
    }

    private async void ReferenceInput_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_settingText && IsVisible && ReferenceInput.IsKeyboardFocused)
            await SearchReferencesAsync(ReferenceInput.Text.Trim(), debounce: true);
    }

    private async void ReferenceInput_Click(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceInput.IsKeyboardFocused)
            return;

        e.Handled = true;
        ReferenceInput.Focus();
        await SearchReferencesAsync("");
    }

    private void ReferenceInput_GotFocus(object sender, KeyboardFocusChangedEventArgs e) => ReferenceInput.SelectAll();

    private async void ReferenceToggle_Click(object sender, RoutedEventArgs e)
    {
        ReferenceInput.Focus();
        if (ReferencePopup.IsOpen)
            CloseReferences();
        else
        {
            ReferenceInput.SelectAll();
            await SearchReferencesAsync("");
        }
    }

    private async void ReferenceInput_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when !ReferencePopup.IsOpen:
                e.Handled = true;
                ReferenceInput.SelectAll();
                await SearchReferencesAsync("", selectFirst: true);
                break;
            case Key.Down or Key.Up when ReferencePopup.IsOpen:
                e.Handled = true;
                if (ReferenceOptions.Items.Count > 0)
                {
                    ReferenceOptions.SelectedIndex = Math.Clamp(
                        ReferenceOptions.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, ReferenceOptions.Items.Count - 1);
                    ReferenceOptions.ScrollIntoView(ReferenceOptions.SelectedItem);
                }
                break;
            case Key.Enter when ReferencePopup.IsOpen:
                e.Handled = true;
                if (ReferenceOptions.SelectedItem is FieldOption option)
                    ChooseReference(option);
                else if (ReferenceOptions.Items.Count == 1 && ReferenceOptions.Items[0] is FieldOption single)
                    ChooseReference(single);
                break;
            case Key.Escape when ReferencePopup.IsOpen:
                e.Handled = true;
                CloseReferences();
                break;
        }
    }

    private void ReferenceOptions_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(ReferenceOptions, source) is ListBoxItem { Content: FieldOption option })
        {
            e.Handled = true;
            ChooseReference(option);
        }
    }

    private void ReferenceInput_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => CloseReferences();

    private void ChooseReference(FieldOption option)
    {
        if (DataContext is FieldEditor field)
            field.Selected = option;
        CloseReferences();
        ReferenceInput.SelectAll();
    }

    private async Task SearchReferencesAsync(string query, bool debounce = false, bool selectFirst = false)
    {
        if (DataContext is not FieldEditor { CanSearch: true } field)
            return;

        _search?.Cancel();
        using var search = new CancellationTokenSource();
        _search = search;
        ReferenceOptions.ItemsSource = null;
        ReferenceStatus.Text = Translation.Current["AuthWorking"];
        ReferenceStatus.Visibility = Visibility.Visible;
        ReferencePopup.IsOpen = true;
        try
        {
            if (debounce)
                await Task.Delay(175, search.Token);
            var options = await field.FindReferencesAsync(query, search.Token);
            if (search.IsCancellationRequested)
                return;

            ReferenceOptions.ItemsSource = options;
            ReferenceStatus.Text = options switch
            {
                null => Translation.Current["ReferenceSearchFailed"],
                { Length: 0 } => Translation.Current["NoReferenceMatches"],
                _ => ""
            };
            ReferenceStatus.Visibility = string.IsNullOrEmpty(ReferenceStatus.Text) ? Visibility.Collapsed : Visibility.Visible;
            if (selectFirst && options is { Length: > 0 })
                ReferenceOptions.SelectedIndex = 0;
        }
        catch (OperationCanceledException) when (search.IsCancellationRequested)
        {
        }
        finally
        {
            if (_search == search)
                _search = null;
        }
    }

    private void CloseReferences()
    {
        _search?.Cancel();
        ReferencePopup.IsOpen = false;
        RestoreSelectionText();
    }

    private void RestoreSelectionText()
    {
        var label = (DataContext as FieldEditor)?.Selected?.Label ?? "";
        if (ReferenceInput.Text == label)
            return;

        _settingText = true;
        ReferenceInput.Text = label;
        _settingText = false;
    }
}
