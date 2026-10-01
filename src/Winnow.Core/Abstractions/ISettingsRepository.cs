namespace Winnow.Core.Abstractions;

/// <summary>Key/value store for user preferences.</summary>
public interface ISettingsRepository
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
}
