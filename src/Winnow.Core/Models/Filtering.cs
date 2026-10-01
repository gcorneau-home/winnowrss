namespace Winnow.Core.Models;

/// <summary>Where an article stands with the interest filter (independent of <see cref="ArticleState"/>).</summary>
public enum FilterStatus
{
    /// <summary>Stored while the filter was off, or before it existed.</summary>
    Unfiltered = 0,
    Pending = 1,
    Kept = 2,
    Rejected = 3,
    /// <summary>The model gave an unusable answer; <c>FilterReason</c> holds the error.</summary>
    Error = 4,
}

public enum CriterionKind
{
    Interest = 0,
    Exclusion = 1,
    /// <summary>A word that filters out any article whose title contains it, without asking the model.</summary>
    Keyword = 2,
}

/// <summary>An interest or exclusion, written in natural language by the user.</summary>
public sealed record FilterCriterion
{
    public long Id { get; init; }
    public CriterionKind Kind { get; init; }
    public string Text { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public int SortOrder { get; init; }
}

public sealed record FilterSettings(bool Enabled, string Endpoint, string Model)
{
    public const string DefaultEndpoint = "http://localhost:11434";
    public const string DefaultModel = "qwen3:8b";
}

/// <summary>A verdict to store on an article.</summary>
public sealed record FilterResult(
    FilterStatus Status,
    string? Criterion,
    string? Reason,
    int Version,
    string Model,
    DateTimeOffset At);
