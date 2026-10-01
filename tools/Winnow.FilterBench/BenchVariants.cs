using Winnow.Core.Filtering;
using Winnow.Core.Models;

namespace Winnow.FilterBench;

/// <summary>A way of wording the two rules given to the model.</summary>
public sealed record PromptVariant(string Name, FilterPromptOptions Options)
{
    private const string BroadExclusion =
        "does the article deal with one of the exclusion topics, including any brand, product or device listed in it? Copy that exclusion's exact text, or null if none applies. An exclusion applies even when the article also relates to an interest.";

    private const string StrictInterest =
        "which interest is the article clearly about? Copy its exact text, or null if the article is not clearly about any of them.";

    public static IReadOnlyList<PromptVariant> All { get; } =
    [
        new("default", FilterPromptOptions.Default),
        new("broad-exclusions", FilterPromptOptions.Default with { ExclusionRule = BroadExclusion }),
        new("strict-interests", FilterPromptOptions.Default with { InterestRule = StrictInterest }),
        new("broad+strict", new FilterPromptOptions(BroadExclusion, StrictInterest)),
    ];
}

/// <summary>How much of the article the model reads.</summary>
public sealed record InputVariant(string Name, int ExcerptWords, bool IncludeDescription)
{
    public static IReadOnlyList<InputVariant> All { get; } =
    [
        new("title", 0, false),
        new("title+description", 0, true),
        new("excerpt", FilterInput.ExcerptWords, true), // what the app uses
        new("long-excerpt", 400, true),
    ];

    public FilterInput Build(Article article) => FilterInput.From(article, ExcerptWords, IncludeDescription);
}
