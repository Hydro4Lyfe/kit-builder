using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KitBuilder.App.Services;
using KitBuilder.Core.Kit;

namespace KitBuilder.App.ViewModels;

/// <summary>
/// The phase folders. Unlike settings, every action here is applied to the files at once:
/// it is a single move, rename or copy, the same thing a person would do in Explorer.
/// </summary>
public partial class ScriptsViewModel : ViewModelBase
{
    private readonly ScriptInventory _inventory;
    private readonly IDialogService _dialogs;
    private readonly IDesktopService _desktop;
    private readonly Action _changed;

    public ScriptsViewModel(ScriptInventory inventory, IDialogService dialogs, IDesktopService desktop, Action changed)
    {
        _inventory = inventory;
        _dialogs = dialogs;
        _desktop = desktop;
        _changed = changed;
        Phases = ScriptInventory.Phases.Select(p => new PhaseViewModel(this, p)).ToList();
        Refresh();
    }

    public IReadOnlyList<PhaseViewModel> Phases { get; }

    [ObservableProperty] private PhaseViewModel? _selectedPhase;

    [ObservableProperty] private string? _error;

    [RelayCommand]
    public void Refresh()
    {
        foreach (var phase in Phases) phase.Load(_inventory.List(phase.Name));
        SelectedPhase ??= Phases[0];
    }

    internal void Toggle(ScriptRowViewModel row, bool enable) => Apply(() =>
    {
        if (enable) _inventory.Enable(row.Script);
        else _inventory.Disable(row.Script);
    });

    internal void Move(ScriptRowViewModel row, int direction) => Apply(() =>
    {
        // Neighbour among the enabled scripts only: helpers and disabled files have no run order.
        var phase = Phases.Single(p => p.Name == row.Script.Phase);
        var enabled = phase.Items.Where(r => r.Script.State == ScriptState.Enabled).ToList();
        var index = enabled.IndexOf(row) + direction;
        if (index < 0 || index >= enabled.Count) return;
        _inventory.SwapOrder(row.Script, enabled[index].Script);
    });

    internal async Task AddFileAsync(PhaseViewModel phase)
    {
        var path = await _dialogs.PickFileAsync($"Add a script to {phase.Name}", "Scripts and registry files",
            ScriptInventory.RunnableExtensions.Select(e => "*" + e).ToList());
        if (path is not null) Apply(() => _inventory.AddFile(phase.Name, path));
    }

    internal void NewReg(PhaseViewModel phase) => Apply(() =>
    {
        var created = _inventory.NewRegFile(phase.Name, phase.NewRegName);
        phase.NewRegName = "";
        _desktop.Open(created.FullPath);
    });

    internal void Open(ScriptRowViewModel row) => _desktop.Open(row.Script.FullPath);

    internal void Reveal(ScriptRowViewModel row) => _desktop.Reveal(row.Script.FullPath);

    internal void OpenFolder(PhaseViewModel phase) => _desktop.Open(_inventory.PhaseFolder(phase.Name));

    private void Apply(Action action)
    {
        try
        {
            action();
            Error = null;
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Error = e.Message;
        }
        Refresh();
        _changed();
    }
}

/// <summary>One phase folder and its files.</summary>
public partial class PhaseViewModel(ScriptsViewModel owner, string name) : ViewModelBase
{
    public string Name { get; } = name;

    public string Description => Name switch
    {
        "specialize" => "Runs as SYSTEM before any user exists. Machine-wide settings (HKLM), drivers, app removal.",
        "defaultuser" => "Settings every new account starts with. .reg files with keys under HKEY_USERS\\DefaultUser.",
        "firstlogon" => "Runs once as Administrator, with network. Software installs, BitLocker.",
        "useronce" => "Runs once for each user at their first logon. HKCU settings.",
        _ => "",
    };

    public ObservableCollection<ScriptRowViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NewRegCommand))]
    private string _newRegName = "";

    internal void Load(IReadOnlyList<KitScript> scripts)
    {
        Items.Clear();
        foreach (var script in scripts) Items.Add(new ScriptRowViewModel(owner, script));
    }

    [RelayCommand]
    private Task AddFileAsync() => owner.AddFileAsync(this);

    private bool CanNewReg() => NewRegName.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanNewReg))]
    private void NewReg() => owner.NewReg(this);

    [RelayCommand]
    private void OpenFolder() => owner.OpenFolder(this);
}

/// <summary>One file in a phase folder.</summary>
public partial class ScriptRowViewModel : ViewModelBase
{
    private readonly ScriptsViewModel _owner;

    public ScriptRowViewModel(ScriptsViewModel owner, KitScript script)
    {
        _owner = owner;
        Script = script;
    }

    public KitScript Script { get; }
    public string FileName => Script.FileName;
    public bool IsHelper => Script.State == ScriptState.Helper;
    public bool CanToggle => !IsHelper;
    public bool CanMove => Script.State == ScriptState.Enabled;

    public string StateLabel => Script.State switch
    {
        ScriptState.Helper => "helper file, used by another script",
        ScriptState.Disabled => "off",
        _ => "",
    };

    /// <summary>The on/off switch: turning it off moves the file into _disabled.</summary>
    public bool IsOn
    {
        get => Script.State == ScriptState.Enabled;
        set
        {
            if (value == IsOn || IsHelper) return;
            _owner.Toggle(this, value);
        }
    }

    [RelayCommand]
    private void MoveUp() => _owner.Move(this, -1);

    [RelayCommand]
    private void MoveDown() => _owner.Move(this, +1);

    [RelayCommand]
    private void Open() => _owner.Open(this);

    [RelayCommand]
    private void Reveal() => _owner.Reveal(this);
}
