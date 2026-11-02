using System.Text.Json.Nodes;
using KitBuilder.Core.Kit;

namespace KitBuilder.Core.Tests;

public class KitSettingsTests
{
    [Fact]
    public void Reads_the_fixture_values()
    {
        using var kit = new TestKit();
        var settings = KitSettings.Load(kit.Kit);

        Assert.Equal("Windows 11 Pro", settings.Edition);
        Assert.Equal("Mountain Standard Time", settings.TimeZone);
        Assert.Equal("en-US", settings.UiLanguage);
        Assert.Equal("0409:00000409", settings.InputLocale);
        Assert.True(settings.WifiEnabled);
        Assert.Equal("PremierTech Client", settings.WifiSsid);
        Assert.True(settings.WifiHidden);
        Assert.Equal("WPA3SAE", settings.WifiAuthentication);
        Assert.Equal("fixture-admin", settings.AdminPassword);
        Assert.Contains("Microsoft.BingNews", settings.RemoveApps);
        Assert.DoesNotContain(settings.RemoveCapabilities, c => c.StartsWith("Hello.Face"));
        Assert.Equal(3, settings.StartPins.Count);
        Assert.Equal(new StartPin("packagedAppId",
            "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel"), settings.StartPins[2]);
    }

    [Fact]
    public void Saving_without_changes_rewrites_identical_bytes()
    {
        // The app must not churn a kit's files just by opening and saving them.
        using var kit = new TestKit();
        var before = File.ReadAllBytes(kit.Kit.SettingsPath);
        var secretsBefore = File.ReadAllBytes(kit.Kit.SecretsPath);

        KitSettings.Load(kit.Kit).Save();

        Assert.Equal(before, File.ReadAllBytes(kit.Kit.SettingsPath));
        Assert.Equal(secretsBefore, File.ReadAllBytes(kit.Kit.SecretsPath));
    }

    [Fact]
    public void Save_keeps_keys_the_app_does_not_model_and_writes_crlf()
    {
        using var kit = new TestKit();
        var json = JsonNode.Parse(File.ReadAllText(kit.Kit.SettingsPath))!.AsObject();
        json["printerName"] = "Front desk";
        json["wifi"]!["band"] = "5GHz";
        File.WriteAllText(kit.Kit.SettingsPath, json.ToJsonString());

        var settings = KitSettings.Load(kit.Kit);
        settings.Edition = "Windows 11 Enterprise";
        settings.WifiSsid = "Other";
        settings.RemoveFeatures = ["Recall", "  ", " MediaPlayback "];
        settings.StartPins = [new StartPin("desktopAppLink", @"%APPDATA%\x.lnk")];
        settings.Save();

        var text = File.ReadAllText(kit.Kit.SettingsPath);
        Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
        Assert.False(File.ReadAllBytes(kit.Kit.SettingsPath).AsSpan().StartsWith("﻿"u8), "no BOM");

        var saved = JsonNode.Parse(text)!.AsObject();
        Assert.Equal("Front desk", (string?)saved["printerName"]);
        Assert.Equal("5GHz", (string?)saved["wifi"]!["band"]);
        Assert.Equal("Windows 11 Enterprise", (string?)saved["edition"]);
        Assert.Equal(new[] { "Recall", "MediaPlayback" }, saved["removeFeatures"]!.AsArray().Select(n => (string?)n));
        Assert.Equal(@"%APPDATA%\x.lnk", (string?)saved["startPins"]![0]!["desktopAppLink"]);
        // Kept readable: no \u escapes for characters like + or '.
        Assert.Contains("\"schemaVersion\": 1", text);
    }

    [Fact]
    public void Missing_secrets_file_is_created_on_save()
    {
        using var kit = new TestKit();
        File.Delete(kit.Kit.SecretsPath);

        var settings = KitSettings.Load(kit.Kit);
        Assert.False(settings.HasSecretsFile);
        Assert.Equal("", settings.AdminPassword);

        settings.AdminPassword = "p&ss<word>";
        settings.Save();

        Assert.Equal("p&ss<word>", (string?)JsonNode.Parse(File.ReadAllText(kit.Kit.SecretsPath))!["adminPassword"]);
    }

    [Fact]
    public void Detects_an_edit_made_outside_the_app()
    {
        using var kit = new TestKit();
        var settings = KitSettings.Load(kit.Kit);
        Assert.False(settings.ChangedOnDisk());

        File.SetLastWriteTimeUtc(kit.Kit.SettingsPath, DateTime.UtcNow.AddMinutes(5));
        Assert.True(settings.ChangedOnDisk());

        settings.Save();
        Assert.False(settings.ChangedOnDisk());
    }
}
