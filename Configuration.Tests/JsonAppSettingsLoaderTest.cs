using Configuration;
using NUnit.Framework;

namespace Configuration.Tests;

[TestFixture]
[TestOf(typeof(JsonAppSettingsLoader))]
public class JsonAppSettingsLoaderTest
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "JsonAppSettingsLoaderTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void WriteAppSettings(string content)
    {
        File.WriteAllText(Path.Combine(_tempDir, "appsettings.json"), content);
    }

    [Test]
    public async Task LoadAsync_GivenValidAppSettingsWithAllFields_WhenLoading_ThenReturnsCorrectConfig()
    {
        WriteAppSettings("""
            {
              "App": {
                "StartInvoiceNumber": 100,
                "SellerAddress": {
                  "Name": "Test Company",
                  "RepresentativeName": "John Doe",
                  "CompanyIdentifier": "123456789",
                  "VatIdentifier": "BG123456789",
                  "Address": "123 Main St",
                  "City": "Sofia",
                  "PostalCode": "1000",
                  "Country": "Bulgaria"
                }
              },
              "Desktop": {
                "DatabasePath": "/path/to/db",
                "BlobStoragePath": "/path/to/blobs",
                "LogDirectory": "/path/to/logs"
              }
            }
            """);

        var loader = new JsonAppSettingsLoader(_tempDir);
        var config = await loader.LoadAsync();

        Assert.That(config.App.StartInvoiceNumber, Is.EqualTo(100));
        Assert.That(config.App.SellerAddress, Is.Not.Null);
        Assert.That(config.App.SellerAddress!.Name, Is.EqualTo("Test Company"));
        Assert.That(config.App.SellerAddress!.RepresentativeName, Is.EqualTo("John Doe"));
        Assert.That(config.App.SellerAddress!.CompanyIdentifier, Is.EqualTo("123456789"));
        Assert.That(config.App.SellerAddress!.VatIdentifier, Is.EqualTo("BG123456789"));
        Assert.That(config.App.SellerAddress!.Address, Is.EqualTo("123 Main St"));
        Assert.That(config.App.SellerAddress!.City, Is.EqualTo("Sofia"));
        Assert.That(config.App.SellerAddress!.PostalCode, Is.EqualTo("1000"));
        Assert.That(config.App.SellerAddress!.Country, Is.EqualTo("Bulgaria"));
        Assert.That(config.Desktop, Is.Not.Null);
        Assert.That(config.Desktop!.DatabasePath, Is.EqualTo("/path/to/db"));
        Assert.That(config.Desktop!.BlobStoragePath, Is.EqualTo("/path/to/blobs"));
        Assert.That(config.Desktop!.LogDirectory, Is.EqualTo("/path/to/logs"));
    }

    [Test]
    public async Task LoadAsync_GivenAppSettingsWithMissingOptionalFields_WhenLoading_ThenUsesDefaults()
    {
        WriteAppSettings("{}");

        var loader = new JsonAppSettingsLoader(_tempDir);
        var config = await loader.LoadAsync();

        Assert.That(config.App.StartInvoiceNumber, Is.EqualTo(1));
        Assert.That(config.App.SellerAddress, Is.Null);
        Assert.That(config.Desktop, Is.Not.Null);
        Assert.That(config.Desktop!.DatabasePath, Is.Empty);
        Assert.That(config.Desktop!.BlobStoragePath, Is.Empty);
        Assert.That(config.Desktop!.LogDirectory, Is.Empty);
    }

    [Test]
    public void LoadAsync_GivenAppSettingsDoesNotExist_WhenLoading_ThenThrowsDescriptiveError()
    {
        var loader = new JsonAppSettingsLoader(_tempDir);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await loader.LoadAsync());

        Assert.That(ex!.Message, Does.Contain("appsettings.json").Or.Contain("not found").Or.Contain("exist"));
    }

    [Test]
    public async Task LoadAsync_GivenEnvironmentVariableOverride_WhenLoading_ThenEnvironmentOverridesJson()
    {
        WriteAppSettings("""
            {
              "App": {
                "StartInvoiceNumber": 42
              },
              "Desktop": {}
            }
            """);
        try
        {
            Environment.SetEnvironmentVariable("App__StartInvoiceNumber", "99");

            var loader = new JsonAppSettingsLoader(_tempDir);
            var config = await loader.LoadAsync();

            Assert.That(config.App.StartInvoiceNumber, Is.EqualTo(99));
        }
        finally
        {
            Environment.SetEnvironmentVariable("App__StartInvoiceNumber", null);
        }
    }

    [Test]
    public async Task LoadAsync_GivenAppSettingsWithSellerAddress_WhenLoading_ThenSellerAddressLoaded()
    {
        WriteAppSettings("""
            {
              "App": {
                "SellerAddress": {
                  "Name": "Dental Lab Ltd",
                  "RepresentativeName": "Jane Smith",
                  "CompanyIdentifier": "987654321",
                  "VatIdentifier": null,
                  "Address": "456 Oak Ave",
                  "City": "Plovdiv",
                  "PostalCode": "4000",
                  "Country": "Bulgaria"
                }
              },
              "Desktop": {}
            }
            """);

        var loader = new JsonAppSettingsLoader(_tempDir);
        var config = await loader.LoadAsync();

        Assert.That(config.App.SellerAddress, Is.Not.Null);
        Assert.That(config.App.SellerAddress!.Name, Is.EqualTo("Dental Lab Ltd"));
        Assert.That(string.IsNullOrEmpty(config.App.SellerAddress!.VatIdentifier), Is.True);
    }

    [Test]
    public async Task LoadAsync_GivenAppSettingsWithSellerAddressBg_WhenLoading_ThenSellerAddressLoaded()
    {
        WriteAppSettings("""
            {
              "App": {
                "SellerAddress": {
                  "Name": "Зъботехническа лаборатория ООД",
                  "RepresentativeName": "Иван Петров",
                  "CompanyIdentifier": "123456789",
                  "VatIdentifier": "BG123456789",
                  "Address": "ул. Граф Игнатиев 15",
                  "City": "София",
                  "PostalCode": "1000",
                  "Country": "България"
                }
              },
              "Desktop": {}
            }
            """);

        var loader = new JsonAppSettingsLoader(_tempDir);
        var config = await loader.LoadAsync();

        Assert.That(config.App.SellerAddress, Is.Not.Null);
        Assert.That(config.App.SellerAddress!.Name, Is.EqualTo("Зъботехническа лаборатория ООД"));
        Assert.That(config.App.SellerAddress!.RepresentativeName, Is.EqualTo("Иван Петров"));
        Assert.That(config.App.SellerAddress!.City, Is.EqualTo("София"));
        Assert.That(config.App.SellerAddress!.Country, Is.EqualTo("България"));
    }

}
