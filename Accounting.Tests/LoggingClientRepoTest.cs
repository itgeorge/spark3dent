using System;
using System.Threading.Tasks;
using Accounting.Tests.Fakes;
using Invoices;
using NUnit.Framework;
using Utilities;
using Utilities.Tests;

namespace Accounting.Tests;

[TestFixture]
[TestOf(typeof(LoggingClientRepo))]
public class LoggingClientRepoTest
{
    [Test]
    public async Task AddAndGet_WhenWrappingFake_ThenDelegatesAndReturnsResult()
    {
        var inner = new FakeClientRepo();
        var logger = new CapturingLogger();
        var sut = new LoggingClientRepo(inner, logger);

        var client = new Client("acme", ValidAddress());
        await sut.AddAsync(client);
        var retrieved = await sut.GetAsync("acme");

        Assert.That(retrieved.Nickname, Is.EqualTo("acme"));
        Assert.That(retrieved.Address.Name, Is.EqualTo(ValidAddress().Name));
        Assert.That(logger.InfoMessages, Does.Contain("ClientRepo.AddAsync nickname=acme"));
        Assert.That(logger.InfoMessages, Does.Contain("ClientRepo.GetAsync nickname=acme"));
    }

    [Test]
    public void GetAsync_WhenInnerThrows_ThenPropagatesExceptionAndLogsError()
    {
        var inner = new FakeClientRepo();
        var logger = new CapturingLogger();
        var sut = new LoggingClientRepo(inner, logger);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await sut.GetAsync("nonexistent"));
        Assert.That(ex!.Message, Does.Contain("nonexistent"));
        Assert.That(logger.InfoMessages, Does.Contain("ClientRepo.GetAsync nickname=nonexistent"));
        Assert.That(logger.ErrorEntries, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AddAsync_WhenLoggerThrows_ThenOperationSucceedsAnyway()
    {
        var inner = new FakeClientRepo();
        var logger = new ThrowingLogger();
        var sut = new LoggingClientRepo(inner, logger);

        var client = new Client("acme", ValidAddress());
        await sut.AddAsync(client);
        var retrieved = await sut.GetAsync("acme");

        Assert.That(retrieved.Nickname, Is.EqualTo("acme"));
    }

    private static BillingAddress ValidAddress() => new(
        "Test Company",
        "Representative",
        "123456789",
        null,
        "Address",
        "City",
        "1000",
        "BG");
}
