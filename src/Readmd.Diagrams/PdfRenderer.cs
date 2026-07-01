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
    /// Renders <paramref name="html"/> to a PDF that matches the on-screen rendering: the document's
    /// own theme background (dark stays dark), full colour, and a single continuous page sized to the
    /// content rather than reflowed to A4. Throws <see cref="PdfNotProvisionedException"/> if the
    /// required tooling isn't available; call <see cref="PdfProvisioning.EnsureInstalled"/> first.
    /// </summary>
    /// <param name="html">The self-contained document HTML.</param>
    /// <param name="contentWidthPx">
    /// Target content width in CSS pixels (the width the document is laid out at, mirroring the
    /// browser view). Defaults to 1000.
    /// </param>
    public static async Task<byte[]> RenderAsync(string html, int contentWidthPx = 1000, CancellationToken ct = default)
    {
        var readiness = PdfProvisioning.CheckReadiness();
        if (!readiness.IsReady)
            throw new PdfNotProvisionedException(readiness);

        // A sensible clamp: too narrow breaks layout, too wide wastes space.
        int width = Math.Clamp(contentWidthPx, 360, 2400);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = ["--no-sandbox", "--disable-gpu"],
        });
        var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            // Lay the document out at the target content width, exactly like the on-screen view.
            ViewportSize = new ViewportSize { Width = width, Height = 1200 },
            DeviceScaleFactor = 2, // crisp text/diagrams
        });
        try
        {
            // Render using SCREEN styles, not print. This is what preserves the theme: the export
            // HTML's `@media print { background:#fff }` rule (and any other print overrides) must not
            // apply, so a dark document stays dark with its light/colourful text.
            await page.EmulateMediaAsync(new PageEmulateMediaOptions { Media = Media.Screen });

            await page.SetContentAsync(html, new PageSetContentOptions { WaitUntil = WaitUntilState.NetworkIdle });
            // Give client-side mermaid/KaTeX a moment to finish rendering before measuring/snapshotting.
            await page.WaitForTimeoutAsync(400);

            // Prepare the DOM for a single-page, un-clipped capture:
            //  - paint the theme background behind the whole page,
            //  - drop the content max-width so it can be as wide as its widest element,
            //  - un-clip horizontally-scrolling blocks (wide tables and code use overflow:auto,
            //    which would crop in a static PDF) and let tables size to their natural width.
            // The page is grown to fit afterwards, so nothing is cut off.
            await page.EvaluateAsync(@"() => {
                const de = document.documentElement, body = document.body;
                const bg = getComputedStyle(body).backgroundColor;
                if (bg) de.style.background = bg;
                de.style.margin = '0'; body.style.margin = '0';

                const content = document.getElementById('readmd-content');
                if (content) { content.style.maxWidth = 'none'; content.style.width = 'max-content'; }

                // Wide tables render as display:block;overflow:auto on screen (so they scroll); in a
                // PDF that clips. Make them lay out fully instead.
                document.querySelectorAll('#readmd-content table').forEach(t => {
                    t.style.display = 'table';
                    t.style.width = 'auto';
                    t.style.maxWidth = 'none';
                    t.style.overflow = 'visible';
                });
                document.querySelectorAll('#readmd-content pre').forEach(p => {
                    p.style.overflow = 'visible';
                });
                // Wrapping cell text isn't cropped, but keep cells from forcing absurd widths.
                document.querySelectorAll('#readmd-content td, #readmd-content th').forEach(c => {
                    c.style.whiteSpace = 'normal';
                });
            }");
            // Let the browser reflow after the style changes.
            await page.WaitForTimeoutAsync(50);

            // Measure the full rendered content size (including any element that overflows the body)
            // so the PDF is one continuous page that fits everything with no A4 pagination or cropping.
            var fullHeight = await page.EvaluateAsync<double>(@"() => {
                const b = document.body, e = document.documentElement;
                let h = Math.max(b.scrollHeight, e.scrollHeight, b.offsetHeight, e.offsetHeight);
                // Account for any child that extends past the body (defensive).
                document.querySelectorAll('#readmd-content *').forEach(el => {
                    const r = el.getBoundingClientRect();
                    if (r.bottom > h) h = r.bottom;
                });
                return Math.ceil(h);
            }");
            var fullWidth = await page.EvaluateAsync<double>(@"() => {
                const b = document.body, e = document.documentElement;
                let w = Math.max(b.scrollWidth, e.scrollWidth, b.offsetWidth, e.offsetWidth);
                // Widest element wins, so a wide table isn't cropped even if it overflows the body.
                document.querySelectorAll('#readmd-content *').forEach(el => {
                    if (el.scrollWidth) {
                        const r = el.getBoundingClientRect();
                        const right = r.left + el.scrollWidth;
                        if (right > w) w = right;
                    }
                });
                return Math.ceil(w);
            }");

            // Add a small buffer so sub-pixel rounding in Chromium's paginator doesn't spill a
            // sliver onto a second page — we want exactly one continuous page.
            int pageW = Math.Max(width, (int)Math.Ceiling(fullWidth)) + 2;
            int pageH = Math.Max(1, (int)Math.Ceiling(fullHeight)) + 2;

            return await page.PdfAsync(new PagePdfOptions
            {
                // Custom single page sized to the content (CSS px -> inches at 96dpi). No margins,
                // so the theme background reaches the edges just like on screen.
                Width = pageW + "px",
                Height = pageH + "px",
                PrintBackground = true,
                Margin = new Margin { Top = "0", Bottom = "0", Left = "0", Right = "0" },
                Scale = 1,
            });
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
