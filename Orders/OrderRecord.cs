namespace Orders;

public sealed record OrderRecord(
    long Id,
    string OrderCode,
    string ClinicCode,
    string ClinicDisplayName,
    string CredentialId,
    string CredentialLabel,
    string CredentialPinHashFingerprint,
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
