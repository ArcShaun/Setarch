using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace SetArch.Core.Output;

public sealed record FillResult(string File, bool Success, int Changes, string Message);

/// <summary>
/// Runs AutoCAD's command-line engine (accoreconsole.exe, installed with full AutoCAD and its verticals)
/// to fill placeholders in drawings without opening the AutoCAD window.
/// </summary>
public sealed class AutoCadConsole
{
    public string ExePath { get; }
    public TimeSpan TimeoutPerFile { get; init; } = TimeSpan.FromMinutes(3);

    public AutoCadConsole(string exePath) => ExePath = exePath;

    /// <summary>Finds the newest accoreconsole.exe under Program Files\Autodesk, or null.</summary>
    public static string? Detect()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetEnvironmentVariable("ProgramW6432"),
        }.Where(r => !string.IsNullOrEmpty(r)).Distinct();

        foreach (var root in roots)
        {
            var autodesk = Path.Combine(root!, "Autodesk");
            if (!Directory.Exists(autodesk)) continue;
            var found = Directory.EnumerateDirectories(autodesk, "AutoCAD*")
                .Where(d => !Path.GetFileName(d).Contains("LT", StringComparison.OrdinalIgnoreCase))
                .Select(d => Path.Combine(d, "accoreconsole.exe"))
                .Where(File.Exists)
                .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (found is not null) return found;
        }
        return null;
    }

    public async Task<List<FillResult>> FillAsync(
        IReadOnlyList<string> drawings,
        IReadOnlyDictionary<string, string> values,
        string prefix,
        string suffix,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        var results = new List<FillResult>();
        if (drawings.Count == 0) return results;

        var scriptPath = Path.Combine(Path.GetTempPath(), $"setarch-fill-{Guid.NewGuid():N}.scr");
        await File.WriteAllTextAsync(scriptPath, FillScript.ConsoleScript(values, prefix, suffix), Encoding.ASCII, ct);
        try
        {
            foreach (var dwg in drawings)
            {
                ct.ThrowIfCancellationRequested();
                log?.Invoke($"Filling text: {Path.GetFileName(dwg)} ...");
                var r = await RunAsync(dwg, scriptPath, ct);
                results.Add(r);
                log?.Invoke(r.Success ? $"  {r.Message}" : $"  Problem: {r.Message}");
            }
        }
        finally
        {
            try { File.Delete(scriptPath); } catch (IOException) { }
        }
        return results;
    }

    private async Task<FillResult> RunAsync(string dwg, string script, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(ExePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            // accoreconsole writes UTF-16; Latin1 + dropping NULs reads both that and plain ASCII.
            StandardOutputEncoding = Encoding.Latin1,
            WorkingDirectory = Path.GetDirectoryName(dwg)!,
        };
        psi.ArgumentList.Add("/i");
        psi.ArgumentList.Add(dwg);
        psi.ArgumentList.Add("/s");
        psi.ArgumentList.Add(script);

        using var process = new Process { StartInfo = psi };
        process.Start();
        process.StandardInput.Close(); // never let it wait for keyboard input
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        _ = process.StandardError.ReadToEndAsync(ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeoutPerFile);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            ct.ThrowIfCancellationRequested();
            return new FillResult(dwg, false, 0, "AutoCAD took too long and was stopped. Open the drawing and check it.");
        }

        var output = (await stdout).Replace("\0", "");
        return Interpret(dwg, output, process.ExitCode);
    }

    public static FillResult Interpret(string dwg, string output, int exitCode)
    {
        var m = Regex.Match(output, FillScript.ResultMarker + @"\s+(\d+)");
        if (!m.Success)
        {
            var tail = string.Join(" ", output.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).TakeLast(3));
            return new FillResult(dwg, false, 0, $"AutoCAD did not finish (exit code {exitCode}). {tail}".Trim());
        }

        var changes = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return new FillResult(dwg, true, changes,
            changes == 0 ? "No placeholders found." : $"{changes} text item(s) filled in.");
    }
}
