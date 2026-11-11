using KitBuilder.Core.Kit;

namespace KitBuilder.Core.Tests;

public class ScriptInventoryTests
{
    [Fact]
    public void Lists_runnable_helper_and_disabled_files_in_run_order()
    {
        using var kit = new TestKit();
        var scripts = new ScriptInventory(kit.Kit).List("specialize");

        Assert.Equal(
            new[] { "10-alpha.ps1", "20-system-tweaks.reg", "30-gamma.ps1", "_LayoutModification.xml", "40-delta.ps1" },
            scripts.Select(s => s.FileName));
        Assert.Equal(
            new[] { ScriptState.Enabled, ScriptState.Enabled, ScriptState.Enabled, ScriptState.Helper, ScriptState.Disabled },
            scripts.Select(s => s.State));
        Assert.Equal(10, scripts[0].Number);
        Assert.Equal("alpha.ps1", scripts[0].Title);
    }

    [Fact]
    public void Disable_and_enable_move_the_file_and_clean_up()
    {
        using var kit = new TestKit();
        var inventory = new ScriptInventory(kit.Kit);
        var alpha = inventory.List("specialize")[0];

        var off = inventory.Disable(alpha);
        Assert.True(File.Exists(kit.PathOf("Scripts", "specialize", "_disabled", "10-alpha.ps1")));
        Assert.False(File.Exists(alpha.FullPath));

        inventory.Enable(off);
        Assert.True(File.Exists(alpha.FullPath));

        // Re-enabling the fixture's own disabled script empties _disabled, which is then removed.
        inventory.Enable(inventory.List("specialize").Single(s => s.State == ScriptState.Disabled));
        Assert.False(Directory.Exists(kit.PathOf("Scripts", "specialize", "_disabled")));
    }

    [Fact]
    public void Helper_files_cannot_be_toggled()
    {
        using var kit = new TestKit();
        var inventory = new ScriptInventory(kit.Kit);
        var helper = inventory.List("specialize").Single(s => s.State == ScriptState.Helper);
        Assert.Throws<InvalidOperationException>(() => inventory.Disable(helper));
    }

    [Fact]
    public void Swap_exchanges_only_the_two_number_prefixes()
    {
        using var kit = new TestKit();
        var inventory = new ScriptInventory(kit.Kit);
        var list = inventory.List("specialize");

        inventory.SwapOrder(list[0], list[2]);

        Assert.Equal(
            new[] { "10-gamma.ps1", "20-system-tweaks.reg", "30-alpha.ps1" },
            inventory.List("specialize").Where(s => s.State == ScriptState.Enabled).Select(s => s.FileName));
        Assert.Contains("alpha", File.ReadAllText(kit.PathOf("Scripts", "specialize", "30-alpha.ps1")));
    }

    [Fact]
    public void Swap_refuses_scripts_without_a_number()
    {
        using var kit = new TestKit();
        File.WriteAllText(kit.PathOf("Scripts", "specialize", "unnumbered.ps1"), "");
        var inventory = new ScriptInventory(kit.Kit);
        var list = inventory.List("specialize");

        var e = Assert.Throws<InvalidOperationException>(() =>
            inventory.SwapOrder(list.Single(s => s.FileName == "unnumbered.ps1"), list[0]));
        Assert.Contains("start with a number", e.Message);
    }

    [Fact]
    public void Added_files_are_numbered_after_every_existing_script_including_disabled()
    {
        using var kit = new TestKit();
        var inventory = new ScriptInventory(kit.Kit);
        var source = Path.Combine(Directory.CreateTempSubdirectory().FullName, "05-Set-Printer.ps1");
        File.WriteAllText(source, "Write-Output 'printer';");

        var added = inventory.AddFile("specialize", source);

        // Highest is the disabled 40-delta, so 50; the source's own 05- prefix is dropped.
        Assert.Equal("50-Set-Printer.ps1", added.FileName);
        Assert.Equal("Write-Output 'printer';", File.ReadAllText(added.FullPath));
    }

    [Fact]
    public void Adding_an_unsupported_file_type_is_refused()
    {
        using var kit = new TestKit();
        var source = Path.Combine(Directory.CreateTempSubdirectory().FullName, "notes.txt");
        File.WriteAllText(source, "");
        Assert.Throws<InvalidOperationException>(() => new ScriptInventory(kit.Kit).AddFile("specialize", source));
    }

    [Fact]
    public void New_powershell_script_has_the_header_the_app_reads()
    {
        using var kit = new TestKit();
        var script = new ScriptInventory(kit.Kit).NewScript("firstlogon", NewScriptKind.PowerShell,
            "Set the default printer!", "Makes the front-desk printer the default", "Without it, nobody can print.");

        Assert.Equal("20-set-the-default-printer.ps1", script.FileName);
        var header = ScriptHeader.Read(script.FullPath);
        Assert.Equal("Makes the front-desk printer the default.", header.Description);
        Assert.Equal("Without it, nobody can print.", header.Important);
        var text = File.ReadAllText(script.FullPath);
        Assert.Contains("never 'exit'", text);
        Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
    }

    [Fact]
    public void New_registry_file_uses_the_right_hive_and_falls_back_to_the_name()
    {
        using var kit = new TestKit();
        var reg = new ScriptInventory(kit.Kit).NewScript("defaultuser", NewScriptKind.Registry, "Hide the Copilot button");

        Assert.Equal("20-hide-the-copilot-button.reg", reg.FileName);
        var text = File.ReadAllText(reg.FullPath);
        Assert.StartsWith("Windows Registry Editor Version 5.00\r\n\r\n; Hide the Copilot button.\r\n", text);
        Assert.Contains(@"; [HKEY_USERS\DefaultUser\Software\Example]", text);
        Assert.Null(ScriptHeader.Read(reg.FullPath).Important);
    }

    [Fact]
    public void Non_ascii_text_gets_a_bom_in_powershell_but_never_in_command_files()
    {
        using var kit = new TestKit();
        var inventory = new ScriptInventory(kit.Kit);

        var ps1 = inventory.NewScript("specialize", NewScriptKind.PowerShell, "Café printer");
        var cmd = inventory.NewScript("specialize", NewScriptKind.Command, "Map drives", "Maps the S: drive — for staff");

        Assert.True(File.ReadAllBytes(ps1.FullPath).AsSpan().StartsWith("\uFEFF"u8));
        Assert.False(File.ReadAllBytes(cmd.FullPath).AsSpan().StartsWith("\uFEFF"u8));
        Assert.All(File.ReadAllBytes(cmd.FullPath), b => Assert.True(b < 128));
    }

    [Fact]
    public void A_new_script_needs_a_name()
    {
        using var kit = new TestKit();
        Assert.Throws<InvalidOperationException>(() =>
            new ScriptInventory(kit.Kit).NewScript("specialize", NewScriptKind.PowerShell, "  !! "));
    }

    [PwshFact]
    public async Task Every_template_passes_the_kits_own_check()
    {
        using var kit = new TestKit();
        var inventory = new ScriptInventory(kit.Kit);
        foreach (var phase in ScriptInventory.Phases)
            foreach (var kind in Enum.GetValues<NewScriptKind>())
                inventory.NewScript(phase, kind, $"Template {kind}", "Does a thing.", "Without it, a thing breaks.");

        var result = await new KitBuilder.Core.Tools.KitTools(kit.Kit, new KitBuilder.Core.Tools.PowerShellRunner()).CheckAsync();

        Assert.Empty(result.Errors);
    }
}
