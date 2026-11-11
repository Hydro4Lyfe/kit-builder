namespace KitBuilder.Core.Tools;

/// <summary>A drive the kit could be copied to.</summary>
public sealed record DriveChoice(string Letter, string Label, long SizeBytes, bool IsRemovable, int RecoveryKeyFiles)

{
    public string Display =>
        $"{Letter}  {(Label.Length > 0 ? Label : "(no label)")}  {SizeBytes / 1_000_000_000.0:0.#} GB" +
        (IsRemovable ? "" : "  (not removable)");
}

public interface IDriveLister
{
    /// <summary>Ready drives, excluding the system drive. Fixed drives only when <paramref name="includeFixed"/>.</summary>
    IReadOnlyList<DriveChoice> List(bool includeFixed);
}

/// <summary>
/// Lists drives for the Build tab. Some USB SSDs report themselves as fixed, hence the
/// option to include fixed drives; the system drive is never offered.
/// </summary>
public sealed class DriveLister : IDriveLister
{
    public IReadOnlyList<DriveChoice> List(bool includeFixed)
    {
        if (!OperatingSystem.IsWindows()) return [];
        var system = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";

        return DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Where(d => d.DriveType == DriveType.Removable || (includeFixed && d.DriveType == DriveType.Fixed))
            .Where(d => !string.Equals(d.RootDirectory.FullName, system, StringComparison.OrdinalIgnoreCase))
            .Select(d => new DriveChoice(
                d.Name.TrimEnd('\\'),
                d.VolumeLabel,
                d.TotalSize,
                d.DriveType == DriveType.Removable,
                CountRecoveryKeys(d.RootDirectory.FullName)))
            .ToList();
    }

    /// <summary>Files in &lt;root&gt;\BitLockerKeys: recovery keys still waiting to be moved off the USB.</summary>
    public static int CountRecoveryKeys(string root)
    {
        var dir = Path.Combine(root, "BitLockerKeys");
        try
        {
            return Directory.Exists(dir) ? Directory.EnumerateFiles(dir).Count() : 0;
        }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }
}
