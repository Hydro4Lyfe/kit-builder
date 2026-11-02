using KitBuilder.Core.Kit;

namespace KitBuilder.Core.Tests;

public class KitFolderTests
{
    [Fact]
    public void Opens_a_kit_and_exposes_its_paths()
    {
        using var kit = new TestKit();
        Assert.Equal(kit.PathOf("tools", "Build-Kit.ps1"), kit.Kit.BuildKitScript);
        Assert.Equal(kit.PathOf("$WinPEDriver$"), kit.Kit.WinPeDriversRoot);
    }

    [Fact]
    public void Rejects_a_folder_that_is_not_a_kit()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var e = Assert.Throws<KitFolderException>(() => KitFolder.Open(dir));
            Assert.Contains("not a deployment kit", e.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Rejects_a_newer_schema()
    {
        using var kit = new TestKit();
        var text = File.ReadAllText(kit.Kit.SettingsPath).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2");
        File.WriteAllText(kit.Kit.SettingsPath, text);

        var e = Assert.Throws<KitFolderException>(() => KitFolder.Open(kit.Root));
        Assert.Contains("Update PT Kit Builder", e.Message);
    }

    [Fact]
    public void Rejects_a_kit_without_its_tools()
    {
        using var kit = new TestKit();
        File.Delete(kit.Kit.TestKitScript);
        Assert.Throws<KitFolderException>(() => KitFolder.Open(kit.Root));
    }

    [Fact]
    public void Finds_the_kit_from_inside_it()
    {
        using var kit = new TestKit();
        Assert.Equal(kit.Root, KitFolder.FindNear(kit.PathOf("Scripts", "specialize")));
    }
}
