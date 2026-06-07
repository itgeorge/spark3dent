using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Orders;

namespace Database.Tests;

[TestFixture]
public class SqliteSchedulingIdentityRepoTest
{
    private string _dbPath = null!;
    private Func<AppDbContext> _contextFactory = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "Spark3Dent", "SqliteSchedulingIdentityRepoTest", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        await using (var ctx = new AppDbContext(options))
            await ctx.Database.MigrateAsync();
        _contextFactory = () => new AppDbContext(options);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    [Test]
    public async Task BootstrapLabAsync_CreatesLabAndMember_AndRefusesWithoutReset()
    {
        var repo = new SqliteSchedulingIdentityRepo(_contextFactory);
        var now = DateTimeOffset.Parse("2026-06-08T10:00:00Z");

        var lab = await repo.BootstrapLabAsync(new LabBootstrapRequest("LAB", "Spark3Dent Lab", "lab-1", "Lab Admin", "hash-1", now), reset: false);

        Assert.That(lab.Code, Is.EqualTo("LAB"));
        var member = await repo.GetMemberAsync(OrganizationType.Lab, "LAB", "lab-1");
        Assert.That(member, Is.Not.Null);
        Assert.That(member!.PinHash, Is.EqualTo("hash-1"));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await repo.BootstrapLabAsync(new LabBootstrapRequest("LAB2", "Other", "lab-2", "Other", "hash-2", now), reset: false));
    }

    [Test]
    public async Task BootstrapLabAsync_WithReset_UpdatesLabMemberAndRevokesLabSessions()
    {
        var repo = new SqliteSchedulingIdentityRepo(_contextFactory);
        var sessions = new SqliteAuthSessionRepo(_contextFactory);
        var now = DateTimeOffset.Parse("2026-06-08T10:00:00Z");
        await repo.BootstrapLabAsync(new LabBootstrapRequest("LAB", "Spark3Dent Lab", "lab-1", "Lab Admin", "hash-1", now), reset: false);
        await sessions.AddSessionAsync(new AuthSession("session-1", OrganizationType.Lab, "LAB", "lab-1", "token", now, now, now.AddDays(1), null, null, "127.0.0.1", "test"));

        var resetAt = now.AddHours(1);
        var lab = await repo.BootstrapLabAsync(new LabBootstrapRequest("OPS", "Ops Lab", "owner", "Owner", "hash-2", resetAt), reset: true);

        Assert.That(lab.Code, Is.EqualTo("OPS"));
        Assert.That(await repo.GetMemberAsync(OrganizationType.Lab, "OPS", "owner"), Is.Not.Null);
        Assert.That(await repo.GetMemberAsync(OrganizationType.Lab, "OPS", "lab-1", includeInactive: true), Is.Not.Null);
        Assert.That((await repo.GetMemberAsync(OrganizationType.Lab, "OPS", "lab-1", includeInactive: true))!.IsActive, Is.False);
        await using var ctx = _contextFactory();
        var session = await ctx.SchedulingAuthSessions.SingleAsync();
        Assert.That(session.OrganizationCode, Is.EqualTo("OPS"));
        Assert.That(session.RevokedAt, Is.EqualTo(resetAt));
    }

    [Test]
    public async Task ClinicAndMemberMutations_SoftDeactivateAndUpdateSecrets()
    {
        var repo = new SqliteSchedulingIdentityRepo(_contextFactory);
        var now = DateTimeOffset.Parse("2026-06-08T10:00:00Z");
        await repo.BootstrapLabAsync(new LabBootstrapRequest("LAB", "Lab", "lab-1", "Lab", "hash-lab", now), reset: false);

        var clinic = await repo.CreateClinicWithInitialMemberAsync(
            new ClinicCreateRequest("DEMO", "Demo", null, "#7c3aed", now),
            new MemberCreateRequest("assistant-1", "Assistant", "hash-1", now));
        Assert.That(clinic.Code, Is.EqualTo("DEMO"));

        await repo.UpdateClinicAsync("demo", new ClinicUpdateRequest("Demo Updated", "client-a", "#0ea5e9", now.AddMinutes(1)));
        Assert.That((await repo.GetClinicAsync("DEMO", includeInactive: true))!.DisplayName, Is.EqualTo("Demo Updated"));

        var member = await repo.CreateMemberAsync(OrganizationType.Clinic, "DEMO", new MemberCreateRequest("assistant-2", "Assistant 2", "hash-2", now));
        Assert.That(member.Id, Is.EqualTo("assistant-2"));
        await repo.UpdateMemberSecretAsync(OrganizationType.Clinic, "demo", "ASSISTANT-2", "hash-3", now);
        Assert.That((await repo.GetMemberAsync(OrganizationType.Clinic, "DEMO", "assistant-2"))!.PinHash, Is.EqualTo("hash-3"));

        await repo.SetMemberActiveAsync(OrganizationType.Clinic, "DEMO", "assistant-2", false, now);
        Assert.That(await repo.GetMemberAsync(OrganizationType.Clinic, "DEMO", "assistant-2"), Is.Null);
        Assert.That((await repo.GetMemberAsync(OrganizationType.Clinic, "DEMO", "assistant-2", includeInactive: true))!.IsActive, Is.False);

        await repo.SetClinicActiveAsync("DEMO", false, now);
        Assert.That(await repo.GetClinicAsync("DEMO"), Is.Null);
        Assert.That((await repo.GetClinicAsync("DEMO", includeInactive: true))!.IsActive, Is.False);
    }
}
