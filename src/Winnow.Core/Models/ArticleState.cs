namespace Winnow.Core.Models;

public enum ArticleState
{
    Active = 0,
    Archived = 1,
    Trashed = 2,
    /// <summary>Content removed by retention; the row stays so the article is not imported again.</summary>
    Purged = 3,
}

public enum Rating
{
    Down = -1,
    None = 0,
    Up = 1,
}
