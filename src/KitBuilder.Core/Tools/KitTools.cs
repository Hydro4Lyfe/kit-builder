using System.Text.Json;
using KitBuilder.Core.Kit;

namespace KitBuilder.Core.Tools;

/// <summary>The result of tools\Test-Kit.ps1.</summary>
public sealed record KitCheckResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool HasErrors => Errors.Count > 0;

    /// <summary>A result for when the check itself could not run.</summary>
    public static KitCheckResult Failed(string message) => new([message], []);
}

/// <summary>Where Build-Kit.ps1 should put the kit: a USB drive or an ISO file.</summary>
public abstract record BuildTarget
{
    public sealed record Usb(string DriveLetter) : BuildTarget;
    public sealed record Iso(string Path) : BuildTarget;
}

/// <summary>
/// Calls the kit's own tools rather than reimplementing them, so the scripts remain the one
/// implementation of checking and building and keep working without this app.
/// </summary>
public sealed class KitTools(KitFolder kit, IPowerShellRunner runner)
{
    /// <summary>Runs Test-Kit.ps1 -Format Json and parses its report.</summary>
    public async Task<KitCheckResult> CheckAsync(CancellationToken ct = default)
    {
        var output = new List<string>();
        int exitCode;
        try
        {
            // -KitRoot explicitly: the kit's own default relies on $PSScriptRoot, which older
            // copies of Test-Kit.ps1 read too early under Windows PowerShell 5.1.
            exitCode = await runner.RunAsync(kit.TestKitScript, ["-KitRoot", kit.Root, "-Format", "Json"],
                line => { lock (output) output.Add(line); }, ct);
        }
        catch (InvalidOperationException e)
        {
            return KitCheckResult.Failed(e.Message);
        }

        var text = string.Join("\n", output);
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end < start)
            return KitCheckResult.Failed($"Test-Kit.ps1 did not report results (exit code {exitCode}). Output:\n{text}");

        try
        {
            using var doc = JsonDocument.Parse(text[start..(end + 1)]);
            return new KitCheckResult(ReadList(doc.RootElement, "errors"), ReadList(doc.RootElement, "warnings"));
        }
        catch (JsonException e)
        {
            return KitCheckResult.Failed($"Could not read Test-Kit.ps1's report: {e.Message}");
        }
    }

    /// <summary>Runs Build-Kit.ps1 for <paramref name="target"/>, streaming its output. Returns true on success.</summary>
    public async Task<bool> BuildAsync(BuildTarget target, Action<string> onLine, CancellationToken ct = default)
    {
        string[] arguments = target switch
        {
            BuildTarget.Usb usb => ["-UsbDrive", usb.DriveLetter],
            BuildTarget.Iso iso => ["-Iso", "-IsoPath", iso.Path],
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
        return await runner.RunAsync(kit.BuildKitScript, arguments, onLine, ct) == 0;
    }

    // ConvertTo-Json in Windows PowerShell 5.1 can emit a lone string where a one-item array was meant.
    private static List<string> ReadList(JsonElement root, string name) =>
        !root.TryGetProperty(name, out var value) ? []
        : value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(e => e.ToString()).ToList()
        : value.ValueKind == JsonValueKind.String ? [value.GetString()!]
        : [];
}
