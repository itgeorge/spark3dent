namespace Orders;

public sealed record OrderDraft(
    string CaseName,
    DateOnly ImpressionDate,
    ProductCategory ProductCategory,
    Material Material,
    IReadOnlyList<OrderWorkItem> WorkItems,
    DateOnly RequestedDeliveryDate,
    Shade Shade,
    string? Notes);
