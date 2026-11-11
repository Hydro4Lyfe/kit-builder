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

    /// <summary>
    /// Switches an item on or off. Items whose header has an "Important:" line ask first, since
    /// switching those off breaks something without any error during setup.
    /// </summary>
    internal async Task ToggleAsync(ScriptRowViewModel row, bool enable)
    {
        if (!enable && row.Important is { } important &&
            !await _dialogs.ConfirmAsync($"Switch off {row.Title}?",
                $"{important}\n\nSwitch it off anyway? It stays in the kit and can be switched back on.", "Switch off"))
        {
            row.RefreshSwitch();
            return;
        }
        Apply(() =>
        {
            if (enable) _inventory.Enable(row.Script);
            else _inventory.Disable(row.Script);
        });
    }

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

    internal void CreateScript(PhaseViewModel phase) => Apply(() =>
    {
        var created = _inventory.NewScript(phase.Name, phase.NewKind.Kind, phase.NewName, phase.NewDescription, phase.NewImportant);
        phase.CloseNewScript();
        _desktop.Edit(created.FullPath);
    });

    internal void Edit(ScriptRowViewModel row) => _desktop.Edit(row.Script.FullPath);

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

/// <summary>One phase folder and its files, named by when it runs rather than by its folder name.</summary>
public partial class PhaseViewModel(ScriptsViewModel owner, string name) : ViewModelBase
{
    /// <summary>The folder name under Scripts\ (specialize, defaultuser, ...).</summary>
    public string Name { get; } = name;

    public string DisplayName => Name switch
    {
        "specialize" => "Setting up the computer",
        "defaultuser" => "Defaults for new users",
        "firstlogon" => "First sign-in",
        "useronce" => "Each user's first sign-in",
        _ => Name,
    };

    public string FolderLabel => $"Scripts\\{Name}";

    public string Description => Name switch
    {
        "specialize" => "Runs before anyone signs in. Changes that apply to the whole computer: drivers, removing built-in apps, Windows settings.",
        "defaultuser" => "Settings every new user account starts with, such as taskbar and Explorer options. Use registry settings files here.",
        "firstlogon" => "Runs once, when the Administrator signs in automatically at the end of setup. The network is up, so apps install here.",
        "useronce" => "Runs once for each person, the first time they sign in. Settings that must be applied as that user.",
        _ => "",
    };

    public ObservableCollection<ScriptRowViewModel> Items { get; } = [];

    // ---- "New script" panel ------------------------------------------------------------

    public static IReadOnlyList<NewScriptKindOption> Kinds { get; } =
    [
        new(NewScriptKind.PowerShell, "PowerShell script", "For anything: settings, files, installs. Runs top to bottom."),
        new(NewScriptKind.Registry, "Registry settings", "Just registry values. Easiest to read and change later."),
        new(NewScriptKind.Command, "Command file (.cmd)", "Command Prompt commands, one per line."),
    ];

    [ObservableProperty] private bool _isNewScriptOpen;

    [ObservableProperty] private NewScriptKindOption _newKind = Kinds[0];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateScriptCommand))]
    private string _newName = "";

    /// <summary>One sentence: becomes the script's first comment line, shown in this list.</summary>
    [ObservableProperty] private string _newDescription = "";

    /// <summary>Optional: what breaks if it is switched off. Makes the app ask first.</summary>
    [ObservableProperty] private string _newImportant = "";

    [RelayCommand]
    private void OpenNewScript()
    {
        // Settings for new users are almost always registry values.
        NewKind = Name == "defaultuser" ? Kinds[1] : Kinds[0];
        IsNewScriptOpen = true;
    }

    [RelayCommand]
    internal void CloseNewScript()
    {
        IsNewScriptOpen = false;
        NewName = "";
        NewDescription = "";
        NewImportant = "";
    }

    internal void Load(IReadOnlyList<KitScript> scripts)
    {
        Items.Clear();
        foreach (var script in scripts) Items.Add(new ScriptRowViewModel(owner, script));
    }

    [RelayCommand]
    private Task AddFileAsync() => owner.AddFileAsync(this);

    private bool CanCreateScript() => NewName.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanCreateScript))]
    private void CreateScript() => owner.CreateScript(this);

    [RelayCommand]
    private void OpenFolder() => owner.OpenFolder(this);
}

/// <summary>A choice in the "New script" panel.</summary>
public sealed record NewScriptKindOption(NewScriptKind Kind, string Label, string Hint);

/// <summary>One file in a phase folder.</summary>
public partial class ScriptRowViewModel : ViewModelBase
{
    private readonly ScriptsViewModel _owner;

    public ScriptRowViewModel(ScriptsViewModel owner, KitScript script)
    {
        _owner = owner;
        Script = script;
        var header = ScriptHeader.Read(script.FullPath);
        Description = header.Description;
        Important = header.Important;
        // PT Script Builder writes this file and owns its header, so it is described here instead.
        IsFromScriptBuilder = IsHelper && IsGeneratedByScriptBuilder(script.FullPath);
        if (IsFromScriptBuilder)
            Description = "The list of apps to install, made in PT Script Builder. Install apps runs it; to change the apps, regenerate it there.";
    }

    public bool IsFromScriptBuilder { get; }

    private static bool IsGeneratedByScriptBuilder(string path)
    {
        if (!path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            return File.ReadLines(path).Take(5).Any(l => l.Contains("Generated by PT Script Builder", StringComparison.Ordinal));
        }
        catch (IOException) { return false; }
    }

    public KitScript Script { get; }
    public string FileName => Script.FileName;
    public bool IsHelper => Script.State == ScriptState.Helper;
    public bool CanToggle => !IsHelper;
    public bool CanMove => Script.State == ScriptState.Enabled;

    /// <summary>Text files only: a program (.exe) or a binary helper has nothing to edit.</summary>
    public bool CanEdit => Path.GetExtension(FileName).ToLowerInvariant() is ".ps1" or ".reg" or ".cmd" or ".bat" or ".xml" or ".txt" or ".json";

    /// <summary>"05-start-menu.ps1" shown as "Start menu".</summary>
    public string Title => IsFromScriptBuilder ? "App list (from PT Script Builder)" : FriendlyTitle(Script.Title);

    /// <summary>What the item does, from the first line of its own comment header.</summary>
    public string? Description { get; }

    /// <summary>What breaks if it is switched off (its "Important:" comment line), or null.</summary>
    public string? Important { get; }

    public string TypeLabel => IsHelper ? "Supporting file" : Path.GetExtension(FileName).ToLowerInvariant() switch
    {
        ".ps1" => "PowerShell script",
        ".reg" => "Registry settings",
        ".cmd" or ".bat" => "Command file",
        ".exe" => "Program",
        _ => "File",
    };

    public string StateLabel => Script.State switch
    {
        ScriptState.Helper => "Used by another item; does not run by itself",
        ScriptState.Disabled => "Off: skipped during setup, but kept",
        _ => "",
    };

    /// <summary>The on/off switch: turning it off moves the file into the phase's _disabled folder.</summary>
    public bool IsOn
    {
        get => Script.State == ScriptState.Enabled;
        set
        {
            if (value == IsOn || IsHelper) return;
            _ = _owner.ToggleAsync(this, value);
        }
    }

    /// <summary>Puts the switch back when switching off was cancelled.</summary>
    internal void RefreshSwitch() => OnPropertyChanged(nameof(IsOn));

    private static readonly Dictionary<string, string> KnownWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["xps"] = "XPS", ["wifi"] = "Wi-Fi", ["bitlocker"] = "BitLocker", ["copilot"] = "Copilot",
        ["edge"] = "Edge", ["windows"] = "Windows", ["hello"] = "Hello", ["admin"] = "Administrator",
        ["onedrive"] = "OneDrive",
    };

    internal static string FriendlyTitle(string title)
    {
        var name = Path.GetFileNameWithoutExtension(title).TrimStart('_');
        var words = name.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => KnownWords.TryGetValue(w, out var known) ? known : w)
            .ToList();
        if (words.Count == 0) return title;
        if (!KnownWords.ContainsValue(words[0]) && words[0].Length > 0)
            words[0] = char.ToUpperInvariant(words[0][0]) + words[0][1..];
        return string.Join(" ", words);
    }

    [RelayCommand]
    private void MoveUp() => _owner.Move(this, -1);

    [RelayCommand]
    private void MoveDown() => _owner.Move(this, +1);

    [RelayCommand]
    private void Edit() => _owner.Edit(this);

    [RelayCommand]
    private void Reveal() => _owner.Reveal(this);
}
