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
    WorkType WorkType,
    Material Material,
    ConstructionType ConstructionType,
    int ToothStart,
    int ToothEnd,
    string AbutmentTeeth,
    DateOnly RequestedDeliveryDate,
    OrderStatus Status,
    Shade Shade,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string CreatedIp,
    string CreatedUserAgent,
    IReadOnlyList<OrderWorkItem>? WorkItems = null)
{
    public IReadOnlyList<OrderWorkItem> WorkItems { get; init; } =
        OrderWorkItem.Normalize(WorkItems, ConstructionType, new ToothRange(ToothStart, ToothEnd));

    public OrderWorkItem PrimaryWorkItem => WorkItems[0];
}
