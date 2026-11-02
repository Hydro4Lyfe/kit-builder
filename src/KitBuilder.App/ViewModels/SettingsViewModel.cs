using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KitBuilder.App.Services;
using KitBuilder.Core.Kit;

namespace KitBuilder.App.ViewModels;

/// <summary>
/// A form over settings.json and secrets.json. Changes stay in the form until Save, which
/// writes both files; it warns first if either was edited outside the app in the meantime.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly KitFolder _kit;
    private readonly IDialogService _dialogs;
    private readonly Action _saved;
    private KitSettings _settings;
    private bool _loading;

    public SettingsViewModel(KitFolder kit, KitSettings settings, IDialogService dialogs, Action saved)
    {
        _kit = kit;
        _settings = settings;
        _dialogs = dialogs;
        _saved = saved;
        Pins.CollectionChanged += OnPinsChanged;
        LoadFrom(settings);
    }

    public static IReadOnlyList<string> Editions { get; } =
        ["Windows 11 Pro", "Windows 11 Enterprise", "Windows 11 Education", "Windows 11 Home"];

    public static IReadOnlyList<string> WifiAuthentications { get; } = ["WPA3SAE", "WPA2PSK"];

    /// <summary>Windows time zone IDs, the form autounattend.xml needs (e.g. "Mountain Standard Time").</summary>
    public static IReadOnlyList<string> TimeZones { get; } = LoadTimeZones();

    [ObservableProperty] private string _edition = "";
    [ObservableProperty] private string _timeZone = "";
    [ObservableProperty] private string _uiLanguage = "";
    [ObservableProperty] private string _inputLocale = "";
    [ObservableProperty] private string _systemLocale = "";
    [ObservableProperty] private string _userLocale = "";
    [ObservableProperty] private bool _wifiEnabled;
    [ObservableProperty] private string _wifiSsid = "";
    [ObservableProperty] private bool _wifiHidden;
    [ObservableProperty] private string _wifiAuthentication = "";
    [ObservableProperty] private string _adminPassword = "";
    [ObservableProperty] private string _wifiPassword = "";

    /// <summary>One entry per line; blank lines are ignored on save.</summary>
    [ObservableProperty] private string _removeAppsText = "";
    [ObservableProperty] private string _removeCapabilitiesText = "";
    [ObservableProperty] private string _removeFeaturesText = "";

    public ObservableCollection<PinRowViewModel> Pins { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordChar))]
    private bool _revealPasswords;

    /// <summary>Masks password boxes until the person asks to see them.</summary>
    public char PasswordChar => RevealPasswords ? '\0' : '•';

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _messageIsError;

    /// <summary>Shown when secrets.json does not exist yet: saving creates it.</summary>
    public bool SecretsMissing => !_settings.HasSecretsFile;

    public bool TimeZoneUnknown => TimeZone.Length > 0 && TimeZones.Count > 0 && !TimeZones.Contains(TimeZone);

    partial void OnTimeZoneChanged(string value) => OnPropertyChanged(nameof(TimeZoneUnknown));

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // Any edit to a field marks the form dirty; bookkeeping properties do not.
        if (!_loading && e.PropertyName is not (nameof(IsDirty) or nameof(Message) or nameof(MessageIsError)
                or nameof(RevealPasswords) or nameof(PasswordChar) or nameof(TimeZoneUnknown) or nameof(SecretsMissing)))
            IsDirty = true;
    }

    [RelayCommand]
    private void AddPin() => Pins.Add(new PinRowViewModel(StartPin.Kinds[0], "", RemovePin, MarkDirty));

    private void RemovePin(PinRowViewModel pin) => Pins.Remove(pin);

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private async Task SaveAsync()
    {
        if (_settings.ChangedOnDisk() &&
            !await _dialogs.ConfirmAsync("Changed outside PT Kit Builder",
                "settings.json or secrets.json was changed by something else since it was opened here. " +
                "Saving replaces those changes with what is in this form.", "Overwrite"))
            return;

        _settings.Edition = Edition.Trim();
        _settings.TimeZone = TimeZone.Trim();
        _settings.UiLanguage = UiLanguage.Trim();
        _settings.InputLocale = InputLocale.Trim();
        _settings.SystemLocale = SystemLocale.Trim();
        _settings.UserLocale = UserLocale.Trim();
        _settings.WifiEnabled = WifiEnabled;
        _settings.WifiSsid = WifiSsid.Trim();
        _settings.WifiHidden = WifiHidden;
        _settings.WifiAuthentication = WifiAuthentication;
        _settings.AdminPassword = AdminPassword;
        _settings.WifiPassword = WifiPassword;
        _settings.RemoveApps = SplitLines(RemoveAppsText);
        _settings.RemoveCapabilities = SplitLines(RemoveCapabilitiesText);
        _settings.RemoveFeatures = SplitLines(RemoveFeaturesText);
        _settings.StartPins = Pins.Where(p => p.Value.Trim().Length > 0)
            .Select(p => new StartPin(p.Kind, p.Value.Trim())).ToList();

        try
        {
            _settings.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"Could not save: {e.Message}", isError: true);
            return;
        }

        IsDirty = false;
        OnPropertyChanged(nameof(SecretsMissing));
        ShowMessage("Saved settings.json and secrets.json.", isError: false);
        _saved();
    }

    /// <summary>Throws away unsaved edits and reads both files again.</summary>
    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (IsDirty && !await _dialogs.ConfirmAsync("Discard changes",
                "Reload the settings from disk and lose the unsaved changes?", "Discard changes"))
            return;
        try
        {
            _settings = KitSettings.Load(_kit);
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException)
        {
            ShowMessage($"Could not read the settings: {e.Message}", isError: true);
            return;
        }
        LoadFrom(_settings);
        ShowMessage("Reloaded from disk.", isError: false);
    }

    private void LoadFrom(KitSettings settings)
    {
        _loading = true;
        try
        {
            Edition = settings.Edition;
            TimeZone = settings.TimeZone;
            UiLanguage = settings.UiLanguage;
            InputLocale = settings.InputLocale;
            SystemLocale = settings.SystemLocale;
            UserLocale = settings.UserLocale;
            WifiEnabled = settings.WifiEnabled;
            WifiSsid = settings.WifiSsid;
            WifiHidden = settings.WifiHidden;
            WifiAuthentication = settings.WifiAuthentication.Length > 0 ? settings.WifiAuthentication : WifiAuthentications[0];
            AdminPassword = settings.AdminPassword;
            WifiPassword = settings.WifiPassword;
            RemoveAppsText = string.Join(Environment.NewLine, settings.RemoveApps);
            RemoveCapabilitiesText = string.Join(Environment.NewLine, settings.RemoveCapabilities);
            RemoveFeaturesText = string.Join(Environment.NewLine, settings.RemoveFeatures);
            Pins.Clear();
            foreach (var pin in settings.StartPins)
                Pins.Add(new PinRowViewModel(pin.Kind, pin.Value, RemovePin, MarkDirty));
            OnPropertyChanged(nameof(SecretsMissing));
        }
        finally
        {
            _loading = false;
        }
        IsDirty = false;
    }

    private void OnPinsChanged(object? sender, NotifyCollectionChangedEventArgs e) => MarkDirty();

    private void MarkDirty()
    {
        if (!_loading) IsDirty = true;
    }

    private void ShowMessage(string text, bool isError)
    {
        Message = text;
        MessageIsError = isError;
    }

    private static List<string> SplitLines(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    private static IReadOnlyList<string> LoadTimeZones()
    {
        // On Windows the IDs are already Windows IDs; elsewhere (WSL) they are IANA and need converting.
        return TimeZoneInfo.GetSystemTimeZones()
            .Select(tz => OperatingSystem.IsWindows() ? tz.Id
                : TimeZoneInfo.TryConvertIanaIdToWindowsId(tz.Id, out var windowsId) ? windowsId : null)
            .OfType<string>()
            .Distinct()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

/// <summary>One Start menu pin in the settings form.</summary>
public partial class PinRowViewModel : ViewModelBase
{
    private readonly Action<PinRowViewModel> _remove;
    private readonly Action _changed;

    public PinRowViewModel(string kind, string value, Action<PinRowViewModel> remove, Action changed)
    {
        _remove = remove;
        _changed = changed;
        Kind = kind;
        Value = value;
    }

    public static IReadOnlyList<string> Kinds => StartPin.Kinds;

    [ObservableProperty] private string _kind = "";
    [ObservableProperty] private string _value = "";

    partial void OnKindChanged(string value) => _changed();
    partial void OnValueChanged(string value) => _changed();

    [RelayCommand]
    private void Remove() => _remove(this);
}
