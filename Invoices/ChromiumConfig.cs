namespace Invoices;

/// <summary>
/// Configuration for headless Chromium used by InvoicePdfExporter.
/// Chromium is downloaded at build time via the Cli project's BundleChromium target
/// and placed in the output directory under "chromium/" so no runtime download occurs.
/// The revision is BrowserFetcher.DefaultRevision, which is tied to the PuppeteerSharp
/// package version (currently 21.0.1). For reproducibility, pin the PuppeteerSharp
/// version in Invoices.csproj.
/// </summary>
public static class ChromiumConfig
{
    /// <summary>
    /// Subdirectory name (relative to app base) where bundled Chromium is stored.
    /// </summary>
    public const string ChromiumSubdir = "chromium";
}
