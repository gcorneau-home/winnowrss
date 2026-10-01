using Winnow.Core.Abstractions;
using Winnow.Core.Models;

namespace Winnow.Core.Services;

public sealed class CategoryService(ICategoryRepository categories)
{
    public Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct = default) => categories.GetAllAsync(ct);

    public Task<long> AddAsync(string name, CancellationToken ct = default) =>
        categories.AddAsync(ValidName(name), ct);

    public Task RenameAsync(long id, string name, CancellationToken ct = default) =>
        categories.RenameAsync(id, ValidName(name), ct);

    /// <summary>Number of archived articles that deleting the category would destroy.</summary>
    public Task<int> CountArchivedArticlesAsync(long id, CancellationToken ct = default) =>
        categories.CountArchivedArticlesAsync(id, ct);

    public Task DeleteAsync(long id, CancellationToken ct = default) => categories.DeleteAsync(id, ct);

    private static string ValidName(string name) =>
        string.IsNullOrWhiteSpace(name) ? throw new WinnowException(WinnowError.CategoryNameEmpty, "Category name cannot be empty.") : name.Trim();
}
