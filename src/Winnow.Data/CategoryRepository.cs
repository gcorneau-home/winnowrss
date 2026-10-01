using Dapper;
using Winnow.Core.Abstractions;
using Winnow.Core.Models;

namespace Winnow.Data;

public sealed class CategoryRepository(WinnowDatabase db) : ICategoryRepository
{
    public async Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        var rows = await c.QueryAsync<Category>(
            "SELECT id, name, sort_order FROM categories ORDER BY sort_order, name COLLATE NOCASE");
        return rows.AsList();
    }

    public async Task<Category?> GetAsync(long id, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<Category>(
            "SELECT id, name, sort_order FROM categories WHERE id = @id", new { id });
    }

    public async Task<long> AddAsync(string name, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<long>(
            """
            INSERT INTO categories (name, sort_order)
            VALUES (@name, (SELECT COALESCE(MAX(sort_order), 0) + 1 FROM categories))
            RETURNING id
            """, new { name });
    }

    public async Task RenameAsync(long id, string name, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync("UPDATE categories SET name = @name WHERE id = @id", new { id, name });
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync("DELETE FROM categories WHERE id = @id", new { id });
    }

    public async Task<int> CountArchivedArticlesAsync(long id, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM articles a JOIN feeds f ON f.id = a.feed_id
            WHERE f.category_id = @id AND a.state = @archived
            """, new { id, archived = ArticleState.Archived });
    }
}
