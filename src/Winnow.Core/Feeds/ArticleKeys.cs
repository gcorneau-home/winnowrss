namespace Winnow.Core.Feeds;

public static class ArticleKeys
{
    /// <summary>Dedup key for an item: its guid, else its normalized link, else title and date.</summary>
    public static string Compute(FeedItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Guid))
            return item.Guid.Trim();

        return NormalizeLink(item.Link) ?? $"{item.Title.Trim()}|{item.PublishedAt:O}";
    }

    /// <summary>Removes tracking parameters (utm_*) from an absolute link; the fragment is kept.</summary>
    public static string? NormalizeLink(string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
            return null;
        if (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri))
            return link.Trim();

        var kept = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("utm_", StringComparison.OrdinalIgnoreCase));

        var builder = new UriBuilder(uri) { Query = string.Join('&', kept) };
        return builder.Uri.AbsoluteUri;
    }
}
