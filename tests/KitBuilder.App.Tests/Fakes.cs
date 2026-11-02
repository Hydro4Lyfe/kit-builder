using KitBuilder.App.Services;
using KitBuilder.App.ViewModels;
using KitBuilder.Core;
using KitBuilder.Core.Tools;

namespace KitBuilder.App.Tests;

/// <summary>A throwaway copy of the fixture kit (shared with the Core tests).</summary>
public sealed class TestKit : IDisposable
{
    public TestKit()
    {
        Root = Path.Combine(Path.GetTempPath(), "kitbuilder-app-tests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "kit");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(Root, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    public string Root { get; }

    public string PathOf(params string[] parts) => Path.Combine([Root, .. parts]);

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
    }
}

/// <summary>Returns queued answers instead of showing dialogs, and records what was asked.</summary>
public sealed class FakeDialogs : IDialogService
{
    public Queue<string?> Folders { get; } = new();
    public Queue<string?> Files { get; } = new();
    public string? SavePath { get; set; }
    public bool ConfirmAnswer { get; set; } = true;
    public List<string> Confirmations { get; } = [];

    public Task<string?> PickFolderAsync(string title) => Task.FromResult(Folders.Count > 0 ? Folders.Dequeue() : null);

    public Task<string?> PickFileAsync(string title, string typeName, IReadOnlyList<string> patterns) =>
        Task.FromResult(Files.Count > 0 ? Files.Dequeue() : null);

    public Task<string?> SaveFileAsync(string title, string suggestedFileName, string extension) => Task.FromResult(SavePath);

    public Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        Confirmations.Add(message);
        return Task.FromResult(ConfirmAnswer);
    }
}

public sealed class FakeDesktop : IDesktopService
{
    public List<string> Opened { get; } = [];
    public string? Copied { get; private set; }

    public void Open(string path) => Opened.Add(path);
    public void Reveal(string path) => Opened.Add(path);

    public Task CopyTextAsync(string text)
    {
        Copied = text;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Stands in for PowerShell: Test-Kit gets <see cref="CheckJson"/>, Build-Kit prints a line and
/// returns <see cref="BuildExitCode"/>. Real script runs are covered by the Core tests.
/// </summary>
public sealed class FakeRunner : IPowerShellRunner
{
    public string CheckJson { get; set; } = """{ "errors": [], "warnings": [] }""";
    public int BuildExitCode { get; set; }
    public List<IReadOnlyList<string>> BuildCalls { get; } = [];
    public int CheckCount { get; private set; }

    public Task<int> RunAsync(string script, IReadOnlyList<string> arguments, Action<string> onLine, CancellationToken ct = default)
    {
        if (script.EndsWith("Test-Kit.ps1"))
        {
            CheckCount++;
            onLine(CheckJson);
            return Task.FromResult(0);
        }
        BuildCalls.Add(arguments);
        onLine("Built it");
        return Task.FromResult(BuildExitCode);
    }
}

public sealed class FakeDrives(params DriveChoice[] drives) : IDriveLister
{
    public IReadOnlyList<DriveChoice> List(bool includeFixed) =>
        drives.Where(d => includeFixed || d.IsRemovable).ToList();
}

public sealed class FakePrefs : IPrefsStore
{
    public Prefs Prefs { get; set; } = new();
    public Prefs Load() => new() { LastKitPath = Prefs.LastKitPath };
    public void Save(Prefs prefs) => Prefs = prefs;
}

/// <summary>Builds a main view model over fakes, opened on a fixture kit.</summary>
public sealed class Harness : IDisposable
{
    public TestKit Kit { get; } = new();
    public FakeDialogs Dialogs { get; } = new();
    public FakeDesktop Desktop { get; } = new();
    public FakeRunner Runner { get; } = new();
    public FakePrefs Prefs { get; } = new();
    public MainWindowViewModel Main { get; }

    public Harness(params DriveChoice[] drives)
    {
        Main = new MainWindowViewModel(Prefs, Dialogs, Desktop, Runner, new FakeDrives(drives));
    }

    public async Task<Harness> OpenAsync()
    {
        await Main.OpenAsync(Kit.Root);
        Assert.True(Main.IsKitOpen, Main.OpenError);
        return this;
    }

    public void Dispose() => Kit.Dispose();
}
