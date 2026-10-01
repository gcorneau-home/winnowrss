using Dapper;
using Winnow.Core.Abstractions;

namespace Winnow.Data;

public sealed class SettingsRepository(WinnowDatabase db) : ISettingsRepository
{
    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<string?>("SELECT value FROM settings WHERE key = @key", new { key });
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync(
            "INSERT INTO settings (key, value) VALUES (@key, @value) ON CONFLICT (key) DO UPDATE SET value = excluded.value",
            new { key, value });
    }
}
