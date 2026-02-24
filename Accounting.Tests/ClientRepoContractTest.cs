using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Accounting;
using Invoices;
using NUnit.Framework;

namespace Accounting.Tests;

[TestFixture]
[TestOf(typeof(IClientRepo))]
public abstract class ClientRepoContractTest
{
    protected abstract Task<FixtureBase> SetUpAsync();

    protected abstract class FixtureBase
    {
        public abstract IClientRepo Repo { get; }
        public abstract Task SetUpClientAsync(Client client);
        public abstract Task<Client> GetClientAsync(string nickname);
        public abstract Task<Invoice> SetUpInvoiceAsync(Invoice.InvoiceContent content);
    }

    private static BillingAddress ValidAddress(string name = "Test Client") =>
        new(Name: name, RepresentativeName: "John Doe", CompanyIdentifier: "123456789",
            VatIdentifier: "BG123456789", Address: "123 Main St", City: "Sofia",
            PostalCode: "1000", Country: "Bulgaria");

    [Test]
    public async Task Add_GivenValidClient_WhenAdding_ThenClientIsRetrievable()
    {
        var fixture = await SetUpAsync();
        var client = new Client("acme", ValidAddress());

        await fixture.Repo.AddAsync(client);

        var retrieved = await fixture.GetClientAsync("acme");
        Assert.That(retrieved, Is.EqualTo(client));
    }

    [Test]
    public async Task Add_WhenAddingDuplicateNickname_ThenThrows()
    {
        var fixture = await SetUpAsync();
        var client = new Client("acme", ValidAddress());
        await fixture.Repo.AddAsync(client);

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Repo.AddAsync(client));
    }

    [Test]
    public async Task Add_GivenConcurrentAddCallsWithDifferentNicknames_WhenRacing_ThenAllSucceed()
    {
        var fixture = await SetUpAsync();
        var racers = Math.Min(16, 2 * Environment.ProcessorCount);
        var barrier = new Barrier(racers);
        var added = new ConcurrentBag<Client>();

        var tasks = Enumerable.Range(0, racers).Select(i => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            var client = new Client($"client-{i}", ValidAddress($"Client {i}"));
            await fixture.Repo.AddAsync(client);
            added.Add(client);
        })).ToList();
        await Task.WhenAll(tasks);

        Assert.That(added, Has.Count.EqualTo(racers));
        var nicknames = added.Select(c => c.Nickname).ToHashSet();
        Assert.That(nicknames, Has.Count.EqualTo(racers), "All client nicknames must be unique");
        foreach (var client in added)
        {
            var retrieved = await fixture.GetClientAsync(client.Nickname);
            Assert.That(retrieved, Is.EqualTo(client));
        }
    }

    [Test]
    public async Task Get_GivenExistingClient_WhenGetting_ThenReturnsClient()
    {
        var fixture = await SetUpAsync();
        var client = new Client("acme", ValidAddress());
        await fixture.SetUpClientAsync(client);

        var retrieved = await fixture.GetClientAsync("acme");

        Assert.That(retrieved, Is.EqualTo(client));
    }

    [Test]
    public async Task Get_GivenNonExistingClient_WhenGetting_ThenThrows()
    {
        var fixture = await SetUpAsync();

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.GetClientAsync("nonexistent"));
    }

    [Test]
    public async Task Update_GivenExistingClient_WhenUpdatingAddress_ThenUpdated()
    {
        var fixture = await SetUpAsync();
        var client = new Client("acme", ValidAddress());
        await fixture.SetUpClientAsync(client);
        var newAddress = ValidAddress("Updated Name") with { Address = "456 New St" };

        await fixture.Repo.UpdateAsync("acme", new IClientRepo.ClientUpdate(Nickname: null, Address: newAddress));

        var retrieved = await fixture.GetClientAsync("acme");
        Assert.That(retrieved.Address, Is.EqualTo(newAddress));
    }

    [Test]
    public async Task Update_GivenExistingClient_WhenUpdatingNickname_ThenUpdated()
    {
        var fixture = await SetUpAsync();
        var client = new Client("acme", ValidAddress());
        await fixture.SetUpClientAsync(client);

        await fixture.Repo.UpdateAsync("acme", new IClientRepo.ClientUpdate(Nickname: "acme-corp", Address: null));

        var retrieved = await fixture.GetClientAsync("acme-corp");
        Assert.That(retrieved.Nickname, Is.EqualTo("acme-corp"));
        Assert.That(retrieved.Address, Is.EqualTo(client.Address));
    }

    [Test]
    public async Task Update_GivenNonExistingClient_WhenUpdating_ThenThrows()
    {
        var fixture = await SetUpAsync();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Repo.UpdateAsync("nonexistent", new IClientRepo.ClientUpdate(Nickname: null, Address: ValidAddress())));
    }

    [Test]
    public async Task Update_GivenConcurrentUpdatesToSameClient_WhenRacing_ThenOnlyOneUpdateIsReflected()
    {
        var fixture = await SetUpAsync();
        var client = new Client("acme", ValidAddress());
        await fixture.SetUpClientAsync(client);
        var racers = Math.Min(8, 2 * Environment.ProcessorCount);
        var barrier = new Barrier(racers);

        var tasks = Enumerable.Range(0, racers).Select(i => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            var uniqueSignature = $"Racer{i}";
            var newAddress = ValidAddress(uniqueSignature) with { Address = $"Address {i}" };
            await fixture.Repo.UpdateAsync("acme", new IClientRepo.ClientUpdate(Nickname: null, Address: newAddress));
        })).ToList();
        await Task.WhenAll(tasks);

        var retrieved = await fixture.GetClientAsync("acme");
        var retrievedName = retrieved.Address.Name;
        var retrievedAddress = retrieved.Address.Address;

        Assert.That(retrievedName, Does.StartWith("Racer"), "Name should come from one of the racer updates");
        var racerIndex = int.Parse(retrievedName.Replace("Racer", ""));
        Assert.That(retrievedAddress, Is.EqualTo($"Address {racerIndex}"), "Address should match the same racer's update");
    }

    [Test]
    public async Task Delete_GivenExistingClient_WhenDeleting_ThenNotRetrievable()
    {
        var fixture = await SetUpAsync();
        var client = new Client("acme", ValidAddress());
        await fixture.SetUpClientAsync(client);

        await fixture.Repo.DeleteAsync("acme");

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.GetClientAsync("acme"));
    }

    [Test]
    public async Task Delete_GivenNonExistingClient_WhenDeleting_ThenThrows()
    {
        var fixture = await SetUpAsync();

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Repo.DeleteAsync("nonexistent"));
    }

    [Test]
    public async Task List_GivenMultipleClients_WhenListing_ThenReturnedSortedAlphabetically()
    {
        var fixture = await SetUpAsync();
        await fixture.SetUpClientAsync(new Client("charlie", ValidAddress("Charlie")));
        await fixture.SetUpClientAsync(new Client("alice", ValidAddress("Alice")));
        await fixture.SetUpClientAsync(new Client("bob", ValidAddress("Bob")));

        var result = await fixture.Repo.ListAsync(10);

        Assert.That(result.Items, Has.Count.EqualTo(3));
        Assert.That(result.Items[0].Nickname, Is.EqualTo("alice"));
        Assert.That(result.Items[1].Nickname, Is.EqualTo("bob"));
        Assert.That(result.Items[2].Nickname, Is.EqualTo("charlie"));
    }

    [Test]
    public async Task List_GivenMultipleClients_WhenListingWithLimit_ThenLimited()
    {
        var fixture = await SetUpAsync();
        await fixture.SetUpClientAsync(new Client("alice", ValidAddress()));
        await fixture.SetUpClientAsync(new Client("bob", ValidAddress()));
        await fixture.SetUpClientAsync(new Client("charlie", ValidAddress()));

        var result = await fixture.Repo.ListAsync(2);

        Assert.That(result.Items, Has.Count.EqualTo(2));
        Assert.That(result.Items[0].Nickname, Is.EqualTo("alice"));
        Assert.That(result.Items[1].Nickname, Is.EqualTo("bob"));
    }

    [Test]
    public async Task List_GivenMultipleClients_WhenListingWithCursor_ThenPaginated()
    {
        var fixture = await SetUpAsync();
        await fixture.SetUpClientAsync(new Client("alice", ValidAddress()));
        await fixture.SetUpClientAsync(new Client("bob", ValidAddress()));
        await fixture.SetUpClientAsync(new Client("charlie", ValidAddress()));
        var first = await fixture.Repo.ListAsync(2);

        var second = await fixture.Repo.ListAsync(2, first.NextStartAfter);

        Assert.That(second.Items, Has.Count.EqualTo(1));
        Assert.That(second.Items[0].Nickname, Is.EqualTo("charlie"));
    }

    [Test]
    public async Task List_GivenNoClients_WhenListing_ThenReturnsEmpty()
    {
        var fixture = await SetUpAsync();

        var result = await fixture.Repo.ListAsync(10);

        Assert.That(result.Items, Is.Empty);
        Assert.That(result.NextStartAfter, Is.Null);
    }

    [Test]
    public async Task Latest_GivenClientsWithInvoices_WhenGettingLatest_ThenClientsRetrievedLatestFirst()
    {
        var fixture = await SetUpAsync();
        var olderDate = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc);
        var newerDate = new DateTime(2026, 2, 23, 0, 0, 0, DateTimeKind.Utc);
        var clientA = new Client("a", ValidAddress("a Corp"));
        var clientTest = new Client("test", ValidAddress("Test Corp"));
        await fixture.SetUpClientAsync(clientA);
        await fixture.SetUpClientAsync(clientTest);
        await fixture.SetUpInvoiceAsync(BuildInvoiceContent(olderDate, clientA.Address));
        await fixture.SetUpInvoiceAsync(BuildInvoiceContent(newerDate, clientTest.Address));

        var result = await fixture.Repo.LatestAsync(5);

        Assert.That(result.Items, Has.Count.EqualTo(2));
        Assert.That(result.Items[0].Nickname, Is.EqualTo("test"));
        Assert.That(result.Items[1].Nickname, Is.EqualTo("a"));
        Assert.That(result.NextStartAfter, Is.Not.Null);
    }

    [Test]
    public async Task Latest_GivenClientsWithInvoices_WhenGettingLatestWithLimit_ThenLimited()
    {
        var fixture = await SetUpAsync();
        var baseDate = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc);
        var clientA = new Client("a", ValidAddress("a Corp"));
        var clientB = new Client("b", ValidAddress("b Corp"));
        var clientC = new Client("c", ValidAddress("c Corp"));
        await fixture.SetUpClientAsync(clientA);
        await fixture.SetUpClientAsync(clientB);
        await fixture.SetUpClientAsync(clientC);
        // Create invoices in chronological order (repo requires date >= last invoice)
        await fixture.SetUpInvoiceAsync(BuildInvoiceContent(baseDate, clientC.Address));
        await fixture.SetUpInvoiceAsync(BuildInvoiceContent(baseDate.AddDays(1), clientB.Address));
        await fixture.SetUpInvoiceAsync(BuildInvoiceContent(baseDate.AddDays(2), clientA.Address));

        var result = await fixture.Repo.LatestAsync(2);

        Assert.That(result.Items, Has.Count.EqualTo(2));
        Assert.That(result.Items[0].Nickname, Is.EqualTo("a"));
        Assert.That(result.Items[1].Nickname, Is.EqualTo("b"));
    }

    [Test]
    public async Task Latest_GivenClientsWithNoInvoices_WhenGettingLatest_ThenClientsAppearLast()
    {
        var fixture = await SetUpAsync();
        var clientWithInv = new Client("with", ValidAddress("With Corp"));
        var clientNoInv = new Client("noinv", ValidAddress("NoInv Corp"));
        await fixture.SetUpClientAsync(clientWithInv);
        await fixture.SetUpClientAsync(clientNoInv);
        await fixture.SetUpInvoiceAsync(BuildInvoiceContent(new DateTime(2026, 2, 23), clientWithInv.Address));

        var result = await fixture.Repo.LatestAsync(5);

        Assert.That(result.Items, Has.Count.EqualTo(2));
        Assert.That(result.Items[0].Nickname, Is.EqualTo("with"));
        Assert.That(result.Items[1].Nickname, Is.EqualTo("noinv"));
    }

    [Test]
    public async Task Latest_GivenNoClients_WhenGettingLatest_ThenReturnsEmpty()
    {
        var fixture = await SetUpAsync();

        var result = await fixture.Repo.LatestAsync(5);

        Assert.That(result.Items, Is.Empty);
        Assert.That(result.NextStartAfter, Is.Null);
    }

    private static readonly BankTransferInfo TestBankInfo = new("BG00TEST00000000000000", "Test Bank", "TESTBGSF");

    private static Invoice.InvoiceContent BuildInvoiceContent(DateTime date, BillingAddress buyerAddress) =>
        new(date,
            new BillingAddress("Seller", "Rep", "123", null, "Addr", "City", "1000", "BG"),
            buyerAddress,
            [new Invoice.LineItem("Item", new Amount(100, Currency.Eur))],
            TestBankInfo);
}
