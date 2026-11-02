using System.Text.Json.Nodes;

namespace KitBuilder.Core.Kit;

/// <summary>A Windows 11 Start menu pin: <c>desktopAppLink</c> (a .lnk path) or <c>packagedAppId</c> (a Store app).</summary>
public sealed record StartPin(string Kind, string Value)
{
    public static readonly IReadOnlyList<string> Kinds = ["desktopAppLink", "packagedAppId"];
}

/// <summary>
/// Typed access to settings.json and secrets.json. Getters and setters go straight to the
/// underlying JSON, so keys this class does not model are kept as they are.
/// </summary>
public sealed class KitSettings
{
    private readonly JsonDocumentFile _settings;
    private readonly JsonDocumentFile _secrets;

    private KitSettings(JsonDocumentFile settings, JsonDocumentFile secrets)
    {
        _settings = settings;
        _secrets = secrets;
    }

    public static KitSettings Load(KitFolder kit) =>
        new(JsonDocumentFile.Load(kit.SettingsPath), JsonDocumentFile.LoadOrEmpty(kit.SecretsPath));

    /// <summary>False until secrets.json exists; saving creates it.</summary>
    public bool HasSecretsFile => File.Exists(_secrets.Path);

    public string Edition { get => GetString(_settings.Root, "edition"); set => SetString(_settings.Root, "edition", value); }
    public string TimeZone { get => GetString(_settings.Root, "timeZone"); set => SetString(_settings.Root, "timeZone", value); }

    public string UiLanguage { get => GetString(Language, "uiLanguage"); set => SetString(Language, "uiLanguage", value); }
    public string InputLocale { get => GetString(Language, "inputLocale"); set => SetString(Language, "inputLocale", value); }
    public string SystemLocale { get => GetString(Language, "systemLocale"); set => SetString(Language, "systemLocale", value); }
    public string UserLocale { get => GetString(Language, "userLocale"); set => SetString(Language, "userLocale", value); }

    public bool WifiEnabled { get => GetBool(Wifi, "enabled"); set => Wifi["enabled"] = value; }
    public string WifiSsid { get => GetString(Wifi, "ssid"); set => SetString(Wifi, "ssid", value); }
    public bool WifiHidden { get => GetBool(Wifi, "hidden"); set => Wifi["hidden"] = value; }
    public string WifiAuthentication { get => GetString(Wifi, "authentication"); set => SetString(Wifi, "authentication", value); }

    public string AdminPassword { get => GetString(_secrets.Root, "adminPassword"); set => SetString(_secrets.Root, "adminPassword", value); }
    public string WifiPassword { get => GetString(_secrets.Root, "wifiPassword"); set => SetString(_secrets.Root, "wifiPassword", value); }

    public IReadOnlyList<string> RemoveApps { get => GetList("removeApps"); set => SetList("removeApps", value); }
    public IReadOnlyList<string> RemoveCapabilities { get => GetList("removeCapabilities"); set => SetList("removeCapabilities", value); }
    public IReadOnlyList<string> RemoveFeatures { get => GetList("removeFeatures"); set => SetList("removeFeatures", value); }

    public IReadOnlyList<StartPin> StartPins
    {
        get => (_settings.Root["startPins"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(pin => pin.FirstOrDefault())
            .Where(p => p.Value is not null)
            .Select(p => new StartPin(p.Key, p.Value!.ToString()))
            .ToList();
        set => _settings.Root["startPins"] = new JsonArray(
            value.Select(p => (JsonNode)new JsonObject { [p.Kind] = p.Value }).ToArray());
    }

    /// <summary>True when either file was changed by something else since it was loaded or saved.</summary>
    public bool ChangedOnDisk() => _settings.ChangedOnDisk() || _secrets.ChangedOnDisk();

    public void Save()
    {
        _settings.Save();
        _secrets.Save();
    }

    private JsonObject Language => Child(_settings.Root, "language");
    private JsonObject Wifi => Child(_settings.Root, "wifi");

    private static JsonObject Child(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject child) return child;
        var created = new JsonObject();
        parent[name] = created;
        return created;
    }

    private static string GetString(JsonObject obj, string name) =>
        obj[name] is JsonValue value && value.TryGetValue(out string? text) ? text : "";

    private static void SetString(JsonObject obj, string name, string value) => obj[name] = value;

    private static bool GetBool(JsonObject obj, string name) =>
        obj[name] is JsonValue value && value.TryGetValue(out bool flag) && flag;

    private IReadOnlyList<string> GetList(string name) =>
        (_settings.Root[name] as JsonArray ?? [])
            .Select(item => item?.ToString() ?? "")
            .Where(item => item.Length > 0)
            .ToList();

    private void SetList(string name, IEnumerable<string> values) =>
        _settings.Root[name] = new JsonArray(
            values.Select(v => v.Trim()).Where(v => v.Length > 0).Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());
}
