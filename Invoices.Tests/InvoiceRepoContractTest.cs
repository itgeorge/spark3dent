using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    public async Task Import_GivenValidContentAndNumber_WhenImporting_ThenReturnsInvoiceWithSpecifiedNumber()
    {
        var fixture = await SetUpAsync();
        var content = BuildValidInvoiceContent();
        const string number = "123";

        var imported = await fixture.Repo.ImportAsync(content, number);

        Assert.That(imported.Number, Is.EqualTo(number));
        AssertInvoiceContentsEqual(content, imported.Content);
        Assert.That(imported.IsCorrected, Is.False);
    }

    [Test]
    public async Task Import_GivenValidContent_WhenImporting_ThenInvoiceIsRetrievable()
    {
        var fixture = await SetUpAsync();
        var content = BuildValidInvoiceContent();
        const string number = "124";

        var imported = await fixture.Repo.ImportAsync(content, number);

        var retrieved = await fixture.GetInvoiceAsync(imported.Number);
        AssertInvoicesEqual(imported, retrieved);
    }

    [Test]
    public async Task Import_GivenExistingNumber_WhenImporting_ThenThrows()
    {
        var fixture = await SetUpAsync();
        var content = BuildValidInvoiceContent();
        const string number = "125";

        await fixture.Repo.ImportAsync(content, number);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Repo.ImportAsync(BuildValidInvoiceContent(buyerAddress: content.BuyerAddress with { Name = "Other" }), number));
    }

    [Test]
    public async Task Import_GivenImportedInvoice_WhenCreatingNewInvoice_ThenSequenceAdvancesFromImported()
    {
        var fixture = await SetUpAsync();
        var content = BuildValidInvoiceContent();
        await fixture.Repo.ImportAsync(content, "999");

        var created = await fixture.Repo.CreateAsync(BuildValidInvoiceContent(date: content.Date.AddDays(1)));

        Assert.That(created.Number, Is.EqualTo("1000"), "Import should advance sequence; Create should get next number after imported");
    }

    [Test]
    public async Task Import_GivenImportedInvoiceWithLowerNumber_WhenCreatingNewInvoice_ThenSequenceUsesHigherOfImportedOrExisting()
    {
        var fixture = await SetUpAsync();
        var content = BuildValidInvoiceContent();
        await fixture.Repo.ImportAsync(content, "999");
        var created1 = await fixture.Repo.CreateAsync(BuildValidInvoiceContent(date: content.Date.AddDays(1)));
        Assert.That(created1.Number, Is.EqualTo("1000"));

        await fixture.Repo.ImportAsync(BuildValidInvoiceContent(date: content.Date.AddDays(-10)), "5");

        var created2 = await fixture.Repo.CreateAsync(BuildValidInvoiceContent(date: content.Date.AddDays(2)));

        Assert.That(created2.Number, Is.EqualTo("1001"), "Importing older invoice (5) should not regress sequence; Create should get 1001");
    }

    [Test]
    public async Task Create_GivenValidContent_WhenCreatingInvoice_ThenReturnsCreatedInvoiceWithGeneratedNumber()
    {
        var fixture = await SetUpAsync();
        var content = BuildValidInvoiceContent();

        var created = await fixture.Repo.CreateAsync(content);

        Assert.That(created.Number, Is.Not.Null.And.Not.Empty);
        AssertInvoiceContentsEqual(content, created.Content);
    }

    [Test]
    public async Task Create_GivenValidContent_WhenCreatingInvoice_ThenIsCorrectedIsFalse()
    {
        var fixture = await SetUpAsync();
        var content = BuildValidInvoiceContent();

        var created = await fixture.Repo.CreateAsync(content);

        Assert.That(created.IsCorrected, Is.False);
    }

    [Test]
    public async Task Create_GivenValidContent_WhenCreatingInvoice_ThenInvoiceIsRetrievable()
    {
        var fixture = await SetUpAsync();
        var content = BuildValidInvoiceContent();

        var created = await fixture.Repo.CreateAsync(content);

        var retrieved = await fixture.GetInvoiceAsync(created.Number);
        AssertInvoicesEqual(created, retrieved);
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

        AssertInvoicesEqual(created, retrieved);
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
        AssertInvoicesEqual(new Invoice(created.Number, updatedContent, isCorrected: true), retrieved);
    }

    [Test]
    public async Task Update_GivenExistingInvoice_WhenUpdatingInvoice_ThenIsCorrectedIsTrue()
    {
        var fixture = await SetUpAsync();
        var created = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent());
        var updatedContent = BuildValidInvoiceContent(buyerAddress: created.Content.BuyerAddress with { Name = "Other Buyer" });

        await fixture.Repo.UpdateAsync(created.Number, updatedContent);

        var retrieved = await fixture.GetInvoiceAsync(created.Number);
        Assert.That(retrieved.IsCorrected, Is.True);
    }

    [Test]
    public async Task Update_GivenExistingInvoice_WhenUpdatingInvoiceMultipleTimes_ThenIsCorrectedRemainsTrue()
    {
        var fixture = await SetUpAsync();
        var created = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent());
        var updatedContent1 = BuildValidInvoiceContent(buyerAddress: created.Content.BuyerAddress with { Name = "First Update" });
        var updatedContent2 = BuildValidInvoiceContent(buyerAddress: created.Content.BuyerAddress with { Name = "Second Update" });

        await fixture.Repo.UpdateAsync(created.Number, updatedContent1);
        await fixture.Repo.UpdateAsync(created.Number, updatedContent2);

        var retrieved = await fixture.GetInvoiceAsync(created.Number);
        Assert.That(retrieved.IsCorrected, Is.True);
    }

    [Test]
    public async Task Create_WhenCreatingMultipleInvoices_ThenOnlyUpdatedOneIsCorrected()
    {
        var fixture = await SetUpAsync();
        var created1 = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent());
        var created2 = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: created1.Content.Date.AddDays(1)));
        var updatedContent = BuildValidInvoiceContent(buyerAddress: created1.Content.BuyerAddress with { Name = "Updated Buyer" });

        await fixture.Repo.UpdateAsync(created1.Number, updatedContent);

        var retrieved1 = await fixture.GetInvoiceAsync(created1.Number);
        var retrieved2 = await fixture.GetInvoiceAsync(created2.Number);
        Assert.That(retrieved1.IsCorrected, Is.True);
        Assert.That(retrieved2.IsCorrected, Is.False);
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
        AssertInvoicesEqual(new Invoice(created1.Number, updatedContent, isCorrected: true), changed);
        AssertInvoicesEqual(created2, unchanged);
    }

    [Test]
    public async Task Update_WhenUpdatingInvoiceDateToBeforePreviousInvoiceDate_ThenThrows()
    {
        var fixture = await SetUpAsync();
        var baseDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var older = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: baseDate));
        var newer = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: baseDate.AddDays(1)));

        var updatedContentWithEarlierDate = BuildValidInvoiceContent(
            date: older.Content.Date.AddDays(-1),
            buyerAddress: newer.Content.BuyerAddress,
            sellerAddress: newer.Content.SellerAddress,
            lineItems: newer.Content.LineItems);

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Repo.UpdateAsync(newer.Number, updatedContentWithEarlierDate));
    }

    [Test]
    public async Task Update_WhenUpdatingInvoiceDateToAfterNextInvoiceDate_ThenThrows()
    {
        var fixture = await SetUpAsync();
        var baseDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var older = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: baseDate));
        var newer = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: baseDate.AddDays(1)));

        var updatedContentWithLaterDate = BuildValidInvoiceContent(
            date: newer.Content.Date.AddDays(1),
            buyerAddress: older.Content.BuyerAddress,
            sellerAddress: older.Content.SellerAddress,
            lineItems: older.Content.LineItems);

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Repo.UpdateAsync(older.Number, updatedContentWithLaterDate));
    }

    [Test]
    public async Task Update_GivenExistingInvoices_WhenUpdatingInvoiceWithNonIntersectingDate_ThenInvoiceIsUpdated()
    {
        var fixture = await SetUpAsync();
        var older = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: DateTime.Now.AddDays(-5)));
        var middle = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: DateTime.Now.AddDays(-3)));
        var newer = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent(date: DateTime.Now.AddDays(-1)));

        // update 1 day ahead but still before the newer invoice
        var updatedContent1 = BuildValidInvoiceContent(date: middle.Content.Date.AddDays(1));
        await fixture.Repo.UpdateAsync(middle.Number, updatedContent1);
        var retrieved = await fixture.GetInvoiceAsync(middle.Number);
        AssertInvoicesEqual(new Invoice(middle.Number, updatedContent1, isCorrected: true), retrieved);

        // update 1 day back but still after the older invoice
        var updatedContent2 = BuildValidInvoiceContent(date: newer.Content.Date.AddDays(-1));
        await fixture.Repo.UpdateAsync(middle.Number, updatedContent2);
        retrieved = await fixture.GetInvoiceAsync(middle.Number);
        AssertInvoicesEqual(new Invoice(middle.Number, updatedContent2, isCorrected: true), retrieved);
    }

    [Test]
    public async Task Update_GivenConcurrentUpdatesToDifferentProperties_WhenRacing_ThenOnlyOneUpdateIsReflected()
    {
        var fixture = await SetUpAsync();
        var created = await fixture.SetUpInvoiceAsync(BuildValidInvoiceContent());
        var racers = Math.Min(8, 2 * Environment.ProcessorCount);
        var barrier = new Barrier(racers);

        var tasks = Enumerable.Range(0, racers).Select(i => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            var uniqueSignature = $"Racer{i}";
            var updatedContent = BuildValidInvoiceContent(
                buyerAddress: created.Content.BuyerAddress with { Name = uniqueSignature },
                sellerAddress: created.Content.SellerAddress with { Name = uniqueSignature },
                lineItems: [created.Content.LineItems[0] with { Amount = new Amount(100 + i, Currency.Eur) }]);
            await fixture.Repo.UpdateAsync(created.Number, updatedContent);
        })).ToList();
        await Task.WhenAll(tasks);

        var retrieved = await fixture.GetInvoiceAsync(created.Number);
        var buyerName = retrieved.Content.BuyerAddress.Name;
        var sellerName = retrieved.Content.SellerAddress.Name;
        var amountCents = retrieved.Content.LineItems[0].Amount.Cents;

        Assert.That(buyerName, Is.EqualTo(sellerName), "Buyer and seller should come from same update (atomic)");
        var racerIndex = int.Parse(buyerName.Replace("Racer", ""));
        Assert.That(amountCents, Is.EqualTo(100 + racerIndex), "Line item amount should match the same racer's update");
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
        AssertInvoicesEqual(newer, latestInvoices.Items[0]);
        AssertInvoicesEqual(middle, latestInvoices.Items[1]);
        AssertInvoicesEqual(older, latestInvoices.Items[2]);
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
        AssertInvoicesEqual(older, invoicesFromCursor.Items[0]);
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
        AssertInvoicesEqual(newer, invoices.Items[0]);
        AssertInvoicesEqual(middle, invoices.Items[1]);
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
        AssertInvoicesEqual(middle, secondPage.Items[0]);
        AssertInvoicesEqual(older, secondPage.Items[1]);

        var lastPage = await fixture.Repo.LatestAsync(2, secondPage.NextStartAfter);
        Assert.That(lastPage.Items, Has.Count.EqualTo(1));
        AssertInvoicesEqual(oldest, lastPage.Items[0]);
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

    private static readonly BankTransferInfo TestBankTransferInfo = new(Iban: "BG00TEST00000000000000", BankName: "Test Bank", Bic: "TESTBGSF");

    protected static void AssertInvoicesEqual(Invoice expected, Invoice actual)
    {
        Assert.That(actual.Number, Is.EqualTo(expected.Number));
        Assert.That(actual.IsCorrected, Is.EqualTo(expected.IsCorrected));
        AssertInvoiceContentsEqual(expected.Content, actual.Content);
    }

    protected static void AssertInvoiceContentsEqual(Invoice.InvoiceContent expected, Invoice.InvoiceContent actual)
    {
        Assert.That(actual.Date, Is.EqualTo(expected.Date));
        Assert.That(actual.SellerAddress, Is.EqualTo(expected.SellerAddress));
        Assert.That(actual.BuyerAddress, Is.EqualTo(expected.BuyerAddress));
        Assert.That(actual.BankTransferInfo, Is.EqualTo(expected.BankTransferInfo));
        Assert.That(actual.LineItems, Has.Length.EqualTo(expected.LineItems.Length));
        for (var i = 0; i < expected.LineItems.Length; i++)
            Assert.That(actual.LineItems[i], Is.EqualTo(expected.LineItems[i]));
    }

    protected static Invoice.InvoiceContent BuildValidInvoiceContent(DateTime? date = null, BillingAddress? sellerAddress = null, BillingAddress? buyerAddress = null, Invoice.LineItem[]? lineItems = null, BankTransferInfo? bankTransferInfo = null)
    {
        return new Invoice.InvoiceContent(
            Date: date ?? DateTime.Now,
            SellerAddress: sellerAddress ?? new BillingAddress(Name: "Test Seller", RepresentativeName: "Test Representative", CompanyIdentifier: "Test CompanyIdentifier", VatIdentifier: "Test VatIdentifier", Address: "Test Address", City: "Test City", PostalCode: "Test PostalCode", Country: "Test Country"),
            BuyerAddress: buyerAddress ?? new BillingAddress(Name: "Test Buyer", RepresentativeName: "Test Representative", CompanyIdentifier: "Test CompanyIdentifier", VatIdentifier: "Test VatIdentifier", Address: "Test Address", City: "Test City", PostalCode: "Test PostalCode", Country: "Test Country"),
            LineItems: lineItems ?? new[] { new Invoice.LineItem(Description: "Test Item", Amount: new Amount(100, Currency.Eur)) },
            BankTransferInfo: bankTransferInfo ?? TestBankTransferInfo);
    }
}