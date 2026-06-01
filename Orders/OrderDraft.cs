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
    string? Shade,
    string? Notes);
