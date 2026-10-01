using Winnow.Core.Abstractions;
using Winnow.Core.Models;

namespace Winnow.Core.Services;

/// <summary>The user's interests and exclusions. Every change starts a new criteria version.</summary>
public sealed class FilterCriteriaService(IFilterCriteriaRepository criteria, SettingsService settings)
{
    public Task<IReadOnlyList<FilterCriterion>> GetAllAsync(CancellationToken ct = default) => criteria.GetAllAsync(ct);

    public async Task<long> AddAsync(CriterionKind kind, string text, CancellationToken ct = default)
    {
        var id = await criteria.AddAsync(kind, ValidText(text), ct);
        await settings.BumpFilterVersionAsync(ct);
        return id;
    }

    public async Task UpdateAsync(long id, string text, bool enabled, CancellationToken ct = default)
    {
        await criteria.UpdateAsync(id, ValidText(text), enabled, ct);
        await settings.BumpFilterVersionAsync(ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        await criteria.DeleteAsync(id, ct);
        await settings.BumpFilterVersionAsync(ct);
    }

    private static string ValidText(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? throw new WinnowException(WinnowError.CriterionEmpty, "A criterion cannot be empty.")
            : text.Trim();
}
