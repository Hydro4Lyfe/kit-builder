using System.Text.Json.Nodes;

namespace KitBuilder.Core.Kit;

/// <summary>
/// A deployment-kit folder on disk, and the well-known paths inside it.
/// The kit's files are the single source of truth: this app keeps no copy of its own,
/// so a technician can always fall back to editing the folder by hand.
/// </summary>
public sealed class KitFolder
{
    /// <summary>
    /// The settings.json layout this app understands. Matches <c>$supportedSchemaVersion</c>
    /// in the kit's tools\Test-Kit.ps1; bump both together on an incompatible layout change.
    /// </summary>
    public const int SupportedSchemaVersion = 1;

    private KitFolder(string root) => Root = root;

    public string Root { get; }

    public string SettingsPath => Path.Combine(Root, "settings.json");
    public string SecretsPath => Path.Combine(Root, "secrets.json");
    public string SecretsExamplePath => Path.Combine(Root, "secrets.example.json");
    public string ScriptsRoot => Path.Combine(Root, "Scripts");
    public string DriversRoot => Path.Combine(Root, "Drivers");
    public string WinPeDriversRoot => Path.Combine(Root, "$WinPEDriver$");
    public string TestKitScript => Path.Combine(Root, "tools", "Test-Kit.ps1");
    public string BuildKitScript => Path.Combine(Root, "tools", "Build-Kit.ps1");
    public string OutputRoot => Path.Combine(Root, "out");

    /// <summary>True when <paramref name="dir"/> has the files every kit has.</summary>
    public static bool LooksLikeKit(string dir) =>
        File.Exists(Path.Combine(dir, "settings.json")) &&
        File.Exists(Path.Combine(dir, "Scripts", "_scripts.marker"));

    /// <summary>
    /// Opens a kit, or throws <see cref="KitFolderException"/> with a message fit to show a technician.
    /// </summary>
    public static KitFolder Open(string dir)
    {
        var root = Path.GetFullPath(dir);
        if (!Directory.Exists(root))
            throw new KitFolderException($"Folder not found: {root}");
        if (!LooksLikeKit(root))
            throw new KitFolderException(
                $"{root} is not a deployment kit: it needs settings.json and Scripts\\_scripts.marker.");

        var kit = new KitFolder(root);
        foreach (var script in new[] { kit.TestKitScript, kit.BuildKitScript })
        {
            if (!File.Exists(script))
                throw new KitFolderException($"The kit is missing {Path.GetRelativePath(root, script)}.");
        }

        var version = ReadSchemaVersion(kit.SettingsPath);
        if (version > SupportedSchemaVersion)
            throw new KitFolderException(
                $"This kit uses settings schema {version}, but this version of PT Kit Builder only " +
                $"understands up to {SupportedSchemaVersion}. Update PT Kit Builder.");
        return kit;
    }

    /// <summary>
    /// Walks up from <paramref name="start"/> looking for a kit, so the app finds one when
    /// it is run from inside (or next to) the kit folder. Returns null when there is none.
    /// </summary>
    public static string? FindNear(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (LooksLikeKit(dir.FullName)) return dir.FullName;
            var child = Path.Combine(dir.FullName, "deployment-kit");
            if (LooksLikeKit(child)) return child;
        }
        return null;
    }

    private static int ReadSchemaVersion(string settingsPath)
    {
        try
        {
            // A missing schemaVersion means version 1: kits created before it existed.
            var node = JsonNode.Parse(File.ReadAllText(settingsPath))?["schemaVersion"];
            return node is null ? 1 : node.GetValue<int>();
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            throw new KitFolderException($"settings.json could not be read: {e.Message}");
        }
    }
}

/// <summary>A kit folder problem, with a message written for the person using the app.</summary>
public sealed class KitFolderException(string message) : Exception(message);
