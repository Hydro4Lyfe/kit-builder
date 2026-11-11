using System.Text.Json.Nodes;
using KitBuilder.App.ViewModels;
using KitBuilder.Core.Kit;
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
        var news = s.RemoveApps.Items.Single(i => i.Id == "Microsoft.BingNews");
        Assert.Equal("News", news.Name);
        Assert.True(news.IsRemoved);
        Assert.False(s.RemoveApps.Items.Single(i => i.Name == "Calculator").IsRemoved);
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
        s.RemoveFeatures.Items.Single(i => i.Id == "MicrosoftWindowsPowerShellV2Root").IsRemoved = false;
        s.Pins[0].Value = @"%APPDATA%\x.lnk";
        Assert.True(s.IsDirty);

        await s.SaveCommand.ExecuteAsync(null);

        Assert.False(s.IsDirty);
        var settings = JsonNode.Parse(File.ReadAllText(h.Kit.PathOf("settings.json")))!;
        Assert.Equal("Windows 11 Enterprise", (string?)settings["edition"]);
        Assert.Equal(new[] { "MediaPlayback", "Recall" }, settings["removeFeatures"]!.AsArray().Select(n => (string?)n));
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
    public async Task New_script_is_created_described_and_opened_in_notepad()
    {
        using var h = await new Harness().OpenAsync();
        var useronce = h.Main.Scripts!.Phases.Single(p => p.Name == "useronce");
        useronce.OpenNewScriptCommand.Execute(null);
        Assert.True(useronce.IsNewScriptOpen);
        Assert.False(useronce.CreateScriptCommand.CanExecute(null));

        useronce.NewKind = PhaseViewModel.Kinds.Single(k => k.Kind == NewScriptKind.Registry);
        useronce.NewName = "Dark mode";
        useronce.NewDescription = "Switches apps and Windows to dark mode";
        useronce.CreateScriptCommand.Execute(null);

        var row = useronce.Items.Single(r => r.FileName == "20-dark-mode.reg");
        Assert.Equal("Switches apps and Windows to dark mode.", row.Description);
        // Edited (Notepad), never opened with the default action, which would merge a .reg into this PC.
        Assert.EndsWith("20-dark-mode.reg", h.Desktop.Edited.Single());
        Assert.Empty(h.Desktop.Opened);
        Assert.False(useronce.IsNewScriptOpen);
        Assert.Equal("", useronce.NewName);
    }

    [Fact]
    public async Task New_script_defaults_to_registry_for_new_user_defaults()
    {
        using var h = await new Harness().OpenAsync();
        var phases = h.Main.Scripts!.Phases;

        phases.Single(p => p.Name == "defaultuser").OpenNewScriptCommand.Execute(null);
        phases.Single(p => p.Name == "firstlogon").OpenNewScriptCommand.Execute(null);

        Assert.Equal(NewScriptKind.Registry, phases.Single(p => p.Name == "defaultuser").NewKind.Kind);
        Assert.Equal(NewScriptKind.PowerShell, phases.Single(p => p.Name == "firstlogon").NewKind.Kind);
    }
}

public class PlainLanguageTests
{
    [Fact]
    public async Task Phases_and_items_use_plain_names_with_the_folder_names_secondary()
    {
        using var h = await new Harness().OpenAsync();
        var phases = h.Main.Scripts!.Phases;

        Assert.Equal(new[] { "Setting up the computer", "Defaults for new users", "First sign-in", "Each user's first sign-in" },
            phases.Select(p => p.DisplayName));
        Assert.Equal(@"Scripts\specialize", phases[0].FolderLabel);

        var gamma = phases[0].Items.Single(r => r.FileName == "30-gamma.ps1");
        Assert.Equal("Gamma", gamma.Title);
        Assert.Equal("Says gamma.", gamma.Description);
        Assert.Equal("Without it, nothing says gamma.", gamma.Important);
        Assert.Equal("PowerShell script", gamma.TypeLabel);

        var reg = phases[0].Items.Single(r => r.FileName == "20-system-tweaks.reg");
        Assert.Equal("System tweaks", reg.Title);
        Assert.Equal("Registry settings", reg.TypeLabel);
        Assert.Equal("Supporting file", phases[0].Items.Single(r => r.IsHelper).TypeLabel);
    }

    [Theory]
    [InlineData("xps-and-edge-search.ps1", "XPS and Edge search")]
    [InlineData("wifi.ps1", "Wi-Fi")]
    [InlineData("_WorldNationalParks.deskthemepack", "WorldNationalParks")]
    [InlineData("admin-profile-picture.ps1", "Administrator profile picture")]
    [InlineData("onedrive-and-permissions.ps1", "OneDrive and permissions")]
    public void Titles_are_readable(string fileTitle, string expected) =>
        Assert.Equal(expected, ViewModels.ScriptRowViewModel.FriendlyTitle(fileTitle));

    [Fact]
    public async Task Switching_off_an_important_item_asks_and_can_be_cancelled()
    {
        using var h = await new Harness().OpenAsync();
        var specialize = h.Main.Scripts!.Phases[0];
        var gamma = specialize.Items.Single(r => r.FileName == "30-gamma.ps1");
        h.Dialogs.ConfirmAnswer = false;
        var notified = false;
        gamma.PropertyChanged += (_, e) => notified |= e.PropertyName == "IsOn";

        gamma.IsOn = false;

        Assert.Contains("Without it, nothing says gamma.", h.Dialogs.Confirmations.Single());
        Assert.True(File.Exists(h.Kit.PathOf("Scripts", "specialize", "30-gamma.ps1")));
        Assert.True(gamma.IsOn);
        Assert.True(notified, "the switch is told to flip back");

        h.Dialogs.ConfirmAnswer = true;
        gamma.IsOn = false;
        Assert.True(File.Exists(h.Kit.PathOf("Scripts", "specialize", "_disabled", "30-gamma.ps1")));
    }

    [Fact]
    public async Task Ordinary_items_switch_off_without_asking()
    {
        using var h = await new Harness().OpenAsync();
        h.Main.Scripts!.Phases[0].Items.Single(r => r.FileName == "10-alpha.ps1").IsOn = false;
        Assert.Empty(h.Dialogs.Confirmations);
    }

    [Fact]
    public async Task Driver_folders_use_plain_names()
    {
        using var h = await new Harness().OpenAsync();
        var folders = h.Main.Drivers!.Folders;

        Assert.Equal(new[] { "All computers", "Dell Latitude 5450", "Needed by Windows Setup" }, folders.Select(f => f.DisplayName));
        Assert.Equal("No drivers yet", folders[0].Summary);
        Assert.StartsWith("1 driver, ", folders[1].Summary);
        Assert.Equal(@"Drivers\_Common", folders[0].FolderLabel);
    }

    [Fact]
    public async Task Pin_types_show_as_shortcut_or_store_app()
    {
        using var h = await new Harness().OpenAsync();
        var pins = h.Main.Settings!.Pins;

        Assert.Equal("Shortcut", pins[0].KindLabel);
        Assert.Equal("Store app", pins[2].KindLabel);

        pins[0].KindLabel = "Store app";
        Assert.Equal("packagedAppId", pins[0].Kind);
        Assert.True(h.Main.Settings.IsDirty);
    }
}

public class RemovalListTests
{
    [Fact]
    public void Shows_known_names_offers_extras_unticked_and_keeps_unknown_ids()
    {
        var changes = 0;
        var list = new RemovalListViewModel(KitBuilder.Core.Kit.RemovalCatalog.Apps,
            ["Microsoft.BingWeather", "Contoso.Toolbar"], () => changes++);

        Assert.True(list.Items.Single(i => i.Name == "Weather").IsRemoved);
        Assert.False(list.Items.Single(i => i.Name == "Camera").IsRemoved);
        var unknown = list.Items.Single(i => i.Id == "Contoso.Toolbar");
        Assert.True(unknown.IsRemoved);
        Assert.Equal("Added by name", unknown.Detail);
        Assert.Equal(0, changes);
        Assert.StartsWith("2 of ", list.Summary);
    }

    [Fact]
    public void Saving_keeps_the_original_order_and_appends_new_ticks()
    {
        var list = new RemovalListViewModel(KitBuilder.Core.Kit.RemovalCatalog.Apps,
            ["Microsoft.ZuneVideo", "Microsoft.BingWeather", "Microsoft.BingNews"], () => { });

        list.Items.Single(i => i.Name == "Weather").IsRemoved = false;
        list.Items.Single(i => i.Name == "Camera").IsRemoved = true;

        Assert.Equal(new[] { "Microsoft.ZuneVideo", "Microsoft.BingNews", "Microsoft.WindowsCamera" }, list.RemovedIds());
    }

    [Fact]
    public void Search_and_tick_all_shown_work_on_the_filtered_items()
    {
        var list = new RemovalListViewModel(KitBuilder.Core.Kit.RemovalCatalog.Apps, [], () => { });

        list.Filter = "xbox";
        Assert.All(list.Visible, i => Assert.Contains("Xbox", i.Name));
        list.TickAllCommand.Execute(null);

        // Matches on the name as well as the ID: "Xbox" is Microsoft.GamingApp.
        Assert.Contains("Microsoft.GamingApp", list.RemovedIds());
        Assert.Equal(list.Visible.Select(i => i.Id).Order(), list.RemovedIds().Order());
    }

    [Fact]
    public void Adding_an_unlisted_id_ticks_it_and_marks_settings_dirty()
    {
        var changes = 0;
        var list = new RemovalListViewModel(KitBuilder.Core.Kit.RemovalCatalog.Apps, [], () => changes++);
        list.Filter = "zzz";

        list.NewId = " Contoso.Toolbar ";
        list.AddCommand.Execute(null);

        Assert.Equal(new[] { "Contoso.Toolbar" }, list.RemovedIds());
        Assert.Equal("", list.Filter);
        Assert.Contains(list.Visible, i => i.Id == "Contoso.Toolbar");
        Assert.True(changes > 0);
    }

    [Fact]
    public async Task Ticking_in_settings_marks_the_form_dirty()
    {
        using var h = await new Harness().OpenAsync();
        h.Main.Settings!.RemoveApps.Items.Single(i => i.Name == "Calculator").IsRemoved = true;
        Assert.True(h.Main.Settings.IsDirty);
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
        Assert.Contains("already a model called", drivers.Message);
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
