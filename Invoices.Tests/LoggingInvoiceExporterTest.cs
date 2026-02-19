using System;
using System.IO;
using System.Threading.Tasks;
using Invoices;
using NUnit.Framework;
using Utilities;
using Utilities.Tests;

namespace Invoices.Tests;

[TestFixture]
[TestOf(typeof(LoggingInvoiceExporter))]
public class LoggingInvoiceExporterTest
{
    [Test]
    public async Task Export_WhenWrappingFake_ThenDelegatesAndReturnsStream()
    {
        var template = await InvoiceHtmlTemplate.LoadAsync(new BgAmountTranscriber());
        var inner = new FakeExporter();
        var logger = new CapturingLogger();
        var sut = new LoggingInvoiceExporter(inner, logger);

        var invoice = CreateTestInvoice();
        var stream = await sut.Export(template, invoice);

        Assert.That(stream, Is.Not.Null);
        Assert.That(stream.Length, Is.GreaterThan(0));
        Assert.That(stream.CanRead, Is.True);
        Assert.That(logger.InfoMessages, Does.Contain("InvoiceExporter.Export invoiceNumber=1"));
        Assert.That(logger.InfoMessages, Does.Contain("InvoiceExporter.Export completed invoiceNumber=1"));
    }

    [Test]
    public async Task Export_WhenInnerThrows_ThenPropagatesExceptionAndLogsError()
    {
        var template = await InvoiceHtmlTemplate.LoadAsync(new BgAmountTranscriber());
        var inner = new ThrowingExporter();
        var logger = new CapturingLogger();
        var sut = new LoggingInvoiceExporter(inner, logger);

        var invoice = CreateTestInvoice();
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await sut.Export(template, invoice));
        Assert.That(ex!.Message, Is.EqualTo("Exporter failed"));
        Assert.That(logger.InfoMessages, Does.Contain("InvoiceExporter.Export invoiceNumber=1"));
        Assert.That(logger.ErrorEntries, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Export_WhenLoggerThrows_ThenOperationSucceedsAnyway()
    {
        var template = await InvoiceHtmlTemplate.LoadAsync(new BgAmountTranscriber());
        var inner = new FakeExporter();
        var logger = new ThrowingLogger();
        var sut = new LoggingInvoiceExporter(inner, logger);

        var invoice = CreateTestInvoice();
        var stream = await sut.Export(template, invoice);

        Assert.That(stream, Is.Not.Null);
        Assert.That(stream.Length, Is.GreaterThan(0));
    }

    private static Invoice CreateTestInvoice() => new("1", new Invoice.InvoiceContent(
        Date: DateTime.UtcNow,
        SellerAddress: new BillingAddress("S", "R", "1", null, "A", "C", "1", "BG"),
        BuyerAddress: new BillingAddress("B", "R", "2", null, "A", "C", "1", "BG"),
        LineItems: [new Invoice.LineItem("Item", new Amount(100_00, Currency.Eur))],
        BankTransferInfo: new BankTransferInfo("BG00", "Bank", "BIC")));

    private sealed class FakeExporter : IInvoiceExporter
    {
        public string MimeType => "application/pdf";

        public Task<Stream> Export(InvoiceHtmlTemplate template, Invoice invoice)
        {
            var ms = new MemoryStream();
            ms.Write(new byte[] { 0x25, 0x50, 0x44, 0x46 }); // %PDF
            ms.Position = 0;
            return Task.FromResult<Stream>(ms);
        }
    }

    private sealed class ThrowingExporter : IInvoiceExporter
    {
        public string MimeType => "application/pdf";

        public Task<Stream> Export(InvoiceHtmlTemplate template, Invoice invoice) =>
            throw new InvalidOperationException("Exporter failed");
    }
}
