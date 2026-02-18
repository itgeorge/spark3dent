using System;
using System.Threading.Tasks;
using Invoices;
using Invoices.Tests.Fakes;
using NUnit.Framework;
using Utilities;
using Utilities.Tests;

namespace Invoices.Tests;

[TestFixture]
[TestOf(typeof(LoggingInvoiceRepo))]
public class LoggingInvoiceRepoTest
{
    private static Invoice.InvoiceContent ValidContent() => new(
        Date: DateTime.UtcNow,
        SellerAddress: new BillingAddress("Seller", "Rep", "123", null, "Addr", "City", "1000", "BG"),
        BuyerAddress: new BillingAddress("Buyer", "Rep", "456", null, "Addr", "City", "1000", "BG"),
        LineItems: [new Invoice.LineItem("Item", new Amount(100_00, Currency.Eur))],
        BankTransferInfo: new BankTransferInfo("BG00TEST", "Bank", "BIC"));

    [Test]
    public async Task CreateAsync_WhenWrappingFake_ThenDelegatesAndReturnsResult()
    {
        var inner = new FakeInvoiceRepo();
        var logger = new CapturingLogger();
        var sut = new LoggingInvoiceRepo(inner, logger);

        var content = ValidContent();
        var invoice = await sut.CreateAsync(content);

        Assert.That(invoice.Number, Is.EqualTo("1"));
        var retrieved = await sut.GetAsync("1");
        Assert.That(retrieved.Number, Is.EqualTo("1"));
        Assert.That(logger.InfoMessages, Does.Contain("InvoiceRepo.CreateAsync"));
        Assert.That(logger.InfoMessages, Does.Contain("InvoiceRepo.CreateAsync completed, number=1"));
        Assert.That(logger.InfoMessages, Does.Contain("InvoiceRepo.GetAsync number=1"));
    }

    [Test]
    public void GetAsync_WhenInnerThrows_ThenPropagatesExceptionAndLogsError()
    {
        var inner = new FakeInvoiceRepo();
        var logger = new CapturingLogger();
        var sut = new LoggingInvoiceRepo(inner, logger);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await sut.GetAsync("nonexistent"));
        Assert.That(ex!.Message, Does.Contain("nonexistent"));
        Assert.That(logger.InfoMessages, Does.Contain("InvoiceRepo.GetAsync number=nonexistent"));
        Assert.That(logger.ErrorEntries, Has.Count.EqualTo(1));
        Assert.That(logger.ErrorEntries[0].Message, Does.Contain("nonexistent"));
    }

    [Test]
    public async Task CreateAsync_WhenLoggerThrows_ThenOperationSucceedsAnyway()
    {
        var inner = new FakeInvoiceRepo();
        var logger = new ThrowingLogger();
        var sut = new LoggingInvoiceRepo(inner, logger);

        var content = ValidContent();
        var invoice = await sut.CreateAsync(content);

        Assert.That(invoice.Number, Is.EqualTo("1"));
    }
}
