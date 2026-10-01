namespace Winnow.Core.Abstractions;

/// <summary>Downloads images for the archive.</summary>
public interface IResourceFetcher
{
    /// <summary>The image at <paramref name="url"/>, or null if it is missing, not an image or too large.</summary>
    Task<FetchedResource?> FetchImageAsync(string url, CancellationToken ct = default);
}

public sealed record FetchedResource(string ContentType, byte[] Data);
