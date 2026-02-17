// Build-time Chromium downloader. Called by the Cli build to bundle Chromium into the output directory
// so the app does not need to download it at runtime. Uses the default revision for the
// PuppeteerSharp package version (reproducibility: pin PuppeteerSharp in package references).
using PuppeteerSharp;

var outputPath = args.Length > 0 ? args[0] : Environment.CurrentDirectory;
var options = new BrowserFetcherOptions { Path = outputPath };
var fetcher = new BrowserFetcher(options);
var installed = await fetcher.DownloadAsync();
Console.WriteLine($"Chromium build {installed.BuildId} downloaded to {outputPath}");
Console.WriteLine($"Executable: {installed.GetExecutablePath()}");
