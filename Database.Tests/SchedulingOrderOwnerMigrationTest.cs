using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Database.Entities;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Orders;
using Utilities;

namespace Database.Tests;

public class SchedulingOrderOwnerMigrationTest
{
    [Test]
    public async Task ReportValidateApply_ReassignsOwnerAndWritesAudit()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OrderOwnerMigrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "test.db");
        var reportPath = Path.Combine(tempDir, "report.json");
        var reviewedPath = Path.Combine(tempDir, "reviewed.json");
        var applyPath = Path.Combine(tempDir, "apply.json");

        try
        {
            await SeedSchedulingIdentityAsync(dbPath);
            await SeedOrderAsync(dbPath, "26-0605-Z1AA", "Case A", "DEMO", "lab-1", "Lab Member 1");

            var report = await SchedulingOrderOwnerMigration.GenerateReportAsync(dbPath);
            Assert.That(report.Orders, Has.Count.EqualTo(1));
            Assert.That(report.Orders[0].CurrentMemberId, Is.EqualTo("lab-1"));
            Assert.That(report.Orders[0].CurrentMemberMatchesActiveClinicMember, Is.False);
            Assert.That(report.Orders[0].ActiveClinicMembers.Select(m => m.MemberId), Does.Contain("assistant-1"));
            await File.WriteAllTextAsync(reportPath, SchedulingOrderOwnerMigration.SerializeReport(report));

            var reviewed = report with
            {
                Orders =
                [
                    report.Orders[0] with { TargetMemberId = "assistant-1" }
                ]
            };
            await File.WriteAllTextAsync(reviewedPath, SchedulingOrderOwnerMigration.SerializeReport(reviewed));

            var validation = await SchedulingOrderOwnerMigration.ValidateAsync(dbPath, reviewedPath);
            Assert.That(validation.Errors, Is.Empty);
            Assert.That(validation.Summary.UpdateCount, Is.EqualTo(1));

            await using (var beforeCtx = OpenDb(dbPath))
            {
                var before = await beforeCtx.SchedulingOrders.SingleAsync();
                Assert.That(before.MemberPinHashFingerprint, Is.EqualTo(string.Empty));
            }

            var apply = await SchedulingOrderOwnerMigration.ApplyAsync(dbPath, reviewedPath, "reviewed.json", backupConfirmed: true);
            Assert.That(apply.Errors, Is.Empty);
            Assert.That(apply.UpdatedOrders, Has.Count.EqualTo(1));
            Assert.That(apply.UpdatedOrders[0].NewMemberId, Is.EqualTo("assistant-1"));
            await File.WriteAllTextAsync(applyPath, SchedulingOrderOwnerMigration.SerializeApplyResult(apply));

            await using var ctx = OpenDb(dbPath);
            var order = await ctx.SchedulingOrders.SingleAsync();
            Assert.That(order.MemberId, Is.EqualTo("assistant-1"));
            Assert.That(order.MemberLabel, Is.EqualTo("Assistant 1"));
            Assert.That(order.MemberPinHashFingerprint, Is.EqualTo(string.Empty));

            var audit = await ctx.AuditEvents.SingleAsync(e => e.Operation == "OrderOwnerReassigned");
            Assert.That(audit.ServiceName, Is.EqualTo("Scheduling"));
            Assert.That(audit.ActorOrganizationType, Is.EqualTo("System"));
            Assert.That(audit.MetadataJson, Does.Contain("assistant-1"));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
        }
    }

    [Test]
    public async Task Validate_FailsWhenBlankTargetAndCurrentMemberIsNotActiveClinicMember()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OrderOwnerMigrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "test.db");
        var reviewedPath = Path.Combine(tempDir, "reviewed.json");

        try
        {
            await SeedSchedulingIdentityAsync(dbPath);
            await SeedOrderAsync(dbPath, "26-0605-Z1AA", "Case A", "DEMO", "lab-1", "Lab Member 1");

            var report = await SchedulingOrderOwnerMigration.GenerateReportAsync(dbPath);
            await File.WriteAllTextAsync(reviewedPath, SchedulingOrderOwnerMigration.SerializeReport(report));

            var validation = await SchedulingOrderOwnerMigration.ValidateAsync(dbPath, reviewedPath);
            Assert.That(validation.Errors, Has.Count.EqualTo(1));
            Assert.That(validation.Errors[0], Does.Contain("lab-1"));
            Assert.That(validation.Errors[0], Does.Contain("targetMemberId"));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
        }
    }

    [Test]
    public async Task Validate_FailsWhenCurrentMemberMismatchWithoutForce()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OrderOwnerMigrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "test.db");
        var reviewedPath = Path.Combine(tempDir, "reviewed.json");

        try
        {
            await SeedSchedulingIdentityAsync(dbPath);
            await SeedOrderAsync(dbPath, "26-0605-Z1AA", "Case A", "DEMO", "lab-1", "Lab Member 1");

            var report = await SchedulingOrderOwnerMigration.GenerateReportAsync(dbPath);
            var reviewed = report with
            {
                Orders =
                [
                    report.Orders[0] with
                    {
                        CurrentMemberId = "assistant-1",
                        TargetMemberId = "assistant-1"
                    }
                ]
            };
            await File.WriteAllTextAsync(reviewedPath, SchedulingOrderOwnerMigration.SerializeReport(reviewed));

            var validation = await SchedulingOrderOwnerMigration.ValidateAsync(dbPath, reviewedPath);
            Assert.That(validation.Errors, Is.Not.Empty);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
        }
    }

    [Test]
    public async Task Apply_IsIdempotentWhenTargetAlreadyApplied()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OrderOwnerMigrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "test.db");
        var reviewedPath = Path.Combine(tempDir, "reviewed.json");

        try
        {
            await SeedSchedulingIdentityAsync(dbPath);
            await SeedOrderAsync(dbPath, "26-0605-Z1AA", "Case A", "DEMO", "assistant-1", "Assistant 1");

            var report = await SchedulingOrderOwnerMigration.GenerateReportAsync(dbPath);
            var reviewed = report with
            {
                Orders =
                [
                    report.Orders[0] with { TargetMemberId = "assistant-1" }
                ]
            };
            await File.WriteAllTextAsync(reviewedPath, SchedulingOrderOwnerMigration.SerializeReport(reviewed));

            var apply = await SchedulingOrderOwnerMigration.ApplyAsync(dbPath, reviewedPath, "reviewed.json", backupConfirmed: true);
            Assert.That(apply.Errors, Is.Empty);
            Assert.That(apply.UpdatedOrders, Is.Empty);
            Assert.That(apply.Summary.NoChangeCount, Is.EqualTo(1));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
        }
    }

    private static async Task SeedOrderAsync(
        string dbPath,
        string code,
        string caseName,
        string clinicCode,
        string memberId,
        string memberLabel)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        await using (var ctx = new AppDbContext(options))
            await ctx.Database.MigrateAsync();
        var repo = new SqliteOrderRepo(() => new AppDbContext(options));
        var timestamp = DateTimeOffset.Parse("2026-06-02T12:00:00Z");
        await repo.CreateOrderAsync(new OrderRecord(
            0,
            code,
            clinicCode,
            "Demo Dental Clinic",
            memberId,
            memberLabel,
            caseName,
            new DateOnly(2026, 6, 2),
            ProductCategory.Permanent,
            Material.FullContourZirconia,
            [new OrderWorkItem(ConstructionType.Crown, new ToothRange(11, 11))],
            new DateOnly(2026, 6, 5),
            OrderStatus.Created,
            Shade.Unspecified,
            null,
            timestamp,
            timestamp,
            "127.0.0.1",
            "test",
            null,
            1.0m));
    }

    private static AppDbContext OpenDb(string dbPath)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task SeedSchedulingIdentityAsync(string dbPath)
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

        await EnsureClinicAsync(ctx, "DEMO", "Demo Dental Clinic", now);
        await EnsureMemberAsync(ctx, OrganizationType.Lab, "LAB", "lab-1", "Lab Member 1", "654321", hasher, now);
        await EnsureMemberAsync(ctx, OrganizationType.Clinic, "DEMO", "assistant-1", "Assistant 1", "123456", hasher, now);
        await ctx.SaveChangesAsync();
    }

    private static async Task EnsureClinicAsync(AppDbContext ctx, string code, string displayName, DateTimeOffset now)
    {
        if (await ctx.SchedulingClinics.AnyAsync(x => x.Code == code)) return;
        ctx.SchedulingClinics.Add(new SchedulingClinicEntity
        {
            Code = code,
            DisplayName = displayName,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private static async Task EnsureMemberAsync(
        AppDbContext ctx,
        OrganizationType organizationType,
        string organizationCode,
        string id,
        string label,
        string secret,
        PinHasher hasher,
        DateTimeOffset now)
    {
        if (await ctx.SchedulingMembers.AnyAsync(x => x.OrganizationType == organizationType && x.OrganizationCode == organizationCode && x.Id == id))
            return;
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
}
