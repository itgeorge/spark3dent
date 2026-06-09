using System.Net;
using System.Text;
using System.Text.Json;
using Database;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Web.Tests;

[TestFixture]
public class AuditApiTests
{
    [Test]
    public async Task ClientAndInvoiceMutations_AppendAuditRowsWithLabActor()
    {
        using var fixture = new ApiTestFixture(autoLoginAsLab: true);
        var client = fixture.Client;

        var createClient = new
        {
            nickname = "acme",
            name = "ACME Ltd",
            companyIdentifier = "BG123",
            address = "Main 1",
            city = "Sofia"
        };
        Assert.That((await client.PostAsync("/api/invoicing/clients", JsonContent(createClient))).StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var updateClient = new { name = "ACME Dental Ltd" };
        Assert.That((await client.PutAsync("/api/invoicing/clients/acme", JsonContent(updateClient))).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var issueBody = new { clientNickname = "acme", amountCents = 12345, date = "2026-02-20" };
        var issueResponse = await client.PostAsync("/api/invoicing/invoices/issue", JsonContent(issueBody));
        Assert.That(issueResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var invoiceNumber = JsonDocument.Parse(await issueResponse.Content.ReadAsStringAsync())
            .RootElement.GetProperty("invoice").GetProperty("number").GetString()!;

        var correctBody = new { invoiceNumber, amountCents = 20000, date = "2026-02-21" };
        Assert.That((await client.PostAsync("/api/invoicing/invoices/correct", JsonContent(correctBody))).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var rows = await LoadAuditRowsAsync(fixture.DbPath);
        Assert.That(rows.Select(r => r.Operation), Is.SupersetOf(new[] { "ClientCreated", "ClientUpdated", "InvoiceIssued", "InvoiceCorrected" }));
        Assert.That(rows.Where(r => r.Operation is "ClientCreated" or "ClientUpdated" or "InvoiceIssued" or "InvoiceCorrected").Select(r => r.ActorOrganizationType), Is.All.EqualTo("Lab"));
        Assert.That(rows.Single(r => r.Operation == "InvoiceIssued").ActorMemberId, Is.EqualTo("lab-1"));
        Assert.That(rows.Single(r => r.Operation == "ClientCreated").MetadataJson, Does.Not.Contain("123456").And.Not.Contain("654321"));

        var auditResponse = await client.GetAsync("/api/invoicing/audit?entityType=Invoice&limit=10");
        Assert.That(auditResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var auditJson = JsonDocument.Parse(await auditResponse.Content.ReadAsStringAsync());
        Assert.That(auditJson.RootElement.GetProperty("items").GetArrayLength(), Is.EqualTo(2));
    }

    [Test]
    public async Task InvoiceImportCommit_AppendsAuditRowAfterSuccessfulNonDryRunCommit()
    {
        var fakeImporter = new FakeInvoiceImporter(new ImportCommitResponse(
            1,
            2,
            0,
            [new ImportCommitItemStatus("invoice.pdf", "imported", null)]));
        using var fixture = new ApiTestFixture(
            openAiKey: "sk-dummy",
            invoiceImporterOverride: fakeImporter,
            autoLoginAsLab: true);

        var body = new { items = Array.Empty<object>(), nicknameMap = new Dictionary<string, string>(), dryRun = false };
        var response = await fixture.Client.PostAsync("/api/invoicing/invoices/import/commit", JsonContent(body));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var rows = await LoadAuditRowsAsync(fixture.DbPath);
        var audit = rows.Single(r => r.Operation == "InvoiceImportCommitted");
        Assert.That(audit.ServiceName, Is.EqualTo("Invoicing"));
        Assert.That(audit.EntityType, Is.EqualTo("InvoiceImport"));
        Assert.That(audit.ActorOrganizationType, Is.EqualTo("Lab"));
        Assert.That(audit.MetadataJson, Does.Contain("\"imported\":1"));
    }

    [Test]
    public async Task FailedInvoicingValidation_DoesNotAppendAuditRow()
    {
        using var fixture = new ApiTestFixture(autoLoginAsLab: true);
        var response = await fixture.Client.PostAsync("/api/invoicing/clients", JsonContent(new { nickname = "bad" }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var rows = await LoadAuditRowsAsync(fixture.DbPath);
        Assert.That(rows, Is.Empty);
    }

    private static StringContent JsonContent(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static async Task<List<Database.Entities.AuditEventEntity>> LoadAuditRowsAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        await using var ctx = new AppDbContext(options);
        return await ctx.AuditEvents.OrderBy(e => e.Id).ToListAsync();
    }

    private sealed class FakeInvoiceImporter : IInvoiceImporter
    {
        private readonly ImportCommitResponse _commitResponse;

        public FakeInvoiceImporter(ImportCommitResponse commitResponse)
        {
            _commitResponse = commitResponse;
        }

        public Task<ImportAnalyzeResponse> AnalyzeAsync(ImportAnalyzeRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImportAnalyzeResponse([], []));

        public Task<ImportCommitResponse> CommitAsync(ImportCommitRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(_commitResponse);
    }
}
