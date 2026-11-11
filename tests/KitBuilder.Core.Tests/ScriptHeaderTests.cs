using KitBuilder.Core.Kit;

namespace KitBuilder.Core.Tests;

public class ScriptHeaderTests
{
    [Fact]
    public void Reads_the_first_sentence_and_the_important_line()
    {
        var header = ScriptHeader.Parse([
            "# Connects to the Wi-Fi network from the Settings tab, so apps can",
            "# install at first sign-in. More detail here.",
            "#",
            "# Important: Without it, computers with no cable cannot install apps.",
            "#",
            "# Technical notes.",
            "$x = 1;",
        ]);

        Assert.Equal("Connects to the Wi-Fi network from the Settings tab, so apps can install at first sign-in.", header.Description);
        Assert.Equal("Without it, computers with no cable cannot install apps.", header.Important);
    }

    [Fact]
    public void Skips_the_registry_file_header()
    {
        var header = ScriptHeader.Parse([
            "Windows Registry Editor Version 5.00", "", "; Turns off notifications.", ";", "[HKEY_CURRENT_USER\\x]",
        ]);
        Assert.Equal("Turns off notifications.", header.Description);
        Assert.Null(header.Important);
    }

    [Fact]
    public void Reads_rem_comments_in_command_files()
    {
        Assert.Equal("Deletes C:\\Windows.old if it is empty.",
            ScriptHeader.Parse(["@rem Deletes C:\\Windows.old if it is empty.", "@rem", "rmdir C:\\Windows.old"]).Description);
    }

    [Fact]
    public void A_script_that_starts_with_code_has_no_description()
    {
        Assert.Equal(ScriptHeader.None, ScriptHeader.Parse(["$tasks = @(", "# not a header", ")"]));
    }

    [Fact]
    public void Binary_files_are_not_read()
    {
        using var kit = new TestKit();
        Assert.Equal(ScriptHeader.None, ScriptHeader.Read(kit.PathOf("CustomAssets", "admin.png")));
        Assert.Equal("Says gamma.", ScriptHeader.Read(kit.PathOf("Scripts", "specialize", "30-gamma.ps1")).Description);
    }
}
