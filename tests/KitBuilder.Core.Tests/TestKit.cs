using KitBuilder.Core.Kit;

namespace KitBuilder.Core.Tests;

/// <summary>A throwaway copy of Fixtures/kit, so each test can change files freely.</summary>
public sealed class TestKit : IDisposable
{
    public TestKit()
    {
        Root = Path.Combine(Path.GetTempPath(), "kitbuilder-tests", Guid.NewGuid().ToString("N"));
        Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "kit"), Root);
        // Empty folders do not survive git or the test-output copy; real kits have this one.
        Directory.CreateDirectory(Path.Combine(Root, "Drivers", "_Common"));
        Kit = KitFolder.Open(Root);
    }

    public string Root { get; }
    public KitFolder Kit { get; }

    public string PathOf(params string[] parts) => Path.Combine([Root, .. parts]);

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
    }

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}

/// <summary>A fact that needs PowerShell 7 (pwsh) on PATH; skipped where it is missing.</summary>
public sealed class PwshFactAttribute : FactAttribute
{
    public PwshFactAttribute()
    {
        if (!OnPath(OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh"))
            Skip = "PowerShell is not installed.";
    }

    public static bool OnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator)
            .Any(dir => File.Exists(Path.Combine(dir, exe)));
}
