using Winnow.App.Localization;
using Winnow.Core.Filtering;
using Winnow.Core.Models;

namespace Winnow.App.ViewModels;

/// <summary>One-line description of a filter verdict, for tooltips, the article header and screen readers.</summary>
public static class FilterVerdictText
{
    /// <summary>Null for articles the filter never looked at.</summary>
    public static string? Describe(Localizer loc, FilterStatus status, string? criterion, string? reason, string? model)
    {
        if (status == FilterStatus.Pending)
            return loc["Verdict_Pending"];
        if (model == FilterRules.TrustedFeed)
            return loc.Format("Verdict_KeptTrustedFeed", criterion);
        if (model == FilterRules.Keyword)
            return loc.Format("Verdict_RejectedKeyword", criterion);

        var key = status switch
        {
            FilterStatus.Kept => "Verdict_Kept",
            FilterStatus.Rejected when string.IsNullOrWhiteSpace(criterion) => "Verdict_RejectedNoInterest",
            FilterStatus.Rejected => "Verdict_Rejected",
            FilterStatus.Error => "Verdict_Error",
            _ => null,
        };
        if (key is null)
            return null;

        var details = string.Join(" — ", new[] { criterion, reason }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return details.Length > 0 ? loc.Format(key, details) : loc[key + "_Bare"];
    }
}
