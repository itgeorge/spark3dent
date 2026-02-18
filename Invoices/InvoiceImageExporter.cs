using PuppeteerSharp;

namespace Invoices;

/// <summary>
/// Exports invoices to PNG using a headless Chromium browser to render the HTML template.
/// When <paramref name="chromiumExecutablePath"/> is provided, uses that Chromium binary
/// (e.g. bundled alongside the app). When null, PuppeteerSharp uses its default
/// (downloads to user cache on first run).
/// </summary>
public class InvoiceImageExporter : IInvoiceExporter
{
    private readonly string? _chromiumExecutablePath;

    /// <param name="chromiumExecutablePath">Optional path to Chromium executable. When null, uses PuppeteerSharp default (may download on first run).</param>
    public InvoiceImageExporter(string? chromiumExecutablePath = null)
    {
        _chromiumExecutablePath = chromiumExecutablePath;
    }

    public async Task<Stream> Export(InvoiceHtmlTemplate template, Invoice invoice)
    {
        var html = template.Render(invoice);

        var launchOptions = new LaunchOptions
        {
            Headless = true,
            Args = ["--no-sandbox", "--disable-setuid-sandbox"]
        };
        if (!string.IsNullOrEmpty(_chromiumExecutablePath) && File.Exists(_chromiumExecutablePath))
        {
            launchOptions.ExecutablePath = _chromiumExecutablePath;
        }

        await using var browser = await Puppeteer.LaunchAsync(launchOptions);
        await using var page = await browser.NewPageAsync();

        await page.SetContentAsync(html, new NavigationOptions
        {
            WaitUntil = new[] { WaitUntilNavigation.Networkidle0 }
        });

        var screenshotOptions = new ScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        };
        var pngBytes = await page.ScreenshotDataAsync(screenshotOptions);

        return new MemoryStream(pngBytes);
    }
}
