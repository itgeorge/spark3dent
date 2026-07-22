using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Database;
using Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Orders;
using Utilities;
using Web;

namespace Web.Tests;

public class ApiTestFixture : WebApplicationFactory<Program>
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly string _blobPath;
    private readonly string _startInvoiceNumber;
    private readonly string _runtimeHostingMode;
    private readonly string? _runtimePort;
    private readonly string? _openAiKey;
    private readonly IInvoiceImporter? _invoiceImporterOverride;
    private readonly Utilities.ILogger? _loggerOverride;
    private readonly bool _autoLoginAsLab;
    private readonly bool _seedIdentity;
    private readonly IClock? _clockOverride;
    private bool _identitySeeded;

    public ApiTestFixture(
        string startInvoiceNumber = "1",
        string runtimeHostingMode = "Desktop",
        string? runtimePort = "0",
        string? openAiKey = null,
        IInvoiceImporter? invoiceImporterOverride = null,
        Utilities.ILogger? loggerOverride = null,
        bool autoLoginAsLab = false,
        bool seedIdentity = true,
        IClock? clockOverride = null)
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WebTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
        _blobPath = Path.Combine(_tempDir, "blob");
        _startInvoiceNumber = startInvoiceNumber;
        _runtimeHostingMode = runtimeHostingMode;
        _runtimePort = runtimePort;
        _openAiKey = openAiKey;
        _invoiceImporterOverride = invoiceImporterOverride;
        _loggerOverride = loggerOverride;
        _autoLoginAsLab = autoLoginAsLab;
        _seedIdentity = seedIdentity;
        _clockOverride = clockOverride;
    }

    public string DbPath => _dbPath;

    public HttpClient Client
    {
        get
        {
            var client = CreateClient();
            if (_autoLoginAsLab)
                LoginAsLabAsync(client).GetAwaiter().GetResult();
            return client;
        }
    }

    public static async Task LoginAsLabAsync(HttpClient client)
    {
        var response = await client.PostAsync(
            "/api/scheduling/auth/login",
            new StringContent("{\"organizationCode\":\"LAB\",\"pin\":\"654321\"}", System.Text.Encoding.UTF8, "application/json"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var cookie = response.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("s3d_order_session="));
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var inMemory = new Dictionary<string, string?>
            {
                ["Runtime:HostingMode"] = _runtimeHostingMode,
                ["SingleBox:DatabasePath"] = _dbPath,
                ["SingleBox:BlobStoragePath"] = _blobPath,
                ["SingleBox:LogDirectory"] = _tempDir,
                ["App:StartInvoiceNumber"] = _startInvoiceNumber
            };
            if (_runtimePort != null)
                inMemory["Runtime:Port"] = _runtimePort;
            if (_openAiKey != null)
                inMemory["App:OpenAiKey"] = _openAiKey;
            config.AddInMemoryCollection(inMemory);
        });

        builder.ConfigureTestServices(services =>
        {
            if (_loggerOverride != null)
            {
                services.RemoveAll<Utilities.ILogger>();
                services.AddSingleton(_loggerOverride);
            }
            if (_invoiceImporterOverride != null)
            {
                services.RemoveAll<IInvoiceImporter>();
                services.AddSingleton(_invoiceImporterOverride);
            }
            if (_clockOverride != null)
            {
                services.RemoveAll<IClock>();
                services.AddSingleton(_clockOverride);
            }
            // Use LegacyOnlyInvoiceParser so API tests don't need OpenAI key
            services.RemoveAll<ILegacyInvoiceParser>();
            services.AddSingleton<ILegacyInvoiceParser, LegacyOnlyInvoiceParser>();
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureHostConfiguration(config =>
        {
            var inMemory = new Dictionary<string, string?>
            {
                ["Runtime:HostingMode"] = _runtimeHostingMode,
                ["SingleBox:DatabasePath"] = _dbPath,
                ["SingleBox:BlobStoragePath"] = _blobPath,
                ["SingleBox:LogDirectory"] = _tempDir,
                ["App:StartInvoiceNumber"] = _startInvoiceNumber
            };
            if (_runtimePort != null)
                inMemory["Runtime:Port"] = _runtimePort;
            if (_openAiKey != null)
                inMemory["App:OpenAiKey"] = _openAiKey;
            config.AddInMemoryCollection(inMemory);
        });
        var host = base.CreateHost(builder);
        if (_seedIdentity && !_identitySeeded)
        {
            SeedSchedulingIdentityAsync(_dbPath).GetAwaiter().GetResult();
            _identitySeeded = true;
        }
        return host;
    }

    public static async Task SeedSchedulingIdentityAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        await using var ctx = new AppDbContext(options);
        await ctx.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var hasher = new PinHasher();

        if (!await ctx.SchedulingLabs.AnyAsync())
        {
            ctx.SchedulingLabs.Add(new SchedulingLabEntity
            {
                Id = 1,
                Code = "LAB",
                DisplayName = "Spark3Dent Lab",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await EnsureClinicAsync(ctx, "DEMO", "Demo Dental Clinic", "demo-client", "#7c3aed", now);
        await EnsureClinicAsync(ctx, "OTHER", "Other Clinic", null, "#0ea5e9", now);
        await EnsureMemberAsync(ctx, OrganizationType.Lab, "LAB", "lab-1", "Lab Member 1", "654321", hasher, now);
        await EnsureMemberAsync(ctx, OrganizationType.Clinic, "DEMO", "assistant-1", "Assistant 1", "123456", hasher, now);
        await EnsureMemberAsync(ctx, OrganizationType.Clinic, "DEMO", "assistant-2", "Assistant 2", "222222", hasher, now);
        await EnsureMemberAsync(ctx, OrganizationType.Clinic, "OTHER", "other-1", "Other Member 1", "111111", hasher, now);
        await ctx.SaveChangesAsync();
    }

    private static async Task EnsureClinicAsync(AppDbContext ctx, string code, string displayName, string? linkedClientNickname, string? displayColor, DateTimeOffset now)
    {
        if (await ctx.SchedulingClinics.AnyAsync(x => x.Code == code)) return;
        ctx.SchedulingClinics.Add(new SchedulingClinicEntity
        {
            Code = code,
            DisplayName = displayName,
            LinkedClientNickname = linkedClientNickname,
            DisplayColor = displayColor,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private static async Task EnsureMemberAsync(AppDbContext ctx, OrganizationType organizationType, string organizationCode, string id, string label, string secret, PinHasher hasher, DateTimeOffset now)
    {
        if (await ctx.SchedulingMembers.AnyAsync(x => x.OrganizationType == organizationType && x.OrganizationCode == organizationCode && x.Id == id)) return;
        ctx.SchedulingMembers.Add(new SchedulingMemberEntity
        {
            OrganizationType = organizationType,
            OrganizationCode = organizationCode,
            Id = id,
            Label = label,
            PinHash = hasher.Hash(secret, iterations: 10_000),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { /* ignore */ }
        }
        base.Dispose(disposing);
    }

    public static async Task AssertJsonErrorAsync(HttpResponseMessage response, HttpStatusCode expectedStatusCode)
    {
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.TryGetProperty("error", out var err), Is.True);
        Assert.That(err.GetString(), Is.Not.Null.And.Not.Empty);
    }
}
