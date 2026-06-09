namespace Utilities;

public sealed record AuditEvent(
    long Id,
    string ServiceName,
    string Operation,
    string EntityType,
    string EntityId,
    string? EntityDisplay,
    string ActorOrganizationType,
    string? ActorOrganizationCode,
    string? ActorMemberId,
    string? ActorMemberLabel,
    string? ActorSessionId,
    DateTimeOffset OccurredAt,
    string? Ip,
    string? UserAgent,
    string? MetadataJson);

public interface IAuditLog
{
    Task AppendAsync(AuditEvent auditEvent, CancellationToken ct = default);
}

public sealed class NoOpAuditLog : IAuditLog
{
    public static readonly NoOpAuditLog Instance = new();

    private NoOpAuditLog()
    {
    }

    public Task AppendAsync(AuditEvent auditEvent, CancellationToken ct = default) => Task.CompletedTask;
}
