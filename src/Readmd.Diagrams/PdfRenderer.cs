using Microsoft.Playwright;

namespace Readmd.Diagrams;

/// <summary>
/// Thrown when PDF export cannot run because its tooling isn't provisioned (no Node.js runtime, or
/// the headless browser hasn't been downloaded). Carries the specific <see cref="PdfReadiness"/> so
/// callers can prompt the user to install or fall back to browser printing.
/// </summary>
public sealed class PdfNotProvisionedException(PdfProvisionResult result)
    : Exception(result.Message ?? "PDF export is not available.")
{
    public PdfReadiness Readiness => result.Readiness;
}

/// <summary>
/// Renders a self-contained HTML string to a PDF using a headless Chromium (via Playwright). The
/// published tool ships only the small Playwright JS driver, so this uses a Node.js runtime found on
/// the machine (see <see cref="PdfProvisioning"/>) and downloads Chromium on demand. Intended for
/// one-shot export, so the browser is launched and torn down per call.
/// </summary>
public static class PdfRenderer
{
    /// <summary>
    /// Renders <paramref name="html"/> to PDF bytes (A4, background graphics on). Throws
    /// <see cref="PdfNotProvisionedException"/> if the required tooling isn't available; call
    /// <see cref="PdfProvisioning.EnsureInstalled"/> first to provision it.
    /// </summary>
    public static async Task<byte[]> RenderAsync(string html, CancellationToken ct = default)
    {
        var readiness = PdfProvisioning.CheckReadiness();
        if (!readiness.IsReady)
            throw new PdfNotProvisionedException(readiness);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = ["--no-sandbox", "--disable-gpu"],
        });
        var page = await browser.NewPageAsync();
        try
        {
            await page.SetContentAsync(html, new PageSetContentOptions { WaitUntil = WaitUntilState.NetworkIdle });
            // Give client-side mermaid/KaTeX a moment to finish rendering before snapshotting.
            await page.WaitForTimeoutAsync(400);
            return await page.PdfAsync(new PagePdfOptions
            {
                Format = "A4",
                PrintBackground = true,
                Margin = new Margin { Top = "1.5cm", Bottom = "1.5cm", Left = "1.2cm", Right = "1.2cm" },
            });
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
