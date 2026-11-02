using Avalonia.Controls;
using KitBuilder.App.ViewModels;
using KitBuilder.App.Services;

namespace KitBuilder.App.Views;

public partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Asks before closing over unsaved settings; everything else is already on disk.</summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || DataContext is not MainWindowViewModel { Settings.IsDirty: true }) return;

        e.Cancel = true;
        var discard = await new AvaloniaDialogService(this).ConfirmAsync("Unsaved settings",
            "The settings have changes that are not saved. Close and lose them?", "Close without saving");
        if (!discard) return;
        _closeConfirmed = true;
        Close();
    }
}
