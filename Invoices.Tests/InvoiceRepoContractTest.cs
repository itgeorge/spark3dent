using System;
using System.Threading.Tasks;
using Invoices;
using NUnit.Framework;

namespace Invoices.Tests;

[TestFixture]
[TestOf(typeof(IInvoiceRepo))]
public abstract class InvoiceRepoContractTest
{
    protected abstract Task<FixtureBase> SetUpAsync();

    protected abstract class FixtureBase
    {
        public abstract IInvoiceRepo Repo { get; }
        public abstract Task SetUpInvoiceAsync(Invoice invoice);
        public abstract Task<Invoice> GetInvoiceAsync(string number);
    }

    [Test]
    public async Task Create_GivenNoExistingInvoice_WhenCreatingInvoice_ThenInvoiceIsRetrievable()
    {
        var fixture = await SetUpAsync();
        var invoice = BuildValidInvoice();
        await fixture.SetUpInvoiceAsync(invoice);

        await fixture.Repo.CreateAsync(invoice);

        var retrievedInvoice = await fixture.GetInvoiceAsync(invoice.Number);
        Assert.That(retrievedInvoice, Is.EqualTo(invoice));
    }

    [Test]
    public async Task Create_GivenExistingInvoice_WhenCreatingInvoiceWithSameNumber_ThenThrows()
    {
        var fixture = await SetUpAsync();
        var invoice = BuildValidInvoice();
        await fixture.SetUpInvoiceAsync(invoice);
        var otherInvoiceSameNumber = BuildValidInvoice(number: invoice.Number, 
            date: invoice.Content.Date.AddDays(1), 
            buyerAddress: invoice.Content.BuyerAddress with { Name = "Other Buyer" });

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Repo.CreateAsync(otherInvoiceSameNumber));
    }

    [Test]
    public async Task Get_GivenExistingInvoice_WhenGettingInvoice_ThenInvoiceIsRetrievable()
    {
        var fixture = await SetUpAsync();
        var invoice = BuildValidInvoice();
        await fixture.SetUpInvoiceAsync(invoice);

        var retrievedInvoice = await fixture.GetInvoiceAsync(invoice.Number);

        Assert.That(retrievedInvoice, Is.EqualTo(invoice));
    }

    [Test]
    public async Task Get_GivenExistingInvoice_WhenGettingInvoiceWithDifferentNumber_ThenThrows()
    {
        var fixture = await SetUpAsync();
        var existingInvoice = BuildValidInvoice(number: "1234567890");
        var nonExistingNumber = existingInvoice.Number + "1";

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.GetInvoiceAsync(nonExistingNumber));
    }

    [Test]
    public async Task Get_GivenNoExistingInvoice_WhenGettingInvoice_ThenThrows()
    {
        var fixture = await SetUpAsync();
        var nonExistingNumber = "1234567890";

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.GetInvoiceAsync(nonExistingNumber));
    }

    [Test]
    public async Task Update_GivenExistingInvoice_WhenUpdatingInvoice_ThenUpdatedInvoiceIsRetrievable()
    {
        var fixture = await SetUpAsync();
        var invoice = BuildValidInvoice();
        await fixture.SetUpInvoiceAsync(invoice);
        var updatedInvoice = BuildValidInvoice(number: invoice.Number, 
            date: invoice.Content.Date.AddDays(1), 
            buyerAddress: invoice.Content.BuyerAddress with { Name = "Other Buyer" },
            lineItems: [invoice.Content.LineItems[0] with { Amount = new Amount(200, Currency.Eur) }],
            sellerAddress: invoice.Content.SellerAddress with { Name = "Other Seller" });

        await fixture.Repo.UpdateAsync(invoice.Number, updatedInvoice.Content);

        var retrievedInvoice = await fixture.GetInvoiceAsync(invoice.Number);
        Assert.That(retrievedInvoice, Is.EqualTo(updatedInvoice));
    }

    [Test]
    public async Task Update_GivenMultipleExistingInvoices_WhenUpdatingInvoice_ThenOnlySpecifiedInvoiceIsUpdated()
    {
        var fixture = await SetUpAsync();
        var originalInvoice1 = BuildValidInvoice(number: "1234567890");
        var originalInvoice2 = BuildValidInvoice(number: "1234567891");
        await fixture.SetUpInvoiceAsync(originalInvoice1);
        await fixture.SetUpInvoiceAsync(originalInvoice2);
        var updatedInvoice = BuildValidInvoice(number: originalInvoice1.Number, 
            buyerAddress: originalInvoice1.Content.BuyerAddress with { Name = "Other Buyer" });

        await fixture.Repo.UpdateAsync(originalInvoice1.Number, updatedInvoice.Content);

        var changedInvoice = await fixture.GetInvoiceAsync(originalInvoice1.Number);
        var unchangedInvoice = await fixture.GetInvoiceAsync(originalInvoice2.Number);
        Assert.That(changedInvoice, Is.EqualTo(updatedInvoice));
        Assert.That(unchangedInvoice, Is.EqualTo(originalInvoice2));
    }

    [Test]
    public async Task Latest_GivenMultipleExistingInvoices_WhenGettingLatestInvoices_ThenInvoicesRetrievedLatestFirst()
    {
        var fixture = await SetUpAsync();
        var middle = BuildValidInvoice(number: "1234567891", date: DateTime.Now.AddDays(-2));
        var older = BuildValidInvoice(number: "1234567892", date: DateTime.Now.AddDays(-3));
        var newer = BuildValidInvoice(number: "1234567893", date: DateTime.Now.AddDays(-1));
        await fixture.SetUpInvoiceAsync(middle);
        await fixture.SetUpInvoiceAsync(older);
        await fixture.SetUpInvoiceAsync(newer);

        var latestInvoices = await fixture.Repo.LatestAsync(5);

        Assert.That(latestInvoices.Items, Has.Count.EqualTo(3));
        Assert.That(latestInvoices.Items[0], Is.EqualTo(newer));
        Assert.That(latestInvoices.Items[1], Is.EqualTo(middle));
        Assert.That(latestInvoices.Items[2], Is.EqualTo(older));
        Assert.That(latestInvoices.NextStartAfter, Is.EqualTo(older.Number));
    }

    [Test]
    public async Task Latest_GivenMultipleExistingInvoices_WhenGettingLatestWithCursor_ThenInvoicesRetrievedFromCursor()
    {
        var fixture = await SetUpAsync();
        var middle = BuildValidInvoice(number: "1234567891", date: DateTime.Now.AddDays(-2));
        var older = BuildValidInvoice(number: "1234567892", date: DateTime.Now.AddDays(-3));
        var newer = BuildValidInvoice(number: "1234567893", date: DateTime.Now.AddDays(-1));
        await fixture.SetUpInvoiceAsync(middle);
        await fixture.SetUpInvoiceAsync(older);
        await fixture.SetUpInvoiceAsync(newer);
        var latestInvoices = await fixture.Repo.LatestAsync(2);

        var invoicesFromCursor = await fixture.Repo.LatestAsync(2, latestInvoices.NextStartAfter);

        Assert.That(invoicesFromCursor.Items, Has.Count.EqualTo(1));
        Assert.That(invoicesFromCursor.Items[0], Is.EqualTo(older));
    }

    [Test]
    public async Task Latest_GivenMultipleExistingInvoices_WhenGettingLatestWithLimit_ThenLimitedInvoicesAreRetrieved()
    {
        var fixture = await SetUpAsync();
        var middle = BuildValidInvoice(number: "1234567891", date: DateTime.Now.AddDays(-2));
        var older = BuildValidInvoice(number: "1234567892", date: DateTime.Now.AddDays(-3));
        var newer = BuildValidInvoice(number: "1234567893", date: DateTime.Now.AddDays(-1));
        await fixture.SetUpInvoiceAsync(middle);
        await fixture.SetUpInvoiceAsync(older);
        await fixture.SetUpInvoiceAsync(newer);

        var invoices = await fixture.Repo.LatestAsync(2);

        Assert.That(invoices.Items, Has.Count.EqualTo(2));
        Assert.That(invoices.Items[0], Is.EqualTo(newer));
        Assert.That(invoices.Items[1], Is.EqualTo(middle));
    }

    [Test]
    public async Task Latest_GivenMultipleExistingInvoices_WhenGettingLatestWithLimitAndCursor_ThenLimitedInvoicesAreRetrievedFromCursor()
    {
        var fixture = await SetUpAsync();
        var middle = BuildValidInvoice(number: "1234567891", date: DateTime.Now.AddDays(-2));
        var older = BuildValidInvoice(number: "1234567892", date: DateTime.Now.AddDays(-3));
        var oldest = BuildValidInvoice(number: "1234567888", date: DateTime.Now.AddDays(-4));
        var newer = BuildValidInvoice(number: "1234567893", date: DateTime.Now.AddDays(-1));
        await fixture.SetUpInvoiceAsync(middle);
        await fixture.SetUpInvoiceAsync(older);
        await fixture.SetUpInvoiceAsync(newer);
        await fixture.SetUpInvoiceAsync(oldest);
        var firstPage = await fixture.Repo.LatestAsync(1);

        var secondPage = await fixture.Repo.LatestAsync(2, firstPage.NextStartAfter);

        Assert.That(secondPage.Items, Has.Count.EqualTo(2));
        Assert.That(secondPage.Items[0], Is.EqualTo(middle));
        Assert.That(secondPage.Items[1], Is.EqualTo(older));
        
        var lastPage = await fixture.Repo.LatestAsync(2, secondPage.NextStartAfter);
        Assert.That(lastPage.Items, Has.Count.EqualTo(1));
        Assert.That(lastPage.Items[0], Is.EqualTo(oldest));
    }

    [Test]
    public async Task Latest_GivenMultipleExistingInvoices_WhenGettingLatestWithCursorAtEnd_ThenReturnsEmptyResult()
    {
        var fixture = await SetUpAsync();
        var middle = BuildValidInvoice(number: "1234567891", date: DateTime.Now.AddDays(-2));
        var older = BuildValidInvoice(number: "1234567892", date: DateTime.Now.AddDays(-3));
        var oldest = BuildValidInvoice(number: "1234567888", date: DateTime.Now.AddDays(-4));
        var newer = BuildValidInvoice(number: "1234567893", date: DateTime.Now.AddDays(-1));
        await fixture.SetUpInvoiceAsync(middle);
        await fixture.SetUpInvoiceAsync(older);
        await fixture.SetUpInvoiceAsync(newer);
        await fixture.SetUpInvoiceAsync(oldest);
        var all = await fixture.Repo.LatestAsync(5);

        var afterLastInvoice = await fixture.Repo.LatestAsync(5, all.NextStartAfter);

        Assert.That(afterLastInvoice.Items, Is.Empty);
        Assert.That(afterLastInvoice.NextStartAfter, Is.Null);
    }

    private static Invoice BuildValidInvoice(string? number = null, DateTime? date = null, BillingAddress? sellerAddress = null, BillingAddress? buyerAddress = null, Invoice.LineItem[]? lineItems = null)
    {
        return new Invoice(number: number ?? "1234567890", content: new Invoice.InvoiceContent(Date: date ?? DateTime.Now, SellerAddress: sellerAddress ?? new Invoices.BillingAddress(Name: "Test Seller", RepresentativeName: "Test Representative", CompanyIdentifier: "Test CompanyIdentifier", VatIdentifier: "Test VatIdentifier", Address: "Test Address", City: "Test City", PostalCode: "Test PostalCode", Country: "Test Country"), BuyerAddress: buyerAddress ?? new Invoices.BillingAddress(Name: "Test Buyer", RepresentativeName: "Test Representative", CompanyIdentifier: "Test CompanyIdentifier", VatIdentifier: "Test VatIdentifier", Address: "Test Address", City: "Test City", PostalCode: "Test PostalCode", Country: "Test Country"), LineItems: lineItems ?? new[] { new Invoice.LineItem(Description: "Test Item", Amount: new Amount(100, Currency.Eur)) }));
    }
}