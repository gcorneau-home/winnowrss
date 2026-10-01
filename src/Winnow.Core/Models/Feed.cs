namespace Winnow.Core.Models;

public sealed record Feed
{
    public long Id { get; init; }
    public long CategoryId { get; init; }
    public string Title { get; init; } = "";
    public string Url { get; init; } = "";
    public string? SiteUrl { get; init; }
    public string? ETag { get; init; }
    public string? LastModified { get; init; }
    public DateTimeOffset? LastFetchedAt { get; init; }
    public string? LastError { get; init; }

    /// <summary>Trusted feed: its articles are never filtered.</summary>
    public bool SkipFilter { get; init; }
}

/// <summary>What a fetch attempt changes on a feed row.</summary>
public sealed record FeedFetchState(
    long FeedId,
    string? SiteUrl,
    string? ETag,
    string? LastModified,
    DateTimeOffset FetchedAt,
    string? Error);
