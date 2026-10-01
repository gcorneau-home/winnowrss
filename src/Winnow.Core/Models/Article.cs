namespace Winnow.Core.Models;

public sealed record Article
{
    public long Id { get; init; }
    public long FeedId { get; init; }

    /// <summary>Deduplication key within a feed: the item guid, else the normalized link.</summary>
    public string DedupKey { get; init; } = "";

    public string? Link { get; init; }
    public string Title { get; init; } = "";
    public string? Author { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>Plain-text description from the feed.</summary>
    public string? Summary { get; init; }

    public string? ContentHtml { get; init; }

    /// <summary>Plain text of the content, used for full-text search.</summary>
    public string? ContentText { get; init; }

    public bool IsRead { get; init; }
    public bool IsPinned { get; init; }
    public Rating Rating { get; init; }
    public ArticleState State { get; init; }
    public DateTimeOffset? TrashedAt { get; init; }
    public DateTimeOffset? ArchivedAt { get; init; }

    public FilterStatus FilterStatus { get; init; }

    /// <summary>The interest or exclusion the verdict is based on, as written when the article was judged.</summary>
    public string? FilterCriterion { get; init; }

    /// <summary>The model's one-sentence explanation (or the error, for <see cref="FilterStatus.Error"/>).</summary>
    public string? FilterReason { get; init; }

    /// <summary>Version of the criteria that judged the article.</summary>
    public int? FilterVersion { get; init; }
    public string? FilterModel { get; init; }
    public DateTimeOffset? FilteredAt { get; init; }

    public DateTimeOffset FetchedAt { get; init; }
}

/// <summary>Lightweight projection used for lists and tree nodes.</summary>
public sealed record ArticleHeadline
{
    public long Id { get; init; }
    public long FeedId { get; init; }
    public string FeedTitle { get; init; } = "";
    public string Title { get; init; } = "";
    public DateTimeOffset? PublishedAt { get; init; }
    public bool IsRead { get; init; }
    public bool IsPinned { get; init; }
    public Rating Rating { get; init; }
    public ArticleState State { get; init; }
    public FilterStatus FilterStatus { get; init; }
    public string? FilterCriterion { get; init; }
    public string? FilterReason { get; init; }

    /// <summary>The model that judged it, or the rule (see <c>FilterRules</c>).</summary>
    public string? FilterModel { get; init; }
}

/// <summary>A full-text search result: the article and an excerpt around the match.</summary>
public sealed record SearchHit
{
    public long Id { get; init; }
    public long FeedId { get; init; }
    public string FeedTitle { get; init; } = "";
    public string Title { get; init; } = "";
    public DateTimeOffset? PublishedAt { get; init; }
    public ArticleState State { get; init; }
    public string Snippet { get; init; } = "";
}

/// <summary>An image stored with an archived article.</summary>
public sealed record ArchivedResource(string ContentType, byte[] Data);

/// <summary>Everything the reading pane shows for one article.</summary>
public sealed record ArticleDetails(Article Article, string FeedTitle, IReadOnlyList<string> Tags);
