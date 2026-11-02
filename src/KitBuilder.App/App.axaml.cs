using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using KitBuilder.App.Services;
using KitBuilder.App.ViewModels;
using KitBuilder.App.Views;
using KitBuilder.Core;
using KitBuilder.Core.Tools;

namespace KitBuilder.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow();
            var viewModel = new MainWindowViewModel(
                new JsonPrefsStore(JsonPrefsStore.DefaultPath),
                new AvaloniaDialogService(mainWindow),
                new DesktopService(mainWindow),
                new PowerShellRunner(),
                new DriveLister(),
                post: action => Dispatcher.UIThread.Post(action));

            mainWindow.DataContext = viewModel;
            desktop.MainWindow = mainWindow;
            _ = viewModel.StartAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
