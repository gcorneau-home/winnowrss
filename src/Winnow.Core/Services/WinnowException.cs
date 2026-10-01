namespace Winnow.Core.Services;

/// <summary>Expected failures the UI reports to the user; the UI translates the code, the message is for logs.</summary>
public enum WinnowError
{
    CategoryNameEmpty,
    FeedAlreadySubscribed,
    /// <summary>Args: the underlying error message.</summary>
    FeedUnreadable,
    FeedTimeout,
    FeedTitleEmpty,
    FeedUrlInvalid,
    /// <summary>Args: the title of the feed already using the address.</summary>
    FeedUrlInUse,
    CriterionEmpty,
    /// <summary>Args: the underlying error message.</summary>
    ThemeInvalid,
    ThemeNone,
    /// <summary>Args: the underlying error message.</summary>
    ThemeCatalogUnavailable,
}

public sealed class WinnowException(
    WinnowError error,
    string message,
    IReadOnlyList<object>? args = null,
    Exception? inner = null) : Exception(message, inner)
{
    public WinnowError Error { get; } = error;

    /// <summary>Values to insert in the translated message.</summary>
    public IReadOnlyList<object> Args { get; } = args ?? [];
}
