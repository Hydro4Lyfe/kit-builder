using System.Text;
using System.Text.RegularExpressions;

namespace KitBuilder.Core.Kit;

/// <summary>How the kit's phase runner treats a file.</summary>
public enum ScriptState
{
    /// <summary>Directly in the phase folder with a runnable extension: it runs.</summary>
    Enabled,
    /// <summary>In the phase's <c>_disabled</c> folder: kept, but skipped.</summary>
    Disabled,
    /// <summary>Starts with <c>_</c>: a file another script uses (data, a wrapped script). Never runs by itself.</summary>
    Helper,
}

/// <summary>One file in a phase folder.</summary>
public sealed record KitScript(string Phase, string FullPath, ScriptState State)
{
    private static readonly Regex NumberPrefix = new(@"^(\d+)-(.+)$");

    public string FileName => Path.GetFileName(FullPath);

    /// <summary>The run-order number from a <c>NN-</c> prefix, or null when there is none.</summary>
    public int? Number => NumberPrefix.Match(FileName) is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;

    /// <summary>The <c>NN</c> prefix exactly as written (keeps <c>01</c> vs <c>1</c>), or null.</summary>
    public string? PrefixText => NumberPrefix.Match(FileName) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>The file name without its number prefix.</summary>
    public string Title => NumberPrefix.Match(FileName) is { Success: true } m ? m.Groups[2].Value : FileName;
}

/// <summary>
/// Lists and rearranges the scripts in <c>Scripts\&lt;phase&gt;\</c>. Every operation is a plain
/// file move/rename/copy, mirroring what the kit README tells people to do by hand.
/// </summary>
public sealed class ScriptInventory(KitFolder kit)
{
    public const string DisabledFolderName = "_disabled";

    /// <summary>The phases in the order the install runs them.</summary>
    public static readonly IReadOnlyList<string> Phases = ["specialize", "defaultuser", "firstlogon", "useronce"];

    /// <summary>Extensions Scripts\_system\Invoke-Phase.ps1 knows how to run.</summary>
    public static readonly IReadOnlyList<string> RunnableExtensions = [".ps1", ".reg", ".cmd", ".bat", ".exe"];

    public string PhaseFolder(string phase) => Path.Combine(kit.ScriptsRoot, phase);

    /// <summary>
    /// Every file in a phase: runnable and helper files in run order (the runner sorts by
    /// name), then the disabled ones.
    /// </summary>
    public IReadOnlyList<KitScript> List(string phase)
    {
        var folder = PhaseFolder(phase);
        if (!Directory.Exists(folder)) return [];

        var top = Directory.EnumerateFiles(folder)
            .Where(IsRealFile)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(path => new KitScript(phase, path,
                Path.GetFileName(path).StartsWith('_') ? ScriptState.Helper : ScriptState.Enabled));

        var disabledFolder = Path.Combine(folder, DisabledFolderName);
        var disabled = Directory.Exists(disabledFolder)
            ? Directory.EnumerateFiles(disabledFolder)
                .Where(IsRealFile)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(path => new KitScript(phase, path, ScriptState.Disabled))
            : [];

        return top.Concat(disabled).ToList();
    }

    /// <summary>Moves a script into the phase's <c>_disabled</c> folder.</summary>
    public KitScript Disable(KitScript script)
    {
        if (script.State != ScriptState.Enabled)
            throw new InvalidOperationException($"{script.FileName} is not an enabled script.");
        var target = Path.Combine(PhaseFolder(script.Phase), DisabledFolderName, script.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        MoveFile(script.FullPath, target);
        return script with { FullPath = target, State = ScriptState.Disabled };
    }

    /// <summary>Moves a script out of <c>_disabled</c> back into the phase folder.</summary>
    public KitScript Enable(KitScript script)
    {
        if (script.State != ScriptState.Disabled)
            throw new InvalidOperationException($"{script.FileName} is not a disabled script.");
        var target = Path.Combine(PhaseFolder(script.Phase), script.FileName);
        MoveFile(script.FullPath, target);

        // Leave no empty _disabled folder behind.
        var disabledFolder = Path.GetDirectoryName(script.FullPath)!;
        if (!Directory.EnumerateFileSystemEntries(disabledFolder).Any())
            Directory.Delete(disabledFolder);
        return script with { FullPath = target, State = ScriptState.Enabled };
    }

    /// <summary>
    /// Swaps the run order of two enabled scripts by exchanging their number prefixes.
    /// Only those two files are renamed, so references to every other script stay valid.
    /// </summary>
    public void SwapOrder(KitScript a, KitScript b)
    {
        if (a.State != ScriptState.Enabled || b.State != ScriptState.Enabled || a.Phase != b.Phase)
            throw new InvalidOperationException("Only enabled scripts in the same phase can be reordered.");
        if (a.PrefixText is null || b.PrefixText is null)
            throw new InvalidOperationException(
                $"Both scripts need a number prefix (like 10-) to be reordered: {a.FileName}, {b.FileName}.");
        if (a.Number == b.Number)
            throw new InvalidOperationException($"{a.FileName} and {b.FileName} have the same number; rename one first.");

        var folder = PhaseFolder(a.Phase);
        var newA = Path.Combine(folder, $"{b.PrefixText}-{a.Title}");
        var newB = Path.Combine(folder, $"{a.PrefixText}-{b.Title}");
        // Via a temporary name, so a rename can never land on the other file.
        var temp = Path.Combine(folder, $"_swap-{Guid.NewGuid():N}{Path.GetExtension(a.FullPath)}");
        File.Move(a.FullPath, temp);
        File.Move(b.FullPath, newB);
        File.Move(temp, newA);
    }

    /// <summary>The next free number in steps of 10, counting disabled scripts too.</summary>
    public int NextNumber(string phase)
    {
        var highest = List(phase).Select(s => s.Number ?? 0).DefaultIfEmpty(0).Max();
        return (highest / 10 + 1) * 10;
    }

    /// <summary>Copies a file into a phase, numbered to run last.</summary>
    public KitScript AddFile(string phase, string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!RunnableExtensions.Contains(extension))
            throw new InvalidOperationException(
                $"{Path.GetFileName(sourcePath)} cannot run: the supported types are {string.Join(", ", RunnableExtensions)}.");

        var title = new KitScript(phase, sourcePath, ScriptState.Enabled).Title.TrimStart('_');
        var target = Path.Combine(PhaseFolder(phase), $"{NextNumber(phase)}-{title}");
        if (File.Exists(target))
            throw new InvalidOperationException($"{Path.GetFileName(target)} already exists.");
        Directory.CreateDirectory(PhaseFolder(phase));
        File.Copy(sourcePath, target);
        return new KitScript(phase, target, ScriptState.Enabled);
    }

    /// <summary>
    /// Creates an empty .reg file with the header reg.exe requires. In defaultuser the example
    /// key uses HKEY_USERS\DefaultUser, which is where that phase loads the Default profile.
    /// </summary>
    public KitScript NewRegFile(string phase, string title)
    {
        var slug = Slugify(title);
        if (slug.Length == 0)
            throw new InvalidOperationException("Give the new file a name.");
        var target = Path.Combine(PhaseFolder(phase), $"{NextNumber(phase)}-{slug}.reg");
        if (File.Exists(target))
            throw new InvalidOperationException($"{Path.GetFileName(target)} already exists.");

        var hive = phase switch
        {
            "defaultuser" => @"HKEY_USERS\DefaultUser",
            "useronce" => "HKEY_CURRENT_USER",
            _ => "HKEY_LOCAL_MACHINE",
        };
        var text = string.Join("\r\n",
            "Windows Registry Editor Version 5.00",
            "",
            $"; {title.Trim()}",
            "; Example - replace with your own keys and values:",
            $"; [{hive}\\Software\\Example]",
            "; \"Setting\"=dword:00000001",
            "");
        Directory.CreateDirectory(PhaseFolder(phase));
        File.WriteAllText(target, text, Encoding.ASCII);
        return new KitScript(phase, target, ScriptState.Enabled);
    }

    /// <summary>"Turn off Bing search" becomes "turn-off-bing-search".</summary>
    public static string Slugify(string title)
    {
        var slug = Regex.Replace(title.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return slug.Length > 60 ? slug[..60].TrimEnd('-') : slug;
    }

    // Files copied in from Windows into WSL carry these "downloaded from the internet" markers.
    private static bool IsRealFile(string path) => !path.EndsWith(":Zone.Identifier", StringComparison.Ordinal);

    private static void MoveFile(string from, string to)
    {
        if (File.Exists(to))
            throw new InvalidOperationException($"{Path.GetFileName(to)} already exists in {Path.GetDirectoryName(to)}.");
        File.Move(from, to);
    }
}
