using System.Diagnostics;
using System.IO.Compression;

namespace KitBuilder.Core.Kit;

/// <summary>A driver folder: a model under Drivers\, Drivers\_Common, or $WinPEDriver$.</summary>
public sealed record DriverFolder(
    string Name,
    string FullPath,
    DriverFolderKind Kind,
    IReadOnlyList<string> MatchLines,
    int InfCount,
    long SizeBytes);

public enum DriverFolderKind
{
    /// <summary>Drivers\&lt;model&gt;: installed when a match.txt line matches the machine.</summary>
    Model,
    /// <summary>Drivers\_Common: installed on every machine.</summary>
    Common,
    /// <summary>$WinPEDriver$: loaded by Windows Setup itself (storage drivers).</summary>
    WinPe,
}

/// <summary>
/// Manages the kit's driver folders. Matching happens at install time in
/// Scripts\specialize\01-drivers.ps1; this class only maintains the folders and match.txt files.
/// </summary>
public sealed class DriverLibrary(KitFolder kit)
{
    public const string CommonFolderName = "_Common";
    public const string MatchFileName = "match.txt";

    /// <summary>
    /// The command a technician runs on the target computer to find the names for match.txt.
    /// It prints just those two names: the model, then (mainly for Lenovo) the friendly version name.
    /// </summary>
    public const string ModelQueryCommand =
        "(Get-CimInstance Win32_ComputerSystem).Model; (Get-CimInstance Win32_ComputerSystemProduct).Version";

    /// <summary>_Common first, then models by name, then $WinPEDriver$.</summary>
    public IReadOnlyList<DriverFolder> List()
    {
        Directory.CreateDirectory(Path.Combine(kit.DriversRoot, CommonFolderName));
        Directory.CreateDirectory(kit.WinPeDriversRoot);

        var models = Directory.EnumerateDirectories(kit.DriversRoot)
            .Where(dir => !string.Equals(Path.GetFileName(dir), CommonFolderName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(dir => Describe(dir, DriverFolderKind.Model));

        return new[] { Describe(Path.Combine(kit.DriversRoot, CommonFolderName), DriverFolderKind.Common) }
            .Concat(models)
            .Append(Describe(kit.WinPeDriversRoot, DriverFolderKind.WinPe))
            .ToList();
    }

    public DriverFolder AddModel(string name, IEnumerable<string> matchLines)
    {
        name = name.Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("Give the model a name, e.g. Dell Latitude 5450.");
        // Windows' rules, checked explicitly: on Linux GetInvalidFileNameChars only lists / and NUL.
        if (name.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|', '\0']) >= 0)
            throw new InvalidOperationException($"\"{name}\" cannot be used as a name. Leave out \\ / : * ? \" < > |.");
        if (name.StartsWith('_'))
            throw new InvalidOperationException("Model names cannot start with _.");

        var dir = Path.Combine(kit.DriversRoot, name);
        if (Directory.Exists(dir))
            throw new InvalidOperationException($"There is already a model called {name}.");

        var lines = CleanLines(matchLines);
        if (lines.Count == 0)
            throw new InvalidOperationException("List at least one computer model, or these drivers are never installed.");

        Directory.CreateDirectory(dir);
        WriteMatchLines(dir, lines);
        return Describe(dir, DriverFolderKind.Model);
    }

    public DriverFolder SetMatchLines(DriverFolder model, IEnumerable<string> matchLines)
    {
        if (model.Kind != DriverFolderKind.Model)
            throw new InvalidOperationException("Only model folders have a match.txt.");
        var lines = CleanLines(matchLines);
        if (lines.Count == 0)
            throw new InvalidOperationException("List at least one computer model, or these drivers are never installed.");
        WriteMatchLines(model.FullPath, lines);
        return Describe(model.FullPath, model.Kind);
    }

    public void RemoveModel(DriverFolder model)
    {
        if (model.Kind != DriverFolderKind.Model)
            throw new InvalidOperationException($"{model.Name} cannot be deleted.");
        Directory.Delete(model.FullPath, recursive: true);
    }

    /// <summary>
    /// Adds a driver pack to a folder: a folder is copied, a .zip or .cab is extracted, each
    /// into its own subfolder named after the source. pnputil searches subfolders, so the
    /// manufacturer's layout can stay as it is.
    /// </summary>
    public async Task<DriverFolder> ImportPackAsync(DriverFolder target, string source, CancellationToken ct = default)
    {
        var name = Path.GetFileNameWithoutExtension(source.TrimEnd('\\', '/'));
        var destination = Path.Combine(target.FullPath, name);
        if (Directory.Exists(destination))
            throw new InvalidOperationException($"{target.Name} already has drivers called {name}. Delete that folder first (Show in folder).");

        if (Directory.Exists(source))
        {
            await Task.Run(() => CopyDirectory(source, destination, ct), ct);
        }
        else
        {
            switch (Path.GetExtension(source).ToLowerInvariant())
            {
                case ".zip":
                    await Task.Run(() => ZipFile.ExtractToDirectory(source, destination), ct);
                    break;
                case ".cab":
                    await ExpandCabAsync(source, destination, ct);
                    break;
                case ".exe":
                    throw new InvalidOperationException(
                        "Driver installers (.exe) must be extracted first. Dell packs: run the .exe and choose " +
                        "Extract. Lenovo/HP packs: run the .exe, which asks for a folder to extract to. Then import that folder.");
                default:
                    throw new InvalidOperationException("Choose a folder, a .zip or a .cab file.");
            }
        }

        // The files stay: the technician may still need to extract an inner archive.
        if (CountInfs(destination) == 0)
            throw new InvalidOperationException(
                $"Added {name}, but it contains no drivers (.inf files), so nothing would be installed. " +
                "It probably needs extracting first.");
        return Describe(target.FullPath, target.Kind);
    }

    private static async Task ExpandCabAsync(string cab, string destination, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(".cab driver packs can only be extracted on Windows (expand.exe).");

        Directory.CreateDirectory(destination);
        using var process = Process.Start(new ProcessStartInfo("expand.exe")
        {
            ArgumentList = { cab, "-F:*", destination },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("Could not start expand.exe.");
        var output = process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"expand.exe failed (exit code {process.ExitCode}): {await output}");
    }

    private static DriverFolder Describe(string dir, DriverFolderKind kind)
    {
        var name = kind == DriverFolderKind.WinPe ? "$WinPEDriver$" : Path.GetFileName(dir);
        var matchFile = Path.Combine(dir, MatchFileName);
        var lines = kind == DriverFolderKind.Model && File.Exists(matchFile)
            ? CleanLines(File.ReadAllLines(matchFile))
            : [];
        var files = Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(f => new FileInfo(f)).ToList()
            : [];
        return new DriverFolder(
            name, dir, kind, lines,
            files.Count(f => f.Extension.Equals(".inf", StringComparison.OrdinalIgnoreCase)),
            files.Sum(f => f.Length));
    }

    private static int CountInfs(string dir) =>
        Directory.EnumerateFiles(dir, "*.inf", SearchOption.AllDirectories).Count();

    private static List<string> CleanLines(IEnumerable<string> lines) =>
        lines.Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static void WriteMatchLines(string dir, IEnumerable<string> lines) =>
        File.WriteAllText(Path.Combine(dir, MatchFileName), string.Join("\r\n", lines) + "\r\n");

    private static void CopyDirectory(string source, string destination, CancellationToken ct)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
        }
    }
}
