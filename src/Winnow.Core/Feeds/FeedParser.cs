using System.ServiceModel.Syndication;
using System.Xml;
using Winnow.Core.Text;

namespace Winnow.Core.Feeds;

/// <summary>Parses RSS 2.0 and Atom documents, including content:encoded and dc:creator.</summary>
public static class FeedParser
{
    private const string ContentNamespace = "http://purl.org/rss/1.0/modules/content/";
    private const string DublinCoreNamespace = "http://purl.org/dc/elements/1.1/";

    public static ParsedFeed Parse(Stream stream, Uri feedUri)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore };
        using var reader = XmlReader.Create(stream, settings);
        var feed = SyndicationFeed.Load(reader);

        var items = feed.Items.Select(item => ParseItem(item, feedUri)).ToList();
        var title = feed.Title?.Text?.Trim();
        return new ParsedFeed(
            string.IsNullOrEmpty(title) ? feedUri.Host : title,
            AlternateLink(feed.Links, feedUri),
            items);
    }

    private static FeedItem ParseItem(SyndicationItem item, Uri feedUri)
    {
        var content = ReadExtension(item, "encoded", ContentNamespace)
            ?? (item.Content as TextSyndicationContent)?.Text;

        var author = ReadExtension(item, "creator", DublinCoreNamespace)
            ?? item.Authors.Select(a => a.Name ?? a.Email).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));

        var summary = HtmlText.ToPlainText(item.Summary?.Text);

        return new FeedItem
        {
            Guid = item.Id,
            Link = ArticleKeys.NormalizeLink(AlternateLink(item.Links, feedUri)),
            Title = HtmlText.ToPlainText(item.Title?.Text) is { Length: > 0 } t ? t : "(untitled)",
            Author = author?.Trim(),
            PublishedAt = FirstDate(item.PublishDate, item.LastUpdatedTime),
            Summary = summary.Length > 0 ? summary : null,
            ContentHtml = string.IsNullOrWhiteSpace(content) ? null : content,
            Tags = item.Categories
                .Select(c => (c.Label ?? c.Name)?.Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()!,
        };
    }

    private static string? ReadExtension(SyndicationItem item, string name, string ns)
    {
        var extension = item.ElementExtensions.FirstOrDefault(e => e.OuterName == name && e.OuterNamespace == ns);
        if (extension is null)
            return null;

        using var reader = extension.GetReader();
        var value = reader.ReadElementContentAsString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? AlternateLink(IEnumerable<SyndicationLink> links, Uri baseUri)
    {
        var link = links.FirstOrDefault(l => l.RelationshipType is null or "alternate");
        if (link?.Uri is null)
            return null;
        return link.Uri.IsAbsoluteUri ? link.Uri.AbsoluteUri : new Uri(baseUri, link.Uri).AbsoluteUri;
    }

    private static DateTimeOffset? FirstDate(params DateTimeOffset[] dates) =>
        dates.Where(d => d != default).Select(d => (DateTimeOffset?)d.ToUniversalTime()).FirstOrDefault();
}
