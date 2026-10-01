namespace Winnow.Core.Feeds;

/// <summary>One entry of a parsed feed, before it becomes a stored article.</summary>
public sealed record FeedItem
{
    public string? Guid { get; init; }
    public string? Link { get; init; }
    public string Title { get; init; } = "";
    public string? Author { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>Plain-text description.</summary>
    public string? Summary { get; init; }

    public string? ContentHtml { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}

public sealed record ParsedFeed(string Title, string? SiteUrl, IReadOnlyList<FeedItem> Items);

/// <summary>Result of a fetch; <see cref="Feed"/> is null when the server answered 304 Not Modified.</summary>
public sealed record FeedFetchResult(ParsedFeed? Feed, string? ETag, string? LastModified)
{
    public bool IsNotModified => Feed is null;
}
