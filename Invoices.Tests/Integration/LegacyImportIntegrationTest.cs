using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Invoices;
using NUnit.Framework;

namespace Invoices.Tests.Integration;

/// <summary>
/// Integration tests for LegacyPdfParser using real PDF files.
/// Uses Invoices/test-data/legacy-invoices/ by default (gitignored).
/// Each PDF must have a matching .expected.txt file (same path, .pdf replaced by .expected.txt)
/// with key=value lines: Number, Date, TotalCents, Currency, Recipient.Name, Recipient.CompanyIdentifier.
/// Tests are skipped if the folder does not exist or no PDFs have expected files.
/// </summary>
[TestFixture]
[TestOf(typeof(LegacyPdfParser))]
public class LegacyImportIntegrationTest
{
    private static string TestPath =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "..", "..", "..", "..", "Invoices", "test-data", "legacy-invoices"));

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        if (!Directory.Exists(TestPath))
        {
            Assert.Ignore($"Folder does not exist: {TestPath}. Copy sample PDFs there to run.");
        }
    }

    [Test]
    public void TryParse_WhenGivenRealPdf_ThenExtractsInvoiceData()
    {
        var pdfs = Directory.GetFiles(TestPath, "*.pdf", SearchOption.AllDirectories);
        var tested = 0;
        foreach (var path in pdfs)
        {
            var expectedPath = Path.ChangeExtension(path, ".expected.txt");
            if (!File.Exists(expectedPath))
                continue;

            var result = LegacyPdfParser.TryParse(path);
            Assert.That(result, Is.Not.Null, $"Failed to parse {path}");

            var expected = ParseExpectedFile(expectedPath);
            foreach (var (key, value) in expected)
            {
                AssertField(path, result!, key, value);
            }
            tested++;
        }

        if (tested == 0)
        {
            Assert.Ignore($"No PDF files with .expected.txt found in {TestPath}. Add PDFs and matching .expected.txt files.");
        }
    }

    private static Dictionary<string, string> ParseExpectedFile(string path)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                continue;
            var eq = trimmed.IndexOf('=');
            if (eq < 0) continue;
            var key = trimmed[..eq].Trim();
            var value = trimmed[(eq + 1)..].Trim();
            dict[key] = value;
        }
        return dict;
    }

    private static void AssertField(string pdfPath, LegacyInvoiceData result, string key, string expected)
    {
        var ctx = $" in {Path.GetFileName(pdfPath)}";
        switch (key)
        {
            case "Number":
                Assert.That(result.Number, Is.EqualTo(expected), $"Number{ctx}");
                break;
            case "Date":
                var parsed = DateTime.TryParse(expected, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d);
                Assert.That(parsed, Is.True, $"Date format '{expected}'{ctx}");
                Assert.That(result.Date.Date, Is.EqualTo(d.Date), $"Date{ctx}");
                break;
            case "TotalCents":
                Assert.That(int.TryParse(expected, out var cents), Is.True, $"TotalCents format '{expected}'{ctx}");
                Assert.That(result.TotalCents, Is.EqualTo(cents), $"TotalCents{ctx}");
                break;
            case "Currency":
                Assert.That(Enum.TryParse<Currency>(expected, ignoreCase: true, out var curr), Is.True, $"Currency '{expected}'{ctx}");
                Assert.That(result.Currency, Is.EqualTo(curr), $"Currency{ctx}");
                break;
            case "Recipient.Name":
                Assert.That(result.Recipient.Name, Is.EqualTo(expected), $"Recipient.Name{ctx}");
                break;
            case "Recipient.CompanyIdentifier":
                Assert.That(result.Recipient.CompanyIdentifier, Is.EqualTo(expected), $"Recipient.CompanyIdentifier{ctx}");
                break;
            default:
                Assert.Fail($"Unknown expected key: {key}{ctx}");
                break;
        }
    }
}
