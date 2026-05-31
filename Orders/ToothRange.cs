namespace Orders;

public sealed record ToothRange(int Start, int End)
{
    public int Min => Math.Min(Start, End);
    public int Max => Math.Max(Start, End);
    public bool IsSingle => Start == End;

    public void Validate(ConstructionType constructionType)
    {
        if (!IsValidFdiTooth(Start)) throw new InvalidOperationException($"Invalid FDI tooth number: {Start}.");
        if (!IsValidFdiTooth(End)) throw new InvalidOperationException($"Invalid FDI tooth number: {End}.");
        if (constructionType == ConstructionType.Crown && !IsSingle)
            throw new InvalidOperationException("Crown orders must select exactly one tooth.");
        if (constructionType != ConstructionType.Crown && IsSingle)
            throw new InvalidOperationException("Non-crown orders must select a tooth range.");
        if (Quadrant(Start) != Quadrant(End))
            throw new InvalidOperationException("Walking skeleton supports ranges within one FDI quadrant only.");
    }

    public int[] DefaultAbutments(ConstructionType constructionType) =>
        constructionType == ConstructionType.Bridge ? [Start, End] : [];

    public static bool IsValidFdiTooth(int tooth)
    {
        var q = tooth / 10;
        var n = tooth % 10;
        return q is >= 1 and <= 4 && n is >= 1 and <= 8;
    }

    private static int Quadrant(int tooth) => tooth / 10;
}
