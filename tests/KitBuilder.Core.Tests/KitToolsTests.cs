using KitBuilder.Core.Tools;

namespace KitBuilder.Core.Tests;

/// <summary>
/// Runs the fixture kit's real tools\Test-Kit.ps1 and Build-Kit.ps1 (copies of the kit's), so
/// these double as a contract test: if the kit's tools change their output, these fail.
/// </summary>
public class KitToolsTests
{
    [PwshFact]
    public async Task Check_reports_the_fixture_warning_and_no_errors()
    {
        using var kit = new TestKit();
        var result = await new KitTools(kit.Kit, new PowerShellRunner()).CheckAsync();

        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, w => w.Contains("deskthemepack"));
    }

    [PwshFact]
    public async Task Check_reports_a_broken_script()
    {
        using var kit = new TestKit();
        File.WriteAllText(kit.PathOf("Scripts", "specialize", "60-broken.ps1"), "if( {\r\n");

        var result = await new KitTools(kit.Kit, new PowerShellRunner()).CheckAsync();

        Assert.Contains(result.Errors, e => e.Contains("60-broken.ps1"));
    }

    [PwshFact]
    public async Task Build_makes_an_iso()
    {
        if (!PwshFactAttribute.OnPath("xorriso") && !OperatingSystem.IsWindows()) return;
        using var kit = new TestKit();
        var iso = kit.PathOf("out", "test.iso");
        var lines = new List<string>();

        var ok = await new KitTools(kit.Kit, new PowerShellRunner()).BuildAsync(new BuildTarget.Iso(iso), lines.Add);

        Assert.True(ok, string.Join("\n", lines));
        Assert.True(new FileInfo(iso).Length > 0);
        Assert.Contains(lines, l => l.Contains("Built"));
    }

    [Fact]
    public async Task Check_reads_a_lone_string_as_a_one_item_list()
    {
        // Windows PowerShell 5.1's ConvertTo-Json can flatten a one-item array.
        using var kit = new TestKit();
        var runner = new FakeRunner("""{ "errors": "only one", "warnings": [] }""");

        var result = await new KitTools(kit.Kit, runner).CheckAsync();

        Assert.Equal(new[] { "only one" }, result.Errors);
        Assert.Equal(new[] { "-Format", "Json" }, runner.LastArguments);
    }

    [Fact]
    public async Task Check_reports_when_the_tool_prints_no_json()
    {
        using var kit = new TestKit();
        var result = await new KitTools(kit.Kit, new FakeRunner("something went wrong")).CheckAsync();

        Assert.True(result.HasErrors);
        Assert.Contains("something went wrong", result.Errors[0]);
    }

    [Fact]
    public async Task Build_passes_the_target_to_build_kit()
    {
        using var kit = new TestKit();
        var runner = new FakeRunner("");

        await new KitTools(kit.Kit, runner).BuildAsync(new BuildTarget.Usb("E:"), _ => { });

        Assert.Equal(kit.Kit.BuildKitScript, runner.LastScript);
        Assert.Equal(new[] { "-UsbDrive", "E:" }, runner.LastArguments);
    }

    private sealed class FakeRunner(string output, int exitCode = 0) : IPowerShellRunner
    {
        public string? LastScript { get; private set; }
        public IReadOnlyList<string>? LastArguments { get; private set; }

        public Task<int> RunAsync(string script, IReadOnlyList<string> arguments, Action<string> onLine, CancellationToken ct = default)
        {
            LastScript = script;
            LastArguments = arguments;
            foreach (var line in output.Split('\n')) onLine(line);
            return Task.FromResult(exitCode);
        }
    }
}
