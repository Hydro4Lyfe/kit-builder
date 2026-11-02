using System.Text.Json.Nodes;
using KitBuilder.Core.Tools;

namespace KitBuilder.App.Tests;

public class MainWindowViewModelTests
{
    [Fact]
    public async Task Opening_a_kit_remembers_it_and_runs_the_check()
    {
        using var h = await new Harness().OpenAsync();

        Assert.Equal(h.Kit.Root, h.Prefs.Prefs.LastKitPath);
        Assert.Equal(1, h.Runner.CheckCount);
        Assert.True(h.Main.IsReady);
        Assert.Equal("Ready to build", h.Main.CheckSummary);
        Assert.NotNull(h.Main.Settings);
        Assert.Contains(h.Kit.Root, h.Main.Title);
    }

    [Fact]
    public async Task A_folder_that_is_not_a_kit_shows_why()
    {
        using var h = new Harness();
        await h.Main.OpenAsync(Path.GetTempPath());

        Assert.False(h.Main.IsKitOpen);
        Assert.Contains("not a deployment kit", h.Main.OpenError);
    }

    [Fact]
    public async Task Startup_reopens_the_last_kit()
    {
        using var h = new Harness();
        h.Prefs.Prefs.LastKitPath = h.Kit.Root;

        await h.Main.StartAsync();

        Assert.True(h.Main.IsKitOpen);
    }

    [Fact]
    public async Task Check_errors_are_listed()
    {
        using var h = new Harness();
        h.Runner.CheckJson = """{ "errors": ["settings.json: 'edition' is missing or empty."], "warnings": ["w"] }""";
        await h.OpenAsync();

        Assert.True(h.Main.HasErrors);
        Assert.Single(h.Main.Errors);
        Assert.Equal("1 problem(s) to fix before building", h.Main.CheckSummary);
    }

    [Fact]
    public async Task Browsing_away_from_unsaved_settings_asks_first()
    {
        using var h = await new Harness().OpenAsync();
        h.Main.Settings!.Edition = "Windows 11 Home";
        h.Dialogs.ConfirmAnswer = false;

        await h.Main.BrowseForKitCommand.ExecuteAsync(null);

        Assert.Single(h.Dialogs.Confirmations);
        Assert.Equal("Windows 11 Home", h.Main.Settings.Edition);
    }
}

public class SettingsViewModelTests
{
    [Fact]
    public async Task Loads_the_kit_values_and_starts_clean()
    {
        using var h = await new Harness().OpenAsync();
        var s = h.Main.Settings!;

        Assert.Equal("Windows 11 Pro", s.Edition);
        Assert.Equal("fixture-admin", s.AdminPassword);
        Assert.Contains("Microsoft.BingNews", s.RemoveAppsText);
        Assert.Equal(3, s.Pins.Count);
        Assert.False(s.IsDirty);
        Assert.False(s.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Save_writes_the_files_and_rechecks()
    {
        using var h = await new Harness().OpenAsync();
        var s = h.Main.Settings!;

        s.Edition = "Windows 11 Enterprise";
        s.WifiPassword = "new-wifi";
        s.RemoveFeaturesText = "Recall\n\n  MediaPlayback \n";
        s.Pins[0].Value = @"%APPDATA%\x.lnk";
        Assert.True(s.IsDirty);

        await s.SaveCommand.ExecuteAsync(null);

        Assert.False(s.IsDirty);
        var settings = JsonNode.Parse(File.ReadAllText(h.Kit.PathOf("settings.json")))!;
        Assert.Equal("Windows 11 Enterprise", (string?)settings["edition"]);
        Assert.Equal(new[] { "Recall", "MediaPlayback" }, settings["removeFeatures"]!.AsArray().Select(n => (string?)n));
        Assert.Equal(@"%APPDATA%\x.lnk", (string?)settings["startPins"]![0]!["desktopAppLink"]);
        Assert.Equal("new-wifi", (string?)JsonNode.Parse(File.ReadAllText(h.Kit.PathOf("secrets.json")))!["wifiPassword"]);
        Assert.Equal(2, h.Runner.CheckCount);
    }

    [Fact]
    public async Task Save_asks_before_overwriting_an_outside_edit()
    {
        using var h = await new Harness().OpenAsync();
        var s = h.Main.Settings!;
        s.Edition = "Windows 11 Home";
        File.SetLastWriteTimeUtc(h.Kit.PathOf("settings.json"), DateTime.UtcNow.AddMinutes(5));
        var before = File.ReadAllText(h.Kit.PathOf("settings.json"));
        h.Dialogs.ConfirmAnswer = false;

        await s.SaveCommand.ExecuteAsync(null);

        Assert.Single(h.Dialogs.Confirmations);
        Assert.True(s.IsDirty);
        Assert.Equal(before, File.ReadAllText(h.Kit.PathOf("settings.json")));
    }

    [Fact]
    public async Task Adding_and_removing_pins_marks_the_form_dirty()
    {
        using var h = await new Harness().OpenAsync();
        var s = h.Main.Settings!;

        s.Pins[1].RemoveCommand.Execute(null);

        Assert.Equal(2, s.Pins.Count);
        Assert.True(s.IsDirty);
    }

    [Fact]
    public async Task Reload_discards_edits()
    {
        using var h = await new Harness().OpenAsync();
        var s = h.Main.Settings!;
        s.Edition = "Windows 11 Home";

        await s.ReloadCommand.ExecuteAsync(null);

        Assert.Equal("Windows 11 Pro", s.Edition);
        Assert.False(s.IsDirty);
    }
}

public class ScriptsViewModelTests
{
    [Fact]
    public async Task Switching_a_script_off_moves_it_to_disabled()
    {
        using var h = await new Harness().OpenAsync();
        var scripts = h.Main.Scripts!;
        var specialize = scripts.Phases[0];
        var alpha = specialize.Items.Single(r => r.FileName == "10-alpha.ps1");

        alpha.IsOn = false;

        Assert.True(File.Exists(h.Kit.PathOf("Scripts", "specialize", "_disabled", "10-alpha.ps1")));
        Assert.False(specialize.Items.Single(r => r.FileName == "10-alpha.ps1").IsOn);
        Assert.Null(scripts.Error);
        Assert.Equal(2, h.Runner.CheckCount);
    }

    [Fact]
    public async Task Move_down_swaps_with_the_next_enabled_script()
    {
        using var h = await new Harness().OpenAsync();
        var specialize = h.Main.Scripts!.Phases[0];

        specialize.Items.Single(r => r.FileName == "10-alpha.ps1").MoveDownCommand.Execute(null);

        Assert.Equal(
            new[] { "10-system-tweaks.reg", "20-alpha.ps1", "30-gamma.ps1" },
            specialize.Items.Where(r => r.CanMove).Select(r => r.FileName));
    }

    [Fact]
    public async Task Helper_files_have_no_switch_or_order()
    {
        using var h = await new Harness().OpenAsync();
        var helper = h.Main.Scripts!.Phases[0].Items.Single(r => r.IsHelper);

        Assert.False(helper.CanToggle);
        Assert.False(helper.CanMove);
    }

    [Fact]
    public async Task Add_file_copies_the_picked_script_in()
    {
        using var h = await new Harness().OpenAsync();
        var source = Path.Combine(Directory.CreateTempSubdirectory().FullName, "Set-Printer.ps1");
        File.WriteAllText(source, "Write-Output 1;");
        h.Dialogs.Files.Enqueue(source);
        var firstlogon = h.Main.Scripts!.Phases.Single(p => p.Name == "firstlogon");

        await firstlogon.AddFileCommand.ExecuteAsync(null);

        Assert.Contains(firstlogon.Items, r => r.FileName == "20-Set-Printer.ps1");
    }

    [Fact]
    public async Task New_reg_file_is_created_and_opened()
    {
        using var h = await new Harness().OpenAsync();
        var useronce = h.Main.Scripts!.Phases.Single(p => p.Name == "useronce");
        Assert.False(useronce.NewRegCommand.CanExecute(null));

        useronce.NewRegName = "Dark mode";
        useronce.NewRegCommand.Execute(null);

        Assert.Contains(useronce.Items, r => r.FileName == "20-dark-mode.reg");
        Assert.EndsWith("20-dark-mode.reg", h.Desktop.Opened.Last());
        Assert.Equal("", useronce.NewRegName);
    }
}

public class DriversViewModelTests
{
    [Fact]
    public async Task Add_model_then_edit_its_match_lines()
    {
        using var h = await new Harness().OpenAsync();
        var drivers = h.Main.Drivers!;

        drivers.NewModelName = "Lenovo T14";
        drivers.NewModelMatchText = "21HD*\nThinkPad T14 Gen 4";
        drivers.AddModelCommand.Execute(null);

        var model = drivers.Folders.Single(f => f.Name == "Lenovo T14");
        Assert.False(drivers.MessageIsError);
        Assert.Equal("", drivers.NewModelName);

        model.MatchText = "21HD*";
        Assert.True(model.MatchChanged);
        model.SaveMatchCommand.Execute(null);

        Assert.Equal("21HD*\r\n", File.ReadAllText(h.Kit.PathOf("Drivers", "Lenovo T14", "match.txt")));
    }

    [Fact]
    public async Task A_bad_model_name_shows_an_error()
    {
        using var h = await new Harness().OpenAsync();
        var drivers = h.Main.Drivers!;

        drivers.NewModelName = "Dell Latitude 5450";
        drivers.NewModelMatchText = "x";
        drivers.AddModelCommand.Execute(null);

        Assert.True(drivers.MessageIsError);
        Assert.Contains("already exists", drivers.Message);
    }

    [Fact]
    public async Task Remove_asks_and_deletes()
    {
        using var h = await new Harness().OpenAsync();
        var drivers = h.Main.Drivers!;
        var dell = drivers.Folders.Single(f => f.Name == "Dell Latitude 5450");

        await dell.RemoveCommand.ExecuteAsync(null);

        Assert.Single(h.Dialogs.Confirmations);
        Assert.False(Directory.Exists(h.Kit.PathOf("Drivers", "Dell Latitude 5450")));
        Assert.DoesNotContain(drivers.Folders, f => f.Name == "Dell Latitude 5450");
    }

    [Fact]
    public async Task Import_reports_a_failure_inline()
    {
        using var h = await new Harness().OpenAsync();
        var drivers = h.Main.Drivers!;
        var exe = Path.Combine(Directory.CreateTempSubdirectory().FullName, "pack.exe");
        File.WriteAllText(exe, "");
        h.Dialogs.Files.Enqueue(exe);

        await drivers.Folders[0].ImportFileCommand.ExecuteAsync(null);

        Assert.True(drivers.MessageIsError);
        Assert.Contains("extracted first", drivers.Message);
        Assert.False(drivers.IsBusy);
    }

    [Fact]
    public async Task Copy_puts_the_model_query_on_the_clipboard()
    {
        using var h = await new Harness().OpenAsync();
        await h.Main.Drivers!.CopyModelQueryCommand.ExecuteAsync(null);
        Assert.Contains("Win32_ComputerSystem", h.Desktop.Copied);
    }
}

public class BuildViewModelTests
{
    private static readonly DriveChoice Usb = new("E:", "KIT", 32_000_000_000, IsRemovable: true, RecoveryKeyFiles: 0);
    private static readonly DriveChoice UsbWithKeys = new("F:", "OLD", 16_000_000_000, IsRemovable: true, RecoveryKeyFiles: 3);
    private static readonly DriveChoice FixedDisk = new("G:", "SSD", 500_000_000_000, IsRemovable: false, RecoveryKeyFiles: 0);

    [Fact]
    public async Task Builds_an_iso_and_shows_the_log()
    {
        using var h = await new Harness().OpenAsync();
        var build = h.Main.Build!;
        build.ToIso = true;

        await build.BuildCommand.ExecuteAsync(null);

        Assert.True(build.Succeeded);
        Assert.Contains("Built it", build.LogText);
        Assert.Equal(new[] { "-Iso", "-IsoPath", h.Kit.PathOf("out", "unattend.iso") }, h.Runner.BuildCalls.Single());
    }

    [Fact]
    public async Task Refuses_to_build_while_the_check_has_errors()
    {
        using var h = new Harness();
        h.Runner.CheckJson = """{ "errors": ["bad"], "warnings": [] }""";
        await h.OpenAsync();
        var build = h.Main.Build!;
        build.ToIso = true;

        await build.BuildCommand.ExecuteAsync(null);

        Assert.False(build.Succeeded);
        Assert.Contains("Overview", build.Result);
        Assert.Empty(h.Runner.BuildCalls);
    }

    [Fact]
    public async Task A_failed_build_says_so()
    {
        using var h = await new Harness().OpenAsync();
        h.Runner.BuildExitCode = 1;
        var build = h.Main.Build!;
        build.ToIso = true;

        await build.BuildCommand.ExecuteAsync(null);

        Assert.False(build.Succeeded);
        Assert.Contains("failed", build.Result);
    }

    [Fact]
    public async Task Usb_build_warns_about_recovery_keys_left_on_the_stick()
    {
        using var h = await new Harness(UsbWithKeys, Usb).OpenAsync();
        var build = h.Main.Build!;
        build.ToUsb = true;
        build.SelectedDrive = build.Drives.Single(d => d.Letter == "F:");
        Assert.Contains("3 BitLocker recovery key", build.RecoveryKeyWarning);
        h.Dialogs.ConfirmAnswer = false;

        await build.BuildCommand.ExecuteAsync(null);

        Assert.Single(h.Dialogs.Confirmations);
        Assert.Empty(h.Runner.BuildCalls);

        build.SelectedDrive = build.Drives.Single(d => d.Letter == "E:");
        await build.BuildCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "-UsbDrive", "E:" }, h.Runner.BuildCalls.Single());
    }

    [Fact]
    public async Task Fixed_drives_are_listed_only_on_request()
    {
        using var h = await new Harness(Usb, FixedDisk).OpenAsync();
        var build = h.Main.Build!;
        Assert.Single(build.Drives);

        build.IncludeFixedDrives = true;

        Assert.Equal(2, build.Drives.Count);
    }
}
