namespace KitBuilder.Core.Kit;

/// <summary>A removable part of Windows, with the name a person knows it by.</summary>
public sealed record CatalogEntry(string Id, string Name, string? Note = null);

/// <summary>
/// Display names for the IDs in settings.json's removal lists. This is only a lookup for the
/// UI: settings.json stays the list of what is removed, and any ID not listed here still
/// works (it is shown by its ID). Entries not in settings.json are offered unticked.
/// </summary>
public static class RemovalCatalog
{
    /// <summary>Store apps (removeApps), removed with Remove-AppxProvisionedPackage.</summary>
    public static readonly IReadOnlyList<CatalogEntry> Apps =
    [
        new("Microsoft.Microsoft3DViewer", "3D Viewer"),
        new("Microsoft.BingSearch", "Bing Search"),
        new("Microsoft.WindowsCalculator", "Calculator"),
        new("Microsoft.WindowsCamera", "Camera"),
        new("Clipchamp.Clipchamp", "Clipchamp", "Video editor"),
        new("Microsoft.WindowsAlarms", "Clock", "Alarms & Clock"),
        new("Microsoft.Copilot", "Copilot"),
        new("Microsoft.549981C3F5F10", "Cortana"),
        new("Microsoft.Windows.DevHome", "Dev Home"),
        new("Microsoft.WindowsFeedbackHub", "Feedback Hub"),
        new("Microsoft.GetHelp", "Get Help"),
        new("microsoft.windowscommunicationsapps", "Mail and Calendar", "The old built-in mail app"),
        new("Microsoft.WindowsMaps", "Maps"),
        new("Microsoft.ZuneMusic", "Media Player", "Formerly Groove Music"),
        new("MicrosoftCorporationII.MicrosoftFamily", "Microsoft Family"),
        new("Microsoft.MicrosoftOfficeHub", "Microsoft 365 (Office) app", "The Office hub, not Office itself"),
        new("Microsoft.Todos", "Microsoft To Do"),
        new("Microsoft.MixedReality.Portal", "Mixed Reality Portal"),
        new("Microsoft.ZuneVideo", "Movies & TV"),
        new("Microsoft.BingNews", "News"),
        new("Microsoft.WindowsNotepad", "Notepad"),
        new("Microsoft.Office.OneNote", "OneNote for Windows 10"),
        new("Microsoft.OutlookForWindows", "Outlook (new)"),
        new("Microsoft.Paint", "Paint"),
        new("Microsoft.MSPaint", "Paint 3D"),
        new("Microsoft.People", "People"),
        new("Microsoft.YourPhone", "Phone Link"),
        new("Microsoft.Windows.Photos", "Photos"),
        new("Microsoft.PowerAutomateDesktop", "Power Automate"),
        new("MicrosoftCorporationII.QuickAssist", "Quick Assist", "Remote help from Microsoft"),
        new("Microsoft.SkypeApp", "Skype"),
        new("Microsoft.ScreenSketch", "Snipping Tool"),
        new("Microsoft.MicrosoftSolitaireCollection", "Solitaire Collection"),
        new("Microsoft.WindowsSoundRecorder", "Sound Recorder"),
        new("Microsoft.MicrosoftStickyNotes", "Sticky Notes"),
        new("MSTeams", "Teams"),
        new("MicrosoftTeams", "Teams (personal, old version)"),
        new("Microsoft.WindowsTerminal", "Terminal"),
        new("Microsoft.Getstarted", "Tips"),
        new("Microsoft.Wallet", "Wallet"),
        new("Microsoft.BingWeather", "Weather"),
        new("Microsoft.GamingApp", "Xbox"),
        new("Microsoft.XboxApp", "Xbox Console Companion"),
        new("Microsoft.XboxGamingOverlay", "Xbox Game Bar"),
        new("Microsoft.XboxGameOverlay", "Xbox Game Bar plug-in"),
        new("Microsoft.XboxIdentityProvider", "Xbox sign-in"),
        new("Microsoft.XboxSpeechToTextOverlay", "Xbox speech to text"),
        new("Microsoft.Xbox.TCUI", "Xbox UI components"),
    ];

    /// <summary>Windows capabilities (removeCapabilities), removed with Remove-WindowsCapability.</summary>
    /// <remarks>Hello.Face is deliberately absent: the kit installs it (firstlogon/50-windows-hello-face.ps1).</remarks>
    public static readonly IReadOnlyList<CatalogEntry> Capabilities =
    [
        new("Print.Fax.Scan", "Fax and Scan"),
        new("Language.Handwriting", "Handwriting recognition"),
        new("Browser.InternetExplorer", "Internet Explorer mode"),
        new("OneCoreUAP.OneSync", "Mail and Calendar sync", "Used by the old Mail app"),
        new("MathRecognizer", "Math Input Panel"),
        new("Microsoft.Windows.MSPaint", "Paint (classic)"),
        new("OpenSSH.Client", "OpenSSH client", "The ssh command"),
        new("Microsoft.Windows.PowerShell.ISE", "PowerShell ISE"),
        new("App.Support.QuickAssist", "Quick Assist (old version)"),
        new("Language.Speech", "Speech recognition"),
        new("App.StepsRecorder", "Steps Recorder"),
        new("Language.TextToSpeech", "Text-to-speech voices"),
        new("Media.WindowsMediaPlayer", "Windows Media Player (legacy)"),
        new("Microsoft.Windows.WordPad", "WordPad"),
        new("XPS.Viewer", "XPS Viewer"),
    ];

    /// <summary>Optional features (removeFeatures), removed with Disable-WindowsOptionalFeature -Remove.</summary>
    public static readonly IReadOnlyList<CatalogEntry> Features =
    [
        new("MediaPlayback", "Media features", "Includes legacy Windows Media Player"),
        new("MicrosoftWindowsPowerShellV2Root", "PowerShell 2.0", "Old and insecure"),
        new("Recall", "Recall", "AI screenshots of activity"),
        new("SMB1Protocol", "SMB 1.0 file sharing", "Old and insecure"),
        new("WorkFolders-Client", "Work Folders"),
    ];
}
