using Database;
using Database.Entities;
using Microsoft.EntityFrameworkCore;
using Orders;
using Utilities;

namespace Web;

public static class SchedulingIdentitySeed
{
    public static async Task SeedAsync(IServiceProvider services, IWebHostEnvironment environment, CancellationToken ct = default)
    {
        if (!ShouldSeed(environment))
            return;

        using var scope = services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<Func<AppDbContext>>();
        var hasher = scope.ServiceProvider.GetRequiredService<PinHasher>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        await using var ctx = contextFactory();
        var now = clock.UtcNow;

        if (!await ctx.SchedulingLabs.AnyAsync(ct))
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

        await EnsureClinicAsync(ctx, "DEMO", "Demo Dental Clinic", "demo-client", "#7c3aed", now, ct);
        await EnsureClinicAsync(ctx, "OTHER", "Other Clinic", null, "#0ea5e9", now, ct);
        await EnsureMemberAsync(ctx, OrganizationType.Lab, "LAB", "lab-1", "Lab Member 1", "654321", hasher, now, ct);
        await EnsureMemberAsync(ctx, OrganizationType.Clinic, "DEMO", "assistant-1", "Assistant 1", "123456", hasher, now, ct);

        await ctx.SaveChangesAsync(ct);
    }

    private static bool ShouldSeed(IWebHostEnvironment environment) =>
        environment.IsDevelopment()
        || string.Equals(environment.EnvironmentName, "LanDev", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environment.EnvironmentName, "Test", StringComparison.OrdinalIgnoreCase);

    private static async Task EnsureClinicAsync(AppDbContext ctx, string code, string displayName, string? linkedClientNickname, string? displayColor, DateTimeOffset now, CancellationToken ct)
    {
        var clinic = await ctx.SchedulingClinics.FirstOrDefaultAsync(x => x.Code == code, ct);
        if (clinic != null)
            return;

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

    private static async Task EnsureMemberAsync(AppDbContext ctx, OrganizationType organizationType, string organizationCode, string id, string label, string pin, PinHasher hasher, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await ctx.SchedulingMembers.FirstOrDefaultAsync(x => x.OrganizationType == organizationType && x.OrganizationCode == organizationCode && x.Id == id, ct);
        if (existing != null)
            return;

        ctx.SchedulingMembers.Add(new SchedulingMemberEntity
        {
            OrganizationType = organizationType,
            OrganizationCode = organizationCode,
            Id = id,
            Label = label,
            PinHash = hasher.Hash(pin),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
    }
}
