using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KitBuilder.App.Services;
using KitBuilder.Core.Kit;

namespace KitBuilder.App.ViewModels;

/// <summary>The kit's driver folders: per-model packs, _Common and $WinPEDriver$.</summary>
public partial class DriversViewModel : ViewModelBase
{
    private readonly DriverLibrary _library;
    private readonly IDialogService _dialogs;
    private readonly IDesktopService _desktop;
    private readonly Action _changed;

    public DriversViewModel(DriverLibrary library, IDialogService dialogs, IDesktopService desktop, Action changed)
    {
        _library = library;
        _dialogs = dialogs;
        _desktop = desktop;
        _changed = changed;
        Refresh();
    }

    public ObservableCollection<DriverFolderViewModel> Folders { get; } = [];

    public string ModelQueryCommand => DriverLibrary.ModelQueryCommand;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddModelCommand))]
    private string _newModelName = "";

    /// <summary>The new model's match.txt, one model name per line; * is a wildcard.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddModelCommand))]
    private string _newModelMatchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;

    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _messageIsError;

    [RelayCommand]
    public void Refresh()
    {
        Folders.Clear();
        foreach (var folder in _library.List()) Folders.Add(new DriverFolderViewModel(this, folder));
    }

    [RelayCommand]
    private Task CopyModelQueryAsync() => _desktop.CopyTextAsync(ModelQueryCommand);

    private bool CanAddModel() => NewModelName.Trim().Length > 0 && NewModelMatchText.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanAddModel))]
    private void AddModel() => Apply(() =>
    {
        var model = _library.AddModel(NewModelName, SplitLines(NewModelMatchText));
        NewModelName = "";
        NewModelMatchText = "";
        return $"Added {model.Name}. Next, add its drivers (below).";
    });

    internal void SaveMatch(DriverFolderViewModel folder) => Apply(() =>
    {
        _library.SetMatchLines(folder.Folder, SplitLines(folder.MatchText));
        return $"Saved the computer models for {folder.DisplayName}.";
    });

    internal async Task RemoveAsync(DriverFolderViewModel folder)
    {
        if (!await _dialogs.ConfirmAsync("Delete model",
                $"Delete {folder.Name} and all of its drivers ({folder.SizeText})? This cannot be undone.", "Delete"))
            return;
        Apply(() =>
        {
            _library.RemoveModel(folder.Folder);
            return $"Deleted {folder.Name}.";
        });
    }

    internal async Task ImportAsync(DriverFolderViewModel folder, bool fromFolder)
    {
        var source = fromFolder
            ? await _dialogs.PickFolderAsync($"Folder of drivers for {folder.DisplayName}")
            : await _dialogs.PickFileAsync($"Driver download for {folder.DisplayName}", "Driver downloads", ["*.zip", "*.cab", "*.exe"]);
        if (source is null) return;

        IsBusy = true;
        ShowMessage($"Adding {Path.GetFileName(source)}...", isError: false);
        try
        {
            await _library.ImportPackAsync(folder.Folder, source);
            ShowMessage($"Added the drivers from {Path.GetFileName(source)} to {folder.DisplayName}.", isError: false);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException
                                      or PlatformNotSupportedException or InvalidDataException)
        {
            ShowMessage(e.Message, isError: true);
        }
        finally
        {
            IsBusy = false;
        }
        Refresh();
        _changed();
    }

    internal void Open(DriverFolderViewModel folder)
    {
        Directory.CreateDirectory(folder.Folder.FullPath);
        _desktop.Open(folder.Folder.FullPath);
    }

    private void Apply(Func<string> action)
    {
        try
        {
            ShowMessage(action(), isError: false);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ShowMessage(e.Message, isError: true);
        }
        Refresh();
        _changed();
    }

    private void ShowMessage(string text, bool isError)
    {
        Message = text;
        MessageIsError = isError;
    }

    private static List<string> SplitLines(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
}

/// <summary>One driver folder.</summary>
public partial class DriverFolderViewModel : ViewModelBase
{
    private readonly DriversViewModel _owner;

    public DriverFolderViewModel(DriversViewModel owner, DriverFolder folder)
    {
        _owner = owner;
        Folder = folder;
        MatchText = string.Join(Environment.NewLine, folder.MatchLines);
    }

    public DriverFolder Folder { get; }

    /// <summary>The folder name: the model name for models, _Common or $WinPEDriver$ otherwise.</summary>
    public string Name => Folder.Name;

    public bool IsModel => Folder.Kind == DriverFolderKind.Model;

    public string DisplayName => Folder.Kind switch
    {
        DriverFolderKind.Common => "All computers",
        DriverFolderKind.WinPe => "Needed by Windows Setup",
        _ => Folder.Name,
    };

    /// <summary>Where it lives in the kit, shown small for anyone working with the folders by hand.</summary>
    public string FolderLabel => Folder.Kind == DriverFolderKind.WinPe ? Folder.Name : $"Drivers\\{Folder.Name}";

    public string KindLabel => Folder.Kind switch
    {
        DriverFolderKind.Common => "Installed on every computer, whatever the model.",
        DriverFolderKind.WinPe => "Only needed when Windows Setup cannot see the computer's disk (usually Intel RST/VMD storage drivers).",
        _ => "Installed only on the computer models listed below.",
    };

    public string SizeText => Folder.SizeBytes switch
    {
        0 => "empty",
        < 1_000_000 => $"{Folder.SizeBytes / 1000.0:0} KB",
        < 1_000_000_000 => $"{Folder.SizeBytes / 1_000_000.0:0.#} MB",
        _ => $"{Folder.SizeBytes / 1_000_000_000.0:0.##} GB",
    };

    public string Summary => Folder.InfCount switch
    {
        0 => "No drivers yet",
        1 => $"1 driver, {SizeText}",
        var n => $"{n} drivers, {SizeText}",
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchChanged))]
    private string _matchText = "";

    public bool MatchChanged => MatchText.Replace("\r", "") != string.Join("\n", Folder.MatchLines);

    [RelayCommand]
    private void SaveMatch() => _owner.SaveMatch(this);

    [RelayCommand]
    private Task ImportFolderAsync() => _owner.ImportAsync(this, fromFolder: true);

    [RelayCommand]
    private Task ImportFileAsync() => _owner.ImportAsync(this, fromFolder: false);

    [RelayCommand]
    private Task RemoveAsync() => _owner.RemoveAsync(this);

    [RelayCommand]
    private void Open() => _owner.Open(this);
}
