namespace Orders;

public sealed record OrderDraft(
    string CaseName,
    DateOnly ImpressionDate,
    ProductCategory ProductCategory,
    WorkType WorkType,
    Material Material,
    ConstructionType ConstructionType,
    ToothRange TeethRange,
    DateOnly RequestedDeliveryDate,
    Shade Shade,
    string? Notes,
    IReadOnlyList<OrderWorkItem>? WorkItems = null)
{
    public IReadOnlyList<OrderWorkItem> ResolvedWorkItems =>
        OrderWorkItem.Normalize(WorkItems, ConstructionType, TeethRange);

    public OrderWorkItem PrimaryWorkItem => ResolvedWorkItems[0];
}
