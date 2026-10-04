using Avalonia.Controls;
using Avalonia.Interactivity;

namespace IBSaveConverter.Views;

/// <summary>A yes/no question. Closes with true only when confirmed. Cancel is the default button.</summary>
public partial class ConfirmDialog : Window
{
    // Needed by the XAML loader and previewer.
    public ConfirmDialog() : this("Confirm", string.Empty, "OK") { }

    public ConfirmDialog(string title, string message, string confirmText)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
