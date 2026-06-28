using System.Diagnostics;
using Readmd.Core;

namespace Readmd.Diagrams;

/// <summary>
/// Renders mermaid diagrams by shelling out to a local <c>mmdc</c> (mermaid-cli), which uses its
/// own bundled headless browser to produce a PNG. This is a lighter alternative to readmd's own
/// Playwright/Chromium download: when <c>mmdc</c> is on PATH (or configured), no extra browser is
/// fetched. We let mmdc emit PNG directly (rather than SVG that we'd rasterize in-process) so the
/// text, label centering, and gantt grid lines are rendered by a real browser and look identical to
/// the browser front-end — the in-process SVG rasterizer mishandles mermaid's tspan/em positioning
/// and <c>currentColor</c>.
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
        var output = Path.Combine(dir, "out.png");
        var configFile = Path.Combine(dir, "config.json");
        try
        {
            await File.WriteAllTextAsync(input, request.Source, ct);
            // Keep htmlLabels:true — mmdc's browser renders the HTML labels natively, so we get
            // correctly centered/wrapped text and visible gantt grid lines.
            await File.WriteAllTextAsync(configFile, MermaidTheme.ConfigJson(theme == DiagramTheme.Dark), ct);

            await RunMmdcAsync(input, output, configFile, ct);
            if (!File.Exists(output))
                return DiagramResult.Fail(request.Key, "mmdc produced no output.");

            var png = await File.ReadAllBytesAsync(output, ct);
            var (width, height) = ImageInfo.GetPngSize(png);
            return new DiagramResult(request.Key, DiagramStatus.Ready, png, null, width, height, null);
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
        // -s 2 renders at 2x for crisp text when scaled into the terminal; -b transparent keeps the
        // diagram background see-through so the theme background shows behind it.
        var psi = ExecutableResolver.Resolve(_mmdcPath,
            ["-i", input, "-o", output, "-c", configFile, "-b", "transparent", "-s", "2"])
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
