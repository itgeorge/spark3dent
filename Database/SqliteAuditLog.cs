using Database.Entities;
using Utilities;

namespace Database;

public sealed class SqliteAuditLog : IAuditLog
{
    private readonly Func<AppDbContext> _contextFactory;

    public SqliteAuditLog(Func<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task AppendAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        ctx.AuditEvents.Add(ToEntity(auditEvent));
        await ctx.SaveChangesAsync(ct);
    }

    private static AuditEventEntity ToEntity(AuditEvent auditEvent) => new()
    {
        ServiceName = auditEvent.ServiceName,
        Operation = auditEvent.Operation,
        EntityType = auditEvent.EntityType,
        EntityId = auditEvent.EntityId,
        EntityDisplay = auditEvent.EntityDisplay,
        ActorRole = auditEvent.ActorRole,
        ActorClinicCode = auditEvent.ActorClinicCode,
        ActorCredentialId = auditEvent.ActorCredentialId,
        ActorCredentialLabel = auditEvent.ActorCredentialLabel,
        ActorSessionId = auditEvent.ActorSessionId,
        OccurredAt = auditEvent.OccurredAt,
        OccurredAtUnixTimeMilliseconds = auditEvent.OccurredAt.ToUnixTimeMilliseconds(),
        Ip = auditEvent.Ip,
        UserAgent = auditEvent.UserAgent,
        MetadataJson = auditEvent.MetadataJson
    };
}
