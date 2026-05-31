namespace Orders;

public sealed record WorkRule(
    ProductCategory ProductCategory,
    WorkType WorkType,
    Material Material,
    ConstructionType ConstructionType,
    int MinBusinessDays);
