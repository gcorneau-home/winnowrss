using Winnow.Core.Models;

namespace Winnow.Core.Abstractions;

public interface ICategoryRepository
{
    Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct = default);
    Task<Category?> GetAsync(long id, CancellationToken ct = default);

    /// <summary>Adds a category at the end of the list and returns its id.</summary>
    Task<long> AddAsync(string name, CancellationToken ct = default);

    Task RenameAsync(long id, string name, CancellationToken ct = default);

    /// <summary>Deletes the category with its feeds and all their articles.</summary>
    Task DeleteAsync(long id, CancellationToken ct = default);

    Task<int> CountArchivedArticlesAsync(long id, CancellationToken ct = default);
}
