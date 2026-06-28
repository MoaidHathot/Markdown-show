using System.Diagnostics;
using Readmd.Core;

namespace Readmd.Diagrams;

/// <summary>
/// Renders mermaid diagrams by shelling out to a local <c>mmdc</c> (mermaid-cli) that produces SVG,
/// which is then rasterized to PNG in-process. This is a lighter alternative to the bundled
/// Playwright/Chromium path: when <c>mmdc</c> is on PATH (or configured), no headless browser
/// download is needed.
/// </summary>
internal sealed class MermaidCliRenderer
{
    private readonly string _mmdcPath;

    public MermaidCliRenderer(string? mmdcPath = null) => _mmdcPath = mmdcPath ?? "mmdc";

    public bool IsAvailable()
    {
        try
        {
            var psi = ExecutableResolver.Resolve(_mmdcPath, ["--version"]);
            if (psi is null) return false;
            using var proc = Process.Start(psi);
            if (proc is null) return false;
            proc.WaitForExit(8000);
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }

    public async Task<DiagramResult> RenderAsync(DiagramRequest request, DiagramTheme theme, CancellationToken ct)
    {
        var dir = Path.Combine(Path.GetTempPath(), "readmd-mmdc", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var input = Path.Combine(dir, "in.mmd");
        var output = Path.Combine(dir, "out.svg");
        var configFile = Path.Combine(dir, "config.json");
        try
        {
            await File.WriteAllTextAsync(input, request.Source, ct);
            // Use native SVG <text> labels (htmlLabels:false): the in-process SVG rasterizer doesn't
            // render <foreignObject> HTML, so HTML labels would come out blank.
            await File.WriteAllTextAsync(configFile, MermaidTheme.ConfigJson(theme == DiagramTheme.Dark, htmlLabels: false), ct);

            await RunMmdcAsync(input, output, configFile, ct);
            if (!File.Exists(output))
                return DiagramResult.Fail(request.Key, "mmdc produced no output.");

            var svg = await File.ReadAllTextAsync(output, ct);
            var png = Rasterizer.SvgToPng(svg, out var width, out var height);
            return new DiagramResult(request.Key, DiagramStatus.Ready, png, svg, width, height, null);
        }
        catch (MmdcNotFoundException)
        {
            return DiagramResult.Fail(request.Key, $"mmdc executable not found ('{_mmdcPath}').");
        }
        catch (OperationCanceledException)
        {
            return DiagramResult.Fail(request.Key, "Mermaid (mmdc) render timed out.");
        }
        catch (Exception ex)
        {
            return DiagramResult.Fail(request.Key, "Mermaid (mmdc) render failed: " + ex.Message);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    private async Task RunMmdcAsync(string input, string output, string configFile, CancellationToken ct)
    {
        var psi = ExecutableResolver.Resolve(_mmdcPath,
            ["-i", input, "-o", output, "-c", configFile, "-b", "transparent"])
            ?? throw new MmdcNotFoundException();

        using var proc = new Process { StartInfo = psi };
        try { proc.Start(); }
        catch (System.ComponentModel.Win32Exception) { throw new MmdcNotFoundException(); }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(45));
        var linked = timeoutCts.Token;
        try
        {
            proc.StandardInput.Close();
            var stderrTask = proc.StandardError.ReadToEndAsync(linked);
            await proc.StandardOutput.ReadToEndAsync(linked);
            await proc.WaitForExitAsync(linked);
            if (proc.ExitCode != 0)
            {
                var err = await stderrTask;
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(err) ? "mmdc exited with an error." : err.Trim());
            }
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw;
        }
    }
}

/// <summary>Signals that the configured mmdc executable could not be started (not found on PATH).</summary>
internal sealed class MmdcNotFoundException : Exception;
