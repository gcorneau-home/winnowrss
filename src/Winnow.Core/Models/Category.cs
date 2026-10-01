namespace Winnow.Core.Models;

public sealed record Category
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public int SortOrder { get; init; }
}
