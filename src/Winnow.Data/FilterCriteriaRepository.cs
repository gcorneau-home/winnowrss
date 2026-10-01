using Dapper;
using Winnow.Core.Abstractions;
using Winnow.Core.Models;

namespace Winnow.Data;

public sealed class FilterCriteriaRepository(WinnowDatabase db) : IFilterCriteriaRepository
{
    public async Task<IReadOnlyList<FilterCriterion>> GetAllAsync(CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        var rows = await c.QueryAsync<FilterCriterion>(
            "SELECT id, kind, text, enabled, sort_order FROM filter_criteria ORDER BY kind, sort_order, id");
        return rows.AsList();
    }

    public async Task<long> AddAsync(CriterionKind kind, string text, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<long>(
            """
            INSERT INTO filter_criteria (kind, text, sort_order)
            VALUES (@kind, @text, (SELECT COALESCE(MAX(sort_order), 0) + 1 FROM filter_criteria))
            RETURNING id
            """, new { kind, text });
    }

    public async Task UpdateAsync(long id, string text, bool enabled, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync("UPDATE filter_criteria SET text = @text, enabled = @enabled WHERE id = @id", new { id, text, enabled });
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync("DELETE FROM filter_criteria WHERE id = @id", new { id });
    }
}
