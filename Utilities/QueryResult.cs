namespace Utilities;

public record QueryResult<T>(IReadOnlyList<T> Items, string? NextStartAfter = null);