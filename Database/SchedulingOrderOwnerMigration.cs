using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Database;
using Database.Entities;
using Microsoft.EntityFrameworkCore;
using Orders;
using Utilities;

namespace Database;

public static class SchedulingOrderOwnerMigration
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<OrderOwnerReportDocument> GenerateReportAsync(string dbPath, CancellationToken ct = default)
    {
        await using var ctx = await OpenContextAsync(dbPath, ct);
        var orders = await ctx.SchedulingOrders.AsNoTracking()
            .OrderBy(o => o.ClinicCode)
            .ThenBy(o => o.OrderCode)
            .ToListAsync(ct);

        var membersByClinic = await LoadActiveClinicMembersByClinicAsync(ctx, ct);
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var items = orders.Select(order =>
        {
            var clinicCode = OrganizationCodes.Normalize(order.ClinicCode);
            var activeMembers = membersByClinic.GetValueOrDefault(clinicCode) ?? [];
            var currentMemberId = order.MemberId.Trim();
            var matchesActive = activeMembers.Any(m => MemberIdsEqual(m.MemberId, currentMemberId));
            return new OrderOwnerAssignmentItem(
                order.Id,
                order.OrderCode,
                order.Status,
                clinicCode,
                order.ClinicDisplayName,
                currentMemberId,
                order.MemberLabel,
                order.CaseName,
                order.RequestedDeliveryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                order.CreatedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                matchesActive,
                activeMembers,
                string.Empty);
        }).ToList();

        return new OrderOwnerReportDocument(generatedAtUtc, items);
    }

    public static async Task<OrderOwnerValidationResult> ValidateAsync(
        string dbPath,
        string assignmentsPath,
        bool forceCurrentMismatch = false,
        CancellationToken ct = default)
    {
        var assignments = await ReadAssignmentsAsync(assignmentsPath, ct);
        await using var ctx = await OpenContextAsync(dbPath, ct);
        var ordersById = await ctx.SchedulingOrders.AsNoTracking().ToDictionaryAsync(o => o.Id, ct);
        var ordersByCode = ordersById.Values.ToDictionary(o => NormalizeOrderCode(o.OrderCode), StringComparer.OrdinalIgnoreCase);
        var membersByClinic = await LoadActiveClinicMembersByClinicAsync(ctx, ct);
        return ValidateAssignments(assignments, ordersById, ordersByCode, membersByClinic, forceCurrentMismatch);
    }

    public static async Task<OrderOwnerApplyResult> ApplyAsync(
        string dbPath,
        string assignmentsPath,
        string? assignmentsFileName,
        bool backupConfirmed,
        bool forceCurrentMismatch = false,
        CancellationToken ct = default)
    {
        if (!backupConfirmed)
            throw new InvalidOperationException("Apply requires --backup-confirmed after completing a database backup.");

        var assignments = await ReadAssignmentsAsync(assignmentsPath, ct);
        await using var ctx = await OpenContextAsync(dbPath, ct);
        var ordersById = await ctx.SchedulingOrders.ToDictionaryAsync(o => o.Id, ct);
        var ordersByCode = ordersById.Values.ToDictionary(o => NormalizeOrderCode(o.OrderCode), StringComparer.OrdinalIgnoreCase);
        var membersByClinic = await LoadActiveClinicMembersByClinicAsync(ctx, ct);
        var validation = ValidateAssignments(assignments, ordersById, ordersByCode, membersByClinic, forceCurrentMismatch);
        if (validation.Errors.Count > 0)
            return new OrderOwnerApplyResult(DateTimeOffset.UtcNow, [], validation.Summary, validation.Errors);

        var appliedAtUtc = DateTimeOffset.UtcNow;
        var updatedOrders = new List<OrderOwnerApplyItem>();
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);

        foreach (var item in validation.PlannedUpdates)
        {
            if (!ordersById.TryGetValue(item.OrderId, out var entity))
                continue;

            var oldMemberId = entity.MemberId;
            var oldMemberLabel = entity.MemberLabel;
            if (MemberIdsEqual(oldMemberId, item.NewMemberId))
                continue;

            entity.MemberId = item.NewMemberId;
            entity.MemberLabel = item.NewMemberLabel;
            updatedOrders.Add(new OrderOwnerApplyItem(
                entity.Id,
                entity.OrderCode,
                entity.ClinicCode,
                entity.CaseName,
                oldMemberId,
                oldMemberLabel,
                item.NewMemberId,
                item.NewMemberLabel));

            ctx.AuditEvents.Add(new AuditEventEntity
            {
                ServiceName = "Scheduling",
                Operation = "OrderOwnerReassigned",
                EntityType = "SchedulingOrder",
                EntityId = entity.OrderCode,
                EntityDisplay = entity.CaseName,
                ActorOrganizationType = "System",
                OccurredAt = appliedAtUtc,
                OccurredAtUnixTimeMilliseconds = appliedAtUtc.ToUnixTimeMilliseconds(),
                MetadataJson = JsonSerializer.Serialize(new
                {
                    source = "cli",
                    assignmentsFile = assignmentsFileName,
                    backupConfirmed = true,
                    oldMemberId,
                    oldMemberLabel,
                    newMemberId = item.NewMemberId,
                    newMemberLabel = item.NewMemberLabel
                })
            });
        }

        await ctx.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new OrderOwnerApplyResult(appliedAtUtc, updatedOrders, validation.Summary, []);
    }

    public static string SerializeReport(OrderOwnerReportDocument report) =>
        JsonSerializer.Serialize(report, JsonOptions);

    public static string SerializeApplyResult(OrderOwnerApplyResult result) =>
        JsonSerializer.Serialize(result, JsonOptions);

    public static void PrintValidationSummary(TextWriter output, OrderOwnerValidationSummary summary, IReadOnlyList<string> errors)
    {
        output.WriteLine($"Total orders in assignment file: {summary.TotalOrders}");
        output.WriteLine($"No change: {summary.NoChangeCount}");
        output.WriteLine($"To update: {summary.UpdateCount}");
        if (summary.UpdatesByClinic.Count > 0)
        {
            output.WriteLine("Per-clinic update counts:");
            foreach (var (clinicCode, count) in summary.UpdatesByClinic.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                output.WriteLine($"  {clinicCode}: {count}");
        }

        if (errors.Count == 0)
        {
            output.WriteLine("Validation passed.");
            return;
        }

        output.WriteLine($"Validation failed with {errors.Count} error(s):");
        foreach (var error in errors)
            output.WriteLine($"  - {error}");
    }

    private static OrderOwnerValidationResult ValidateAssignments(
        OrderOwnerReportDocument assignments,
        IReadOnlyDictionary<long, SchedulingOrderEntity> ordersById,
        IReadOnlyDictionary<string, SchedulingOrderEntity> ordersByCode,
        IReadOnlyDictionary<string, IReadOnlyList<OrderOwnerMemberRef>> membersByClinic,
        bool forceCurrentMismatch)
    {
        var errors = new List<string>();
        var seenOrderIds = new HashSet<long>();
        var seenOrderCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var noChangeCount = 0;
        var updateCount = 0;
        var updatesByClinic = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var plannedUpdates = new List<OrderOwnerPlannedUpdate>();

        foreach (var item in assignments.Orders)
        {
            if (!seenOrderIds.Add(item.OrderId))
                errors.Add($"Duplicate assignment entry for orderId {item.OrderId}.");
            if (!seenOrderCodes.Add(item.OrderCode))
                errors.Add($"Duplicate assignment entry for orderCode '{item.OrderCode}'.");

            if (!ordersById.TryGetValue(item.OrderId, out var order))
            {
                errors.Add($"Order id {item.OrderId} was not found.");
                continue;
            }

            if (!OrderCodesEqual(order.OrderCode, item.OrderCode))
            {
                errors.Add($"Order id {item.OrderId} has code '{order.OrderCode}', but assignment file has '{item.OrderCode}'.");
                continue;
            }

            if (!ordersByCode.TryGetValue(NormalizeOrderCode(item.OrderCode), out var orderByCode) || orderByCode.Id != order.Id)
            {
                errors.Add($"Order code '{item.OrderCode}' does not match order id {item.OrderId}.");
                continue;
            }

            var clinicCode = OrganizationCodes.Normalize(order.ClinicCode);
            if (!string.IsNullOrWhiteSpace(item.ClinicCode) &&
                !string.Equals(OrganizationCodes.Normalize(item.ClinicCode), clinicCode, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Order '{order.OrderCode}' belongs to clinic '{clinicCode}', but assignment file has clinic '{item.ClinicCode}'.");
            }

            var currentMemberId = order.MemberId.Trim();
            var currentIsActiveClinicMember = IsActiveClinicMember(membersByClinic, clinicCode, currentMemberId);

            if (string.IsNullOrWhiteSpace(item.TargetMemberId))
            {
                if (!currentIsActiveClinicMember)
                {
                    errors.Add(
                        $"Order '{order.OrderCode}' has current member '{currentMemberId}' who is not an active member of clinic '{clinicCode}'; set targetMemberId to an active clinic member.");
                }
                else
                {
                    noChangeCount++;
                }

                continue;
            }

            var expectedCurrentMemberId = item.CurrentMemberId?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(expectedCurrentMemberId) &&
                !MemberIdsEqual(currentMemberId, expectedCurrentMemberId) &&
                !MemberIdsEqual(currentMemberId, item.TargetMemberId) &&
                !forceCurrentMismatch)
            {
                errors.Add($"Order '{order.OrderCode}' current member is '{currentMemberId}', expected '{expectedCurrentMemberId}' from assignment file.");
            }

            var targetMemberId = item.TargetMemberId.Trim();
            if (MemberIdsEqual(currentMemberId, targetMemberId))
            {
                if (!currentIsActiveClinicMember)
                {
                    errors.Add(
                        $"Order '{order.OrderCode}' has current member '{currentMemberId}' who is not an active member of clinic '{clinicCode}'; set targetMemberId to an active clinic member.");
                }
                else
                {
                    noChangeCount++;
                }

                continue;
            }

            if (!membersByClinic.TryGetValue(clinicCode, out var activeMembers))
            {
                errors.Add($"Order '{order.OrderCode}' clinic '{clinicCode}' has no active members.");
                continue;
            }

            var targetMember = activeMembers.FirstOrDefault(m => MemberIdsEqual(m.MemberId, targetMemberId));
            if (targetMember == null)
            {
                errors.Add($"Order '{order.OrderCode}' target member '{targetMemberId}' is not an active member of clinic '{clinicCode}'.");
                continue;
            }

            updateCount++;
            updatesByClinic[clinicCode] = updatesByClinic.GetValueOrDefault(clinicCode) + 1;
            plannedUpdates.Add(new OrderOwnerPlannedUpdate(order.Id, targetMember.MemberId, targetMember.MemberLabel));
        }

        var summary = new OrderOwnerValidationSummary(assignments.Orders.Count, noChangeCount, updateCount, updatesByClinic);
        return new OrderOwnerValidationResult(summary, errors, plannedUpdates);
    }

    private static async Task<OrderOwnerReportDocument> ReadAssignmentsAsync(string assignmentsPath, CancellationToken ct)
    {
        if (!File.Exists(assignmentsPath))
            throw new FileNotFoundException("Assignments file was not found.", assignmentsPath);

        await using var stream = File.OpenRead(assignmentsPath);
        var document = await JsonSerializer.DeserializeAsync<OrderOwnerReportDocument>(stream, JsonOptions, ct);
        if (document?.Orders == null || document.Orders.Count == 0)
            throw new InvalidOperationException("Assignments file must contain at least one order entry.");
        return document;
    }

    private static async Task<AppDbContext> OpenContextAsync(string dbPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
            throw new InvalidOperationException("Database path is required.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var ctx = new AppDbContext(options);
        await ctx.Database.MigrateAsync(ct);
        return ctx;
    }

    private static async Task<Dictionary<string, IReadOnlyList<OrderOwnerMemberRef>>> LoadActiveClinicMembersByClinicAsync(
        AppDbContext ctx,
        CancellationToken ct)
    {
        var members = await ctx.SchedulingMembers.AsNoTracking()
            .Where(m => m.OrganizationType == OrganizationType.Clinic && m.IsActive)
            .OrderBy(m => m.OrganizationCode)
            .ThenBy(m => m.Label)
            .ThenBy(m => m.Id)
            .ToListAsync(ct);

        return members
            .GroupBy(m => OrganizationCodes.Normalize(m.OrganizationCode), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<OrderOwnerMemberRef>)g
                    .Select(m => new OrderOwnerMemberRef(m.Id, m.Label))
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsActiveClinicMember(
        IReadOnlyDictionary<string, IReadOnlyList<OrderOwnerMemberRef>> membersByClinic,
        string clinicCode,
        string memberId) =>
        membersByClinic.TryGetValue(clinicCode, out var activeMembers) &&
        activeMembers.Any(m => MemberIdsEqual(m.MemberId, memberId));

    private static bool MemberIdsEqual(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool OrderCodesEqual(string left, string right) =>
        string.Equals(NormalizeOrderCode(left), NormalizeOrderCode(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeOrderCode(string code) => code.Trim().ToUpperInvariant();
}

public sealed record OrderOwnerReportDocument(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<OrderOwnerAssignmentItem> Orders);

public sealed record OrderOwnerAssignmentItem(
    long OrderId,
    string OrderCode,
    string Status,
    string ClinicCode,
    string ClinicDisplayName,
    string CurrentMemberId,
    string CurrentMemberLabel,
    string CaseName,
    string RequestedDeliveryDate,
    string CreatedAtUtc,
    bool CurrentMemberMatchesActiveClinicMember,
    IReadOnlyList<OrderOwnerMemberRef> ActiveClinicMembers,
    string TargetMemberId);

public sealed record OrderOwnerMemberRef(string MemberId, string MemberLabel);

public sealed record OrderOwnerValidationSummary(
    int TotalOrders,
    int NoChangeCount,
    int UpdateCount,
    IReadOnlyDictionary<string, int> UpdatesByClinic);

public sealed record OrderOwnerValidationResult(
    OrderOwnerValidationSummary Summary,
    IReadOnlyList<string> Errors,
    IReadOnlyList<OrderOwnerPlannedUpdate> PlannedUpdates);

public sealed record OrderOwnerPlannedUpdate(long OrderId, string NewMemberId, string NewMemberLabel);

public sealed record OrderOwnerApplyResult(
    DateTimeOffset AppliedAtUtc,
    IReadOnlyList<OrderOwnerApplyItem> UpdatedOrders,
    OrderOwnerValidationSummary Summary,
    IReadOnlyList<string> Errors);

public sealed record OrderOwnerApplyItem(
    long OrderId,
    string OrderCode,
    string ClinicCode,
    string CaseName,
    string OldMemberId,
    string OldMemberLabel,
    string NewMemberId,
    string NewMemberLabel);
