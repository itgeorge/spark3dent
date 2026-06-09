namespace Orders;

public sealed record OrderRecord(
    long Id,
    string OrderCode,
    string ClinicCode,
    string ClinicDisplayName,
    string MemberId,
    string MemberLabel,
    string MemberPinHashFingerprint,
    string CaseName,
    DateOnly ImpressionDate,
    ProductCategory ProductCategory,
    Material Material,
    IReadOnlyList<OrderWorkItem> WorkItems,
    DateOnly RequestedDeliveryDate,
    OrderStatus Status,
    Shade Shade,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string CreatedIp,
    string CreatedUserAgent);
