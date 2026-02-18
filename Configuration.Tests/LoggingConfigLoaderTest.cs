using System.Threading.Tasks;
using Configuration;
using NUnit.Framework;
using Utilities;
using Utilities.Tests;

namespace Configuration.Tests;

[TestFixture]
[TestOf(typeof(LoggingConfigLoader))]
public class LoggingConfigLoaderTest
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LoggingConfigLoaderTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public async Task LoadAsync_WhenWrappingJsonLoader_ThenDelegatesAndReturnsConfig()
    {
        WriteAppSettings("""{"App":{"StartInvoiceNumber":42},"Desktop":{}}""");
        var inner = new JsonAppSettingsLoader(_tempDir);
        var logger = new CapturingLogger();
        var sut = new LoggingConfigLoader(inner, logger);

        var config = await sut.LoadAsync();

        Assert.That(config.App.StartInvoiceNumber, Is.EqualTo(42));
        Assert.That(logger.InfoMessages, Does.Contain("ConfigLoader.LoadAsync"));
        Assert.That(logger.InfoMessages, Does.Contain("ConfigLoader.LoadAsync completed"));
    }

    [Test]
    public void LoadAsync_WhenConfigFileMissing_ThenPropagatesExceptionAndLogsError()
    {
        var inner = new JsonAppSettingsLoader(_tempDir);
        var logger = new CapturingLogger();
        var sut = new LoggingConfigLoader(inner, logger);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await sut.LoadAsync());
        Assert.That(ex!.Message, Does.Contain("not found"));
        Assert.That(logger.InfoMessages, Does.Contain("ConfigLoader.LoadAsync"));
        Assert.That(logger.ErrorEntries, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task LoadAsync_WhenLoggerThrows_ThenOperationSucceedsAnyway()
    {
        WriteAppSettings("""{"App":{"StartInvoiceNumber":99},"Desktop":{}}""");
        var inner = new JsonAppSettingsLoader(_tempDir);
        var logger = new ThrowingLogger();
        var sut = new LoggingConfigLoader(inner, logger);

        var config = await sut.LoadAsync();

        Assert.That(config.App.StartInvoiceNumber, Is.EqualTo(99));
    }

    private void WriteAppSettings(string content) =>
        File.WriteAllText(Path.Combine(_tempDir, "appsettings.json"), content);
}
