using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace KitBuilder.Core.Tools;

/// <summary>Runs a kit tool script (tools\*.ps1) and streams its output.</summary>
public interface IPowerShellRunner
{
    /// <summary>Runs <paramref name="script"/>; every stdout/stderr line goes to <paramref name="onLine"/>. Returns the exit code.</summary>
    Task<int> RunAsync(string script, IReadOnlyList<string> arguments, Action<string> onLine, CancellationToken ct = default);
}

/// <summary>
/// Runs scripts with Windows PowerShell 5.1 on Windows (what technicians run by hand, so the
/// app exercises the same code path) and with pwsh elsewhere (development under WSL).
/// </summary>
public sealed partial class PowerShellRunner : IPowerShellRunner
{
    public async Task<int> RunAsync(string script, IReadOnlyList<string> arguments, Action<string> onLine, CancellationToken ct = default)
    {
        var info = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(script)!,
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        if (OperatingSystem.IsWindows())
        {
            // The kit may sit on a share or have come from a download; Bypass avoids
            // execution-policy prompts the technician would otherwise have to deal with.
            info.ArgumentList.Add("-ExecutionPolicy");
            info.ArgumentList.Add("Bypass");
        }
        info.ArgumentList.Add("-File");
        info.ArgumentList.Add(script);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {info.FileName}.");
        }
        catch (Win32Exception e)
        {
            throw new InvalidOperationException(
                $"Could not start {info.FileName}: {e.Message}." +
                (OperatingSystem.IsWindows() ? "" : " Install PowerShell 7 (pwsh) to build from Linux."), e);
        }

        using (process)
        {
            var stdout = Pump(process.StandardOutput, onLine, ct);
            var stderr = Pump(process.StandardError, onLine, ct);
            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }
            await Task.WhenAll(stdout, stderr);
            return process.ExitCode;
        }
    }

    private static async Task Pump(StreamReader reader, Action<string> onLine, CancellationToken ct)
    {
        while (await reader.ReadLineAsync(ct) is { } line)
            onLine(AnsiEscape().Replace(line, ""));
    }

    // pwsh colours warnings and errors with ANSI codes; they are noise in a log pane.
    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]")]
    private static partial Regex AnsiEscape();
}
