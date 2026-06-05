namespace Orders;

public sealed record OrderWorkItem(ConstructionType ConstructionType, ToothRange TeethRange)
{
    public int ToothStart => TeethRange.Start;
    public int ToothEnd => TeethRange.End;
    public int[] Teeth => TeethRange.Teeth;
    public int[] DefaultAbutments => TeethRange.DefaultAbutments(ConstructionType);

    public void Validate() => TeethRange.Validate(ConstructionType);

    public static IReadOnlyList<OrderWorkItem> Normalize(IReadOnlyList<OrderWorkItem>? items, ConstructionType legacyConstructionType, ToothRange legacyTeethRange)
    {
        if (items == null)
            return [new OrderWorkItem(legacyConstructionType, legacyTeethRange)];
        if (items.Count == 0)
            return [];
        return items.Select(i => new OrderWorkItem(i.ConstructionType, i.TeethRange)).ToArray();
    }

    public static void ValidateAll(IReadOnlyList<OrderWorkItem> items)
    {
        if (items.Count == 0)
            throw new InvalidOperationException("At least one order work item is required.");

        var selected = new HashSet<int>();
        foreach (var item in items)
        {
            item.Validate();
            foreach (var tooth in item.Teeth)
            {
                if (!selected.Add(tooth))
                    throw new InvalidOperationException("Order work items must not overlap selected teeth.");
            }
        }
    }

    public static int[] AllTeeth(IReadOnlyList<OrderWorkItem> items) =>
        items.SelectMany(i => i.Teeth).Distinct().ToArray();

    public static string AbutmentsCsv(IReadOnlyList<OrderWorkItem> items) =>
        string.Join(",", items.SelectMany(i => i.DefaultAbutments).Distinct());
}
