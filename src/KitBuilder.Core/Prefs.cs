using System.Text.Json;

namespace KitBuilder.Core;

/// <summary>
/// The app's only state of its own: which kit folder was open last. Kept in
/// %APPDATA%\PT Kit Builder\prefs.json so it never ends up inside the kit.
/// </summary>
public sealed class Prefs
{
    public string? LastKitPath { get; set; }
}

public interface IPrefsStore
{
    Prefs Load();
    void Save(Prefs prefs);
}

public sealed class JsonPrefsStore(string path) : IPrefsStore
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PT Kit Builder", "prefs.json");

    public Prefs Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Prefs>(File.ReadAllText(path)) ?? new Prefs() : new Prefs();
        }
        catch (JsonException)
        {
            // A corrupt prefs file only loses the last-opened folder; start fresh.
            return new Prefs();
        }
    }

    public void Save(Prefs prefs)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(prefs, new JsonSerializerOptions { WriteIndented = true }));
    }
}
