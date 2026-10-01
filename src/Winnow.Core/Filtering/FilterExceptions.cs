namespace Winnow.Core.Filtering;

/// <summary>The model server is unreachable or the model is missing: stop filtering and retry later.</summary>
public sealed class FilterUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The model answered, but not with a usable verdict for this article.</summary>
public sealed class FilterResponseException(string message, Exception? inner = null) : Exception(message, inner);
