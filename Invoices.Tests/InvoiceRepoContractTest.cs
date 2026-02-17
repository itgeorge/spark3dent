using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        public abstract Task<Invoice> SetUpInvoiceAsync(Invoice.InvoiceContent content);
        public abstract Task<Invoice> GetInvoiceAsync(string number);
    }

    [Test]
    public async Task Create_GivenValidContent_WhenCreatingInvoice_ThenReturnsCreatedInvoiceWithGeneratedNumber()
    {
        var fixture = await SetUpAsync();
        var content = BuildValidInvoiceContent();

        var created = await fixture.Repo.CreateAsync(content);

        Assert.That(created.Number, Is.Not.Null.And.Not.Empty);
        Assert.That(created.Content, Is.EqualTo(content));
    }

    [Test]
    public async Task Create_GivenValidContent_WhenCreatingInvoice_ThenInvoiceIsRetrievable()
    {
        var fixture = await SetUpAsync();
        var content = BuildValidInvoiceContent();

        var created = await fixture.Repo.CreateAsync(content);

        var retrieved = await fixture.GetInvoiceAsync(created.Number);
        Assert.That(retrieved, Is.EqualTo(created));
    }

    [Test]
    public async Task Create_WhenCreatingMultipleInvoices_ThenEachGetsUniqueNumber()
    {
        var fixture = await SetUpAsync();
        var created1 = await fixture.Repo.CreateAsync(BuildValidInvoiceContent());
        var created2 = await fixture.Repo.CreateAsync(BuildValidInvoiceContent(date: created1.Content.Date.AddDays(1), buyerAddress: created1.Content.BuyerAddress with { Name = "Other Buyer" }));

        Assert.That(created1.Number, Is.Not.EqualTo(created2.Number));
    }

    [Test]
    public async Task Create_GivenConcurrentCreateCallsWithSameContent_WhenRacing_ThenAllSucceedWithIncrementedNumbers()
    {
        var fixture = await SetUpAsync();
        var content = BuildValidInvoiceContent();
        var racers = Math.Min(16, 2 * Environment.ProcessorCount);
        var barrier = new Barrier(racers);
        var created = new ConcurrentBag<Invoice>();

        var tasks = Enumerable.Range(0, racers).Select(_ => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            var invoice = await fixture.Repo.CreateAsync(content);
            created.Add(invoice);
        })).ToList();
        await Task.WhenAll(tasks);

        Assert.That(created, Has.Count.EqualTo(racers));
        var numbers = created.Select(c => c.Number).ToHashSet();
        Assert.That(numbers, Has.Count.EqualTo(racers), "All invoice numbers must be a sequence of numbers incrementing by one");
        Assert.That(numbers, Is.EquivalentTo(Enumerable.Range(1, racers).Select(i => i.ToString())));
    }

    [Test]
    public async Task Get_GivenExistingInvoice_WhenGettingInvoice_ThenInvoiceIsRetrievable()
    {
        var fixture = await SetUpAsync();
        var created = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent());

        var retrieved = await fixture.GetInvoiceAsync(created.Number);

        Assert.That(retrieved, Is.EqualTo(created));
    }

    [Test]
    public async Task Get_GivenExistingInvoice_WhenGettingInvoiceWithDifferentNumber_ThenThrows()
    {
        var fixture = await SetUpAsync();
        var created = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent());
        var nonExistingNumber = created.Number + "1";

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
        var created = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent());
        var updatedContent = BuildValidInvoiceContent(
            date: created.Content.Date.AddDays(1),
            buyerAddress: created.Content.BuyerAddress with { Name = "Other Buyer" },
            lineItems: [created.Content.LineItems[0] with { Amount = new Amount(200, Currency.Eur) }],
            sellerAddress: created.Content.SellerAddress with { Name = "Other Seller" });

        await fixture.Repo.UpdateAsync(created.Number, updatedContent);

        var retrieved = await fixture.GetInvoiceAsync(created.Number);
        Assert.That(retrieved, Is.EqualTo(new Invoice(created.Number, updatedContent)));
    }

    [Test]
    public async Task Update_GivenMultipleExistingInvoices_WhenUpdatingInvoice_ThenOnlySpecifiedInvoiceIsUpdated()
    {
        var fixture = await SetUpAsync();
        var created1 = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent());
        var created2 = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: created1.Content.Date.AddDays(1)));
        var updatedContent = BuildValidInvoiceContent(buyerAddress: created1.Content.BuyerAddress with { Name = "Other Buyer" });

        await fixture.Repo.UpdateAsync(created1.Number, updatedContent);

        var changed = await fixture.GetInvoiceAsync(created1.Number);
        var unchanged = await fixture.GetInvoiceAsync(created2.Number);
        Assert.That(changed, Is.EqualTo(new Invoice(created1.Number, updatedContent)));
        Assert.That(unchanged, Is.EqualTo(created2));
    }

    [Test]
    public async Task Latest_GivenMultipleExistingInvoices_WhenGettingLatestInvoices_ThenInvoicesRetrievedLatestFirst()
    {
        var fixture = await SetUpAsync();
        var older = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: DateTime.Now.AddDays(-3)));
        var middle = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: DateTime.Now.AddDays(-2)));
        var newer = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: DateTime.Now.AddDays(-1)));

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
        var older = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: DateTime.Now.AddDays(-3)));
        await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: DateTime.Now.AddDays(-2)));
        await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: DateTime.Now.AddDays(-1)));
        var latestInvoices = await fixture.Repo.LatestAsync(2);

        var invoicesFromCursor = await fixture.Repo.LatestAsync(2, latestInvoices.NextStartAfter);

        Assert.That(invoicesFromCursor.Items, Has.Count.EqualTo(1));
        Assert.That(invoicesFromCursor.Items[0], Is.EqualTo(older));
    }

    [Test]
    public async Task Latest_GivenMultipleExistingInvoices_WhenGettingLatestWithLimit_ThenLimitedInvoicesAreRetrieved()
    {
        var fixture = await SetUpAsync();
        var older = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: DateTime.Now.AddDays(-3)));
        var middle = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: DateTime.Now.AddDays(-2)));
        var newer = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: DateTime.Now.AddDays(-1)));

        var invoices = await fixture.Repo.LatestAsync(2);

        Assert.That(invoices.Items, Has.Count.EqualTo(2));
        Assert.That(invoices.Items[0], Is.EqualTo(newer));
        Assert.That(invoices.Items[1], Is.EqualTo(middle));
    }

    [Test]
    public async Task Latest_GivenMultipleExistingInvoices_WhenGettingLatestWithLimitAndCursor_ThenLimitedInvoicesAreRetrievedFromCursor()
    {
        var fixture = await SetUpAsync();
        var oldest = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: DateTime.Now.AddDays(-4)));
        var older = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: DateTime.Now.AddDays(-3)));
        var middle = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: DateTime.Now.AddDays(-2)));
        var newer = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: DateTime.Now.AddDays(-1)));
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
        await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: DateTime.Now.AddDays(-4)));
        await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: DateTime.Now.AddDays(-3)));
        await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: DateTime.Now.AddDays(-2)));
        await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: DateTime.Now.AddDays(-1)));
        var all = await fixture.Repo.LatestAsync(5);

        var afterLastInvoice = await fixture.Repo.LatestAsync(5, all.NextStartAfter);

        Assert.That(afterLastInvoice.Items, Is.Empty);
        Assert.That(afterLastInvoice.NextStartAfter, Is.Null);
    }

    [Test]
    public async Task Create_WhenCreatingMultipleInvoices_ThenNumbersAreIncrementedByOne()
    {
        var fixture = await SetUpAsync();

        var created1 = await fixture.Repo.CreateAsync(BuildValidInvoiceContent());
        var created2 = await fixture.Repo.CreateAsync(BuildValidInvoiceContent(date: created1.Content.Date.AddDays(1)));
        var created3 = await fixture.Repo.CreateAsync(BuildValidInvoiceContent(date: created2.Content.Date.AddDays(1)));

        Assert.That(long.Parse(created2.Number), Is.EqualTo(long.Parse(created1.Number) + 1));
        Assert.That(long.Parse(created3.Number), Is.EqualTo(long.Parse(created2.Number) + 1));
    }

    [Test]
    public async Task Create_WhenCreatingInvoicesWithDifferentDates_ThenHigherNumberImpliesLaterDate()
    {
        var fixture = await SetUpAsync();
        var olderDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newerDate = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var createdFirst = await fixture.Repo.CreateAsync(BuildValidInvoiceContent(date: olderDate));
        
        var createdSecond = await fixture.Repo.CreateAsync(BuildValidInvoiceContent(date: newerDate));

        Assert.That(long.Parse(createdSecond.Number), Is.GreaterThan(long.Parse(createdFirst.Number)));
        Assert.That(createdSecond.Content.Date, Is.GreaterThan(createdFirst.Content.Date));
    }

    [Test]
    public async Task Create_WhenCreatingInvoicesWithMismatchingDatesAndNumbers_ThenThrows()
    {
        var fixture = await SetUpAsync();
        var created1 = await fixture.Repo.CreateAsync(BuildValidInvoiceContent());

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Repo.CreateAsync(BuildValidInvoiceContent(date: created1.Content.Date.AddDays(-1))));
    }

    [Test]
    public async Task Create_GivenExistingInvoices_WhenCreatingInvoicesWithDateBetweenExistingInvoices_ThenThrows()
    {
        var fixture = await SetUpAsync();
        var oldestDate = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var middleDate = oldestDate.AddDays(1);
        var newestDate = oldestDate.AddDays(2);
        await fixture.Repo.CreateAsync(BuildValidInvoiceContent(date: oldestDate));
        await fixture.Repo.CreateAsync(BuildValidInvoiceContent(date: newestDate));

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Repo.CreateAsync(BuildValidInvoiceContent(date: middleDate)));
    }

    [Test]
    public async Task Create_WhenCreatingMultipleInvoicesSequentially_ThenNumbersAreIncrementedByOne()
    {
        var fixture = await SetUpAsync();
        var baseDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var created = new List<Invoice>();
        for (var i = 0; i < 50; i++)
        {
            var content = BuildValidInvoiceContent(date: baseDate.AddDays(i));
            created.Add(await fixture.Repo.CreateAsync(content));
        }

        for (var i = 1; i < created.Count; i++)
        {
            Assert.That(long.Parse(created[i].Number), Is.EqualTo(long.Parse(created[i - 1].Number) + 1));
            Assert.That(created[i].Content.Date, Is.GreaterThan(created[i - 1].Content.Date));
        }
    }

    private static Invoice.InvoiceContent BuildValidInvoiceContent(DateTime? date = null, BillingAddress? sellerAddress = null, BillingAddress? buyerAddress = null, Invoice.LineItem[]? lineItems = null)
    {
        return new Invoice.InvoiceContent(
            Date: date ?? DateTime.Now,
            SellerAddress: sellerAddress ?? new BillingAddress(Name: "Test Seller", RepresentativeName: "Test Representative", CompanyIdentifier: "Test CompanyIdentifier", VatIdentifier: "Test VatIdentifier", Address: "Test Address", City: "Test City", PostalCode: "Test PostalCode", Country: "Test Country"),
            BuyerAddress: buyerAddress ?? new BillingAddress(Name: "Test Buyer", RepresentativeName: "Test Representative", CompanyIdentifier: "Test CompanyIdentifier", VatIdentifier: "Test VatIdentifier", Address: "Test Address", City: "Test City", PostalCode: "Test PostalCode", Country: "Test Country"),
            LineItems: lineItems ?? new[] { new Invoice.LineItem(Description: "Test Item", Amount: new Amount(100, Currency.Eur)) });
    }
}