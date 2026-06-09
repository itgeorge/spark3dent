namespace Orders;

public sealed record ToothRange
{
    private static readonly int[] UpperTeeth = [18, 17, 16, 15, 14, 13, 12, 11, 21, 22, 23, 24, 25, 26, 27, 28];
    private static readonly int[] LowerTeeth = [48, 47, 46, 45, 44, 43, 42, 41, 31, 32, 33, 34, 35, 36, 37, 38];

    public ToothRange(int start, int end)
    {
        (Start, End) = Normalize(start, end);
    }

    public int Start { get; }
    public int End { get; }
    public int Min => Math.Min(Start, End);
    public int Max => Math.Max(Start, End);
    public bool IsSingle => Start == End;
    public int[] Teeth => ResolveTeeth(Start, End) ?? [];

    public void Validate(ConstructionType constructionType)
    {
        if (!IsValidFdiTooth(Start)) throw new InvalidOperationException($"Invalid FDI tooth number: {Start}.");
        if (!IsValidFdiTooth(End)) throw new InvalidOperationException($"Invalid FDI tooth number: {End}.");

        if (constructionType == ConstructionType.Crown)
        {
            if (!IsSingle) throw new InvalidOperationException("Crown orders must select exactly one tooth.");
            return;
        }

        var teeth = Teeth;
        if (teeth.Length == 0)
            throw new InvalidOperationException("Tooth ranges must be contiguous within the same jaw.");
        if (teeth.Length < 2)
            throw new InvalidOperationException("Bridge/facet orders must span at least two teeth.");
    }

    public static bool IsValidFdiTooth(int tooth)
    {
        var q = tooth / 10;
        var n = tooth % 10;
        return q is >= 1 and <= 4 && n is >= 1 and <= 8;
    }

    private static (int Start, int End) Normalize(int start, int end)
    {
        var sequence = JawSequenceFor(start);
        if (sequence == null || !ReferenceEquals(sequence, JawSequenceFor(end))) return (start, end);

        var startIndex = Array.IndexOf(sequence, start);
        var endIndex = Array.IndexOf(sequence, end);
        return startIndex <= endIndex ? (start, end) : (end, start);
    }

    private static int[]? ResolveTeeth(int start, int end)
    {
        var sequence = JawSequenceFor(start);
        if (sequence == null || !ReferenceEquals(sequence, JawSequenceFor(end))) return null;

        var startIndex = Array.IndexOf(sequence, start);
        var endIndex = Array.IndexOf(sequence, end);
        if (startIndex < 0 || endIndex < 0) return null;

        var lowerIndex = Math.Min(startIndex, endIndex);
        var upperIndex = Math.Max(startIndex, endIndex);
        return sequence[lowerIndex..(upperIndex + 1)];
    }

    private static int[]? JawSequenceFor(int tooth)
    {
        if (UpperTeeth.Contains(tooth)) return UpperTeeth;
        if (LowerTeeth.Contains(tooth)) return LowerTeeth;
        return null;
    }
}
