using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KitBuilder.App.Services;
using KitBuilder.Core;
using KitBuilder.Core.Kit;
using KitBuilder.Core.Tools;

namespace KitBuilder.App.ViewModels;

/// <summary>
/// The open kit and its tabs. Every tab edits the kit's files directly; after each change the
/// kit's own Test-Kit.ps1 runs again, so the Overview always shows what a build would report.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IPrefsStore _prefs;
    private readonly IDialogService _dialogs;
    private readonly IDesktopService _desktop;
    private readonly IPowerShellRunner _runner;
    private readonly IDriveLister _drives;
    private readonly Action<Action> _post;
    private CancellationTokenSource? _checkCts;

    public MainWindowViewModel(
        IPrefsStore prefs, IDialogService dialogs, IDesktopService desktop,
        IPowerShellRunner runner, IDriveLister drives, Action<Action>? post = null)
    {
        _prefs = prefs;
        _dialogs = dialogs;
        _desktop = desktop;
        _runner = runner;
        _drives = drives;
        // Background work (tool output) reaches the UI through this; tests run it inline.
        _post = post ?? (action => action());
    }

    public static string Version =>
        typeof(MainWindowViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    public string Title => Kit is null ? $"PT Kit Builder {Version}" : $"PT Kit Builder {Version} - {Kit.Root}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(IsKitOpen))]
    private KitFolder? _kit;

    public bool IsKitOpen => Kit is not null;

    /// <summary>Why the last kit could not be opened, shown on the start screen.</summary>
    [ObservableProperty] private string? _openError;

    [ObservableProperty] private SettingsViewModel? _settings;
    [ObservableProperty] private ScriptsViewModel? _scripts;
    [ObservableProperty] private DriversViewModel? _drivers;
    [ObservableProperty] private BuildViewModel? _build;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Errors), nameof(Warnings), nameof(CheckSummary), nameof(HasErrors), nameof(IsReady))]
    private KitCheckResult? _check;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckSummary))]
    private bool _isChecking;

    public IReadOnlyList<string> Errors => Check?.Errors ?? [];
    public IReadOnlyList<string> Warnings => Check?.Warnings ?? [];
    public bool HasErrors => Check?.HasErrors == true;
    public bool IsReady => Check is { HasErrors: false };

    public string CheckSummary =>
        IsChecking ? "Checking the kit..."
        : Check is null ? ""
        : Check.HasErrors ? $"{Check.Errors.Count} problem(s) to fix before building"
        : Check.Warnings.Count > 0 ? $"Ready to build ({Check.Warnings.Count} warning(s))"
        : "Ready to build";

    /// <summary>
    /// Startup: the last kit used, else a kit around the app's own folder (the exe may sit in
    /// or next to the kit), else the start screen.
    /// </summary>
    public async Task StartAsync()
    {
        var path = _prefs.Load().LastKitPath;
        if (path is null || !KitFolder.LooksLikeKit(path))
            path = KitFolder.FindNear(AppContext.BaseDirectory);
        if (path is not null)
            await OpenAsync(path);
    }

    [RelayCommand]
    private async Task BrowseForKitAsync()
    {
        if (Settings is { IsDirty: true } &&
            !await _dialogs.ConfirmAsync("Unsaved settings",
                "The settings have unsaved changes. Open another kit and discard them?", "Discard changes"))
            return;

        var path = await _dialogs.PickFolderAsync("Open the deployment-kit folder");
        if (path is not null)
            await OpenAsync(path);
    }

    public async Task OpenAsync(string path)
    {
        KitFolder kit;
        KitSettings settings;
        try
        {
            kit = KitFolder.Open(path);
            settings = KitSettings.Load(kit);
        }
        catch (Exception e) when (e is KitFolderException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            OpenError = e.Message;
            return;
        }

        OpenError = null;
        Kit = kit;
        var tools = new KitTools(kit, _runner);
        Settings = new SettingsViewModel(kit, settings, _dialogs, OnKitChanged);
        Scripts = new ScriptsViewModel(new ScriptInventory(kit), _dialogs, _desktop, OnKitChanged);
        Drivers = new DriversViewModel(new DriverLibrary(kit), _dialogs, _desktop, OnKitChanged);
        Build = new BuildViewModel(kit, tools, _drives, _dialogs, () => Check, _post);

        var prefs = _prefs.Load();
        prefs.LastKitPath = kit.Root;
        _prefs.Save(prefs);

        await RunCheckAsync();
    }

    [RelayCommand]
    private void OpenKitFolder()
    {
        if (Kit is not null) _desktop.Open(Kit.Root);
    }

    /// <summary>Runs Test-Kit.ps1. A newer request cancels an older one still running.</summary>
    [RelayCommand]
    public async Task RunCheckAsync()
    {
        if (Kit is null) return;
        _checkCts?.Cancel();
        var cts = _checkCts = new CancellationTokenSource();
        IsChecking = true;
        try
        {
            var result = await new KitTools(Kit, _runner).CheckAsync(cts.Token);
            if (!cts.IsCancellationRequested)
                Check = result;
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_checkCts == cts) IsChecking = false;
        }
    }

    private void OnKitChanged() => _ = RunCheckAsync();
}
