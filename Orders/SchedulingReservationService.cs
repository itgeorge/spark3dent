using System.Text.Json;
using Utilities;

namespace Orders;

public sealed class SchedulingReservationService
{
    private readonly ISchedulingIdentityRepository _identities;
    private readonly IReservationRepository _reservations;
    private readonly DateAvailabilityService _availability;
    private readonly DeadlineRecommendationService _deadlineRecommendations;
    private readonly ISchedulingWriteTransaction _writeTransaction;
    private readonly IClock _clock;
    private readonly IAuditLog _auditLog;
    private const int DefaultListLimit = 100;

    public SchedulingReservationService(
        ISchedulingIdentityRepository identities,
        IReservationRepository reservations,
        DateAvailabilityService availability,
        DeadlineRecommendationService deadlineRecommendations,
        ISchedulingWriteTransaction writeTransaction,
        IClock clock,
        IAuditLog? auditLog = null)
    {
        _identities = identities;
        _reservations = reservations;
        _availability = availability;
        _deadlineRecommendations = deadlineRecommendations;
        _writeTransaction = writeTransaction;
        _clock = clock;
        _auditLog = auditLog ?? NoOpAuditLog.Instance;
    }

    public async Task<DeadlineDateStatusesResult> GetDateStatusesResultAsync(ReservationDraft draft, DateOnly start, DateOnly end, long? excludedReservationId = null, CancellationToken ct = default)
    {
        ValidateReservationWorkItems(draft);
        await ValidateImpressionDateAsync(draft.ImpressionDate, ct);
        return await _deadlineRecommendations.GetCapacityAwareDateStatusesAsync(
            ToSchedulingInput(draft, excludedReservationId),
            start,
            end,
            orderRepositoryOverride: null,
            reservationRepositoryOverride: null,
            ct);
    }

    public async Task<ReservationRecord> CreateReservationAsync(AuthenticatedActor actor, ReservationDraft draft, string ip, string userAgent, string? targetClinicCode, DeadlineOverrideRequest? deadlineOverride, CancellationToken ct = default)
    {
        ValidateDraft(draft);
        await ValidateImpressionDateAsync(draft.ImpressionDate, ct);
        var targetClinic = await ResolveTargetClinicAsync(actor, targetClinicCode, ct);
        var now = _clock.UtcNow;
        var created = await _writeTransaction.ExecuteAsync(async (txOrders, txReservations) =>
        {
            await ValidateImpressionDateAsync(draft.ImpressionDate, ct);
            var decision = await DecideDeadlineCommitAsync(actor, draft, excludedReservationId: null, txOrders, txReservations, deadlineOverride, ct);
            var reservation = BuildReservation(actor, targetClinic, draft, ip, userAgent, decision.ValidationWithAudit.Validation.OrderCapacityUnits, now);
            return await txReservations.CreateReservationAsync(reservation, ct);
        }, ct);
        await AppendReservationAuditAsync(actor, "ReservationCreated", created, ip, userAgent, new
        {
            targetClinicCode = created.ClinicCode,
            targetClinicDisplayName = created.ClinicDisplayName,
            created.CaseName,
            created.ImpressionDate,
            created.RequestedDeliveryDate,
            status = created.Status.ToString(),
            totalToothCount = OrderWorkItem.AllTeeth(created.WorkItems).Length
        }, ct);
        return created;
    }

    public async Task<ReservationRecord> UpdateReservationAsync(AuthenticatedActor actor, long id, ReservationDraft draft, string? ip, string? userAgent, DeadlineOverrideRequest? deadlineOverride, CancellationToken ct = default)
    {
        ValidateDraft(draft);
        await ValidateImpressionDateAsync(draft.ImpressionDate, ct);
        var saved = await _writeTransaction.ExecuteAsync(async (txOrders, txReservations) =>
        {
            var existing = await GetAuthorizedReservationAsync(actor, id, txReservations, ct);
            EnsureActive(existing);
            await ValidateImpressionDateAsync(draft.ImpressionDate, ct);
            var decision = await DecideDeadlineCommitAsync(actor, draft, existing.Id, txOrders, txReservations, deadlineOverride, ct);
            var updated = existing with
            {
                CaseName = draft.CaseName.Trim(),
                ImpressionDate = draft.ImpressionDate,
                ProductCategory = draft.ProductCategory,
                Material = draft.Material,
                WorkItems = draft.WorkItems,
                RequestedDeliveryDate = draft.RequestedDeliveryDate,
                Shade = draft.Shade,
                Notes = string.IsNullOrWhiteSpace(draft.Notes) ? null : draft.Notes.Trim(),
                ColorNote = string.IsNullOrWhiteSpace(draft.ColorNote) ? null : draft.ColorNote.Trim(),
                CalculatedCapacityUnits = decision.ValidationWithAudit.Validation.OrderCapacityUnits,
                UpdatedAt = _clock.UtcNow
            };
            return await txReservations.UpdateReservationAsync(updated, ct);
        }, ct);
        await AppendReservationAuditAsync(actor, "ReservationUpdated", saved, ip, userAgent, new { saved.Id, saved.ImpressionDate, saved.RequestedDeliveryDate, saved.CaseName }, ct);
        return saved;
    }

    public async Task<ReservationRecord> CancelReservationAsync(AuthenticatedActor actor, long id, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var existing = await GetAuthorizedReservationAsync(actor, id, ct);
        EnsureActive(existing);
        var cancelled = await _reservations.UpdateReservationAsync(existing with { Status = ReservationStatus.Cancelled, UpdatedAt = _clock.UtcNow }, ct);
        await AppendReservationAuditAsync(actor, "ReservationCancelled", cancelled, ip, userAgent, new { cancelled.Id, previousStatus = existing.Status.ToString(), newStatus = cancelled.Status.ToString() }, ct);
        return cancelled;
    }

    public async Task<ReservationRecord> GetReservationForActorAsync(AuthenticatedActor actor, long id, CancellationToken ct = default) =>
        await GetAuthorizedReservationAsync(actor, id, ct);

    public Task<IReadOnlyList<ReservationRecord>> ListActiveReservationsForActorAsync(AuthenticatedActor actor, int limit = DefaultListLimit, CancellationToken ct = default) =>
        _reservations.ListActiveReservationsForActorAsync(actor.IsLab ? null : actor.OrganizationCode, Math.Clamp(limit, 1, 500), _clock.UtcNow, ct);

    public Task<IReadOnlyList<ReservationRecord>> ListCalendarReservationsAsync(AuthenticatedActor actor, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        if (start > end)
            throw new InvalidOperationException("Calendar start date must be before or equal to end date.");
        return _reservations.ListActiveReservationsForCalendarAsync(actor.IsLab ? null : actor.OrganizationCode, start, end, _clock.UtcNow, ct);
    }

    private async Task ValidateImpressionDateAsync(DateOnly impressionDate, CancellationToken ct)
    {
        var localToday = DateOnly.FromDateTime(LabTimeZone.ToLabLocal(_clock.UtcNow).DateTime.Date);
        if (impressionDate <= localToday)
            throw new InvalidOperationException("Reservation impression date must be a future date.");
        if (await _availability.IsClosedAsync(impressionDate, ct))
            throw new InvalidOperationException($"Reservation impression date {impressionDate:yyyy-MM-dd} is not available: non-working day.");
    }

    private async Task<DeadlineCommitDecision> DecideDeadlineCommitAsync(
        AuthenticatedActor actor,
        ReservationDraft draft,
        long? excludedReservationId,
        IOrderRepository orderRepositoryOverride,
        IReservationRepository reservationRepositoryOverride,
        DeadlineOverrideRequest? deadlineOverride,
        CancellationToken ct)
    {
        var result = await _deadlineRecommendations.ValidateRequestedDateWithAuditAsync(
            ToSchedulingInput(draft, excludedReservationId),
            draft.RequestedDeliveryDate,
            orderRepositoryOverride,
            reservationRepositoryOverride,
            ct);
        var validation = result.Validation;
        if (validation.Status.IsSelectable)
            return new DeadlineCommitDecision(result, false, [], null);

        var rulesBypassed = validation.FailedRules;
        var message = $"Delivery date {draft.RequestedDeliveryDate:yyyy-MM-dd} is not available: {validation.Status.Reason}. Reservation deadline overrides are not available yet.";
        throw new DeadlineOverrideRequiredException(message, overrideAllowed: false, rulesBypassed, validation.RecommendedDate);
    }

    private static OrderSchedulingInput ToSchedulingInput(ReservationDraft draft, long? excludedReservationId = null) =>
        new(draft.Material, draft.WorkItems, ReservationActiveRules.ToAfterCutoffImpressionTimestampUtc(draft.ImpressionDate), null, excludedReservationId);

    private static void ValidateDraft(ReservationDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.CaseName))
            throw new InvalidOperationException("Case name is required.");
        ValidateReservationWorkItems(draft);
    }

    private static void ValidateReservationWorkItems(ReservationDraft draft)
    {
        if (draft.WorkItems == null)
            throw new InvalidOperationException("At least one order work item is required.");
        OrderWorkItem.ValidateAll(draft.WorkItems);
    }

    private async Task<SchedulingClinic> ResolveTargetClinicAsync(AuthenticatedActor actor, string? targetClinicCode, CancellationToken ct)
    {
        if (!actor.IsLab)
        {
            if (!string.IsNullOrWhiteSpace(targetClinicCode) && !string.Equals(targetClinicCode, actor.OrganizationCode, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Clinic users cannot create reservations for another clinic.");
            return await RequireActiveClinicAsync(actor.OrganizationCode, ct);
        }

        if (string.IsNullOrWhiteSpace(targetClinicCode))
            throw new InvalidOperationException("Target clinic is required for lab reservation creation.");
        return await RequireActiveClinicAsync(targetClinicCode.Trim(), ct);
    }

    private async Task<SchedulingClinic> RequireActiveClinicAsync(string clinicCode, CancellationToken ct)
    {
        var clinic = await _identities.GetClinicAsync(clinicCode, includeInactive: false, ct);
        return clinic ?? throw new InvalidOperationException("Clinic not found or inactive.");
    }

    private Task<ReservationRecord> GetAuthorizedReservationAsync(AuthenticatedActor actor, long id, CancellationToken ct) =>
        GetAuthorizedReservationAsync(actor, id, _reservations, ct);

    private async Task<ReservationRecord> GetAuthorizedReservationAsync(AuthenticatedActor actor, long id, IReservationRepository reservations, CancellationToken ct)
    {
        var reservation = await reservations.GetReservationByIdAsync(id, ct);
        if (reservation == null || !CanActorSeeReservation(actor, reservation))
            throw new KeyNotFoundException("Reservation not found.");
        return reservation;
    }

    private bool IsActiveNow(ReservationRecord reservation) => ReservationActiveRules.IsActiveForScheduling(reservation, _clock.UtcNow);

    private void EnsureActive(ReservationRecord reservation)
    {
        if (!IsActiveNow(reservation))
            throw new InvalidOperationException("Reservation is no longer active.");
    }

    private static bool CanActorSeeReservation(AuthenticatedActor actor, ReservationRecord reservation) =>
        actor.IsLab || string.Equals(reservation.ClinicCode, actor.OrganizationCode, StringComparison.OrdinalIgnoreCase);

    private ReservationRecord BuildReservation(AuthenticatedActor actor, SchedulingClinic targetClinic, ReservationDraft draft, string ip, string userAgent, decimal calculatedCapacityUnits, DateTimeOffset now) =>
        new(
            0,
            targetClinic.Code,
            targetClinic.DisplayName,
            actor.MemberId,
            actor.MemberLabel,
            actor.MemberPinHashFingerprint,
            draft.CaseName.Trim(),
            draft.ImpressionDate,
            draft.ProductCategory,
            draft.Material,
            draft.WorkItems,
            draft.RequestedDeliveryDate,
            ReservationStatus.Active,
            draft.Shade,
            string.IsNullOrWhiteSpace(draft.Notes) ? null : draft.Notes.Trim(),
            now,
            now,
            ip,
            userAgent,
            string.IsNullOrWhiteSpace(draft.ColorNote) ? null : draft.ColorNote.Trim(),
            calculatedCapacityUnits);

    private Task AppendReservationAuditAsync(AuthenticatedActor actor, string operation, ReservationRecord reservation, string? ip, string? userAgent, object metadata, CancellationToken ct)
    {
        var auditEvent = new AuditEvent(
            0,
            "Scheduling",
            operation,
            "SchedulingReservation",
            reservation.Id.ToString(),
            reservation.CaseName,
            actor.OrganizationType.ToString(),
            actor.OrganizationCode,
            actor.MemberId,
            actor.MemberLabel,
            actor.SessionId,
            _clock.UtcNow,
            string.IsNullOrWhiteSpace(ip) ? null : ip,
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            JsonSerializer.Serialize(metadata));
        return _auditLog.AppendAsync(auditEvent, ct);
    }

    private sealed record DeadlineCommitDecision(
        DeadlineValidationWithAuditResult ValidationWithAudit,
        bool IsOverride,
        IReadOnlyList<DeadlineValidationRule> RulesBypassed,
        string? OverrideReason);
}
