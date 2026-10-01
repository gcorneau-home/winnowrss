using System.Text.RegularExpressions;
using Winnow.Core.Abstractions;
using Winnow.Core.Models;

namespace Winnow.Core.Filtering;

/// <summary>
/// What the model says about an article: the exclusion it is about, the interest it relates to, and why.
/// The keep/reject decision is made here from those answers, so the stated criterion always matches the decision.
/// </summary>
public sealed partial record FilterAnswer(string? Exclusion, string? Interest, string? Reason)
{
    /// <summary>
    /// Rejected if it is about one of the user's exclusions; otherwise kept if it relates to an interest.
    /// An "exclusion" the user never wrote (the model made it up) is ignored: a false rejection costs more than a false keep.
    /// </summary>
    public FilterDecision Decide(FilterContext context)
    {
        var exclusion = Canonical(Exclusion, context, CriterionKind.Exclusion);
        if (exclusion is not null)
            return new FilterDecision(false, exclusion, Reason);

        var interest = Clean(Interest);
        return interest is null
            ? new FilterDecision(false, null, Reason) // relates to none of the interests
            : new FilterDecision(true, Canonical(interest, context, CriterionKind.Interest) ?? interest, Reason);
    }

    /// <summary>The user's own wording of the criterion the model named (models trim, renumber or reword slightly).</summary>
    private static string? Canonical(string? answer, FilterContext context, CriterionKind kind)
    {
        if (Clean(answer) is not { } named)
            return null;

        var candidates = context.Criteria.Where(c => c.Enabled && c.Kind == kind).Select(c => c.Text.Trim()).ToList();
        return candidates.FirstOrDefault(c => string.Equals(c, named, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(c => named.Length >= 5 && (
                c.StartsWith(named, StringComparison.OrdinalIgnoreCase) || named.StartsWith(c, StringComparison.OrdinalIgnoreCase)));
    }

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
            return null;
        // Small models sometimes copy the list numbering or the quotes ("1. Bons plans…").
        return ListNumber().Replace(text.Trim(), "").Trim('"', '«', '»', ' ');
    }

    [GeneratedRegex(@"^\d+[.)]\s*")]
    private static partial Regex ListNumber();
}
