using System.IO.Compression;
using KitBuilder.Core.Kit;

namespace KitBuilder.Core.Tests;

public class DriverLibraryTests
{
    [Fact]
    public void Lists_common_then_models_then_winpe()
    {
        using var kit = new TestKit();
        var folders = new DriverLibrary(kit.Kit).List();

        Assert.Equal(new[] { "_Common", "Dell Latitude 5450", "$WinPEDriver$" }, folders.Select(f => f.Name));
        var dell = folders[1];
        Assert.Equal(DriverFolderKind.Model, dell.Kind);
        Assert.Equal(new[] { "Latitude 5450" }, dell.MatchLines);
        Assert.Equal(1, dell.InfCount);
    }

    [Fact]
    public void Add_model_writes_a_clean_match_file()
    {
        using var kit = new TestKit();
        var model = new DriverLibrary(kit.Kit).AddModel(" Lenovo ThinkPad T14 G4 ", ["21HD*", "", " ThinkPad T14 Gen 4 ", "21hd*"]);

        Assert.Equal("Lenovo ThinkPad T14 G4", model.Name);
        Assert.Equal("21HD*\r\nThinkPad T14 Gen 4\r\n", File.ReadAllText(Path.Combine(model.FullPath, "match.txt")));
    }

    [Theory]
    [InlineData("", "21HD*")]
    [InlineData("Bad:Name", "21HD*")]
    [InlineData("_Hidden", "21HD*")]
    [InlineData("Dell Latitude 5450", "x")]
    [InlineData("No lines", "  ")]
    public void Add_model_rejects_bad_input(string name, string line)
    {
        using var kit = new TestKit();
        Assert.Throws<InvalidOperationException>(() => new DriverLibrary(kit.Kit).AddModel(name, [line]));
    }

    [Fact]
    public async Task Imports_a_folder_and_a_zip_into_subfolders()
    {
        using var kit = new TestKit();
        var library = new DriverLibrary(kit.Kit);
        var model = library.AddModel("HP EliteBook 840", ["HP EliteBook 840 G10"]);

        var source = Directory.CreateTempSubdirectory().FullName;
        var pack = Path.Combine(source, "Audio");
        Directory.CreateDirectory(Path.Combine(pack, "x64"));
        File.WriteAllText(Path.Combine(pack, "x64", "audio.inf"), "; inf");
        var zip = Path.Combine(source, "Network.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            archive.CreateEntry("wifi/wifi.inf");

        await library.ImportPackAsync(model, pack);
        var result = await library.ImportPackAsync(model, zip);

        Assert.True(File.Exists(Path.Combine(model.FullPath, "Audio", "x64", "audio.inf")));
        Assert.True(File.Exists(Path.Combine(model.FullPath, "Network", "wifi", "wifi.inf")));
        Assert.Equal(2, result.InfCount);
    }

    [Fact]
    public async Task Import_explains_how_to_handle_an_exe_pack()
    {
        using var kit = new TestKit();
        var library = new DriverLibrary(kit.Kit);
        var exe = Path.Combine(Directory.CreateTempSubdirectory().FullName, "pack.exe");
        File.WriteAllText(exe, "");

        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => library.ImportPackAsync(library.List()[0], exe));
        Assert.Contains("extracted first", e.Message);
    }

    [Fact]
    public async Task Import_warns_when_the_pack_has_no_inf_files()
    {
        using var kit = new TestKit();
        var library = new DriverLibrary(kit.Kit);
        var pack = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(pack, "setup.exe"), "");

        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => library.ImportPackAsync(library.List()[0], pack));
        Assert.Contains("no .inf files", e.Message);
    }

    [Fact]
    public void Remove_deletes_models_but_never_common_or_winpe()
    {
        using var kit = new TestKit();
        var library = new DriverLibrary(kit.Kit);
        var folders = library.List();

        Assert.Throws<InvalidOperationException>(() => library.RemoveModel(folders[0]));
        Assert.Throws<InvalidOperationException>(() => library.RemoveModel(folders[2]));
        library.RemoveModel(folders[1]);
        Assert.False(Directory.Exists(folders[1].FullPath));
    }
}
