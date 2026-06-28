using Microsoft.Playwright;

namespace Readmd.Diagrams;

/// <summary>
/// Renders a self-contained HTML string to a PDF using the same headless Chromium
/// (via Playwright) that mermaid rendering uses. Intended for one-shot export, so the browser is
/// launched and torn down per call.
/// </summary>
public static class PdfRenderer
{
    /// <summary>Renders <paramref name="html"/> to PDF bytes (A4, background graphics on).</summary>
    public static async Task<byte[]> RenderAsync(string html, CancellationToken ct = default)
    {
        EnsureChromiumInstalled();

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

    private static void EnsureChromiumInstalled()
    {
        var exit = Program.Main(["install", "chromium"]);
        if (exit != 0)
            throw new InvalidOperationException(
                "Failed to install the headless browser used to render PDFs. " +
                "Ensure you have network access on first run.");
    }
}
