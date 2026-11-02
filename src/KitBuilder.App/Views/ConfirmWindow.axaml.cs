using Avalonia.Controls;

namespace KitBuilder.App.Views;

/// <summary>A two-button question. Avalonia has no built-in message box.</summary>
public partial class ConfirmWindow : Window
{
    public ConfirmWindow() : this("", "", "OK") { }

    public ConfirmWindow(string title, string message, string confirmText)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        ConfirmButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }
}
