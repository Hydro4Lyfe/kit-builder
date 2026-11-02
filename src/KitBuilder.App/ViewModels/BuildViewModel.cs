using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KitBuilder.App.Services;
using KitBuilder.Core.Kit;
using KitBuilder.Core.Tools;

namespace KitBuilder.App.ViewModels;

/// <summary>Runs the kit's Build-Kit.ps1 to a USB drive or an ISO and shows its output.</summary>
public partial class BuildViewModel : ViewModelBase
{
    private readonly KitTools _tools;
    private readonly IDriveLister _drives;
    private readonly IDialogService _dialogs;
    private readonly Func<KitCheckResult?> _latestCheck;
    private readonly Action<Action> _post;
    private readonly StringBuilder _log = new();

    public BuildViewModel(KitFolder kit, KitTools tools, IDriveLister drives, IDialogService dialogs,
        Func<KitCheckResult?> latestCheck, Action<Action> post)
    {
        _tools = tools;
        _drives = drives;
        _dialogs = dialogs;
        _latestCheck = latestCheck;
        _post = post;
        IsoPath = Path.Combine(kit.OutputRoot, "unattend.iso");
        // A USB copy uses robocopy and drive letters; under WSL only the ISO works.
        ToUsb = CanBuildUsb;
        RefreshDrives();
    }

    public bool CanBuildUsb => OperatingSystem.IsWindows();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToIso), nameof(RecoveryKeyWarning))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    private bool _toUsb;

    public bool ToIso
    {
        get => !ToUsb;
        set => ToUsb = !value;
    }

    public ObservableCollection<DriveChoice> Drives { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecoveryKeyWarning))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    private DriveChoice? _selectedDrive;

    /// <summary>Some USB SSDs report themselves as fixed disks.</summary>
    [ObservableProperty] private bool _includeFixedDrives;

    partial void OnIncludeFixedDrivesChanged(bool value) => RefreshDrives();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    private string _isoPath = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    private bool _isBuilding;

    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private string? _result;
    [ObservableProperty] private bool _succeeded;

    /// <summary>Recovery keys left on the chosen USB from earlier builds.</summary>
    public string? RecoveryKeyWarning =>
        ToUsb && SelectedDrive is { RecoveryKeyFiles: > 0 } drive
            ? $"{drive.Letter} holds {drive.RecoveryKeyFiles} BitLocker recovery key file(s) in BitLockerKeys\\. " +
              "Move them into the password manager and delete them from the USB."
            : null;

    [RelayCommand]
    public void RefreshDrives()
    {
        var previous = SelectedDrive?.Letter;
        Drives.Clear();
        foreach (var drive in _drives.List(IncludeFixedDrives)) Drives.Add(drive);
        SelectedDrive = Drives.FirstOrDefault(d => d.Letter == previous) ?? Drives.FirstOrDefault();
    }

    [RelayCommand]
    private async Task BrowseIsoAsync()
    {
        var path = await _dialogs.SaveFileAsync("Save the ISO as", Path.GetFileName(IsoPath), "iso");
        if (path is not null) IsoPath = path;
    }

    private bool CanBuild() => !IsBuilding && (ToUsb ? SelectedDrive is not null : IsoPath.Trim().Length > 0);

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildAsync()
    {
        if (_latestCheck() is { HasErrors: true } check)
        {
            Finish(false, $"Fix the {check.Errors.Count} problem(s) on the Overview tab first; the build would stop on them.");
            return;
        }
        if (RecoveryKeyWarning is { } warning &&
            !await _dialogs.ConfirmAsync("Recovery keys on the USB", warning + " Build anyway?", "Build anyway"))
            return;

        BuildTarget target = ToUsb ? new BuildTarget.Usb(SelectedDrive!.Letter) : new BuildTarget.Iso(IsoPath.Trim());
        _log.Clear();
        LogText = "";
        Result = null;
        IsBuilding = true;
        try
        {
            var ok = await _tools.BuildAsync(target, line => _post(() => AppendLog(line)));
            Finish(ok, ok
                ? target is BuildTarget.Usb usb ? $"The kit is on {usb.DriveLetter}." : $"Built {IsoPath.Trim()}."
                : "The build failed. The log above says why.");
        }
        catch (InvalidOperationException e)
        {
            Finish(false, e.Message);
        }
        finally
        {
            IsBuilding = false;
            RefreshDrives();
        }
    }

    private void AppendLog(string line)
    {
        _log.AppendLine(line);
        LogText = _log.ToString();
    }

    private void Finish(bool ok, string message)
    {
        Succeeded = ok;
        Result = message;
    }
}
