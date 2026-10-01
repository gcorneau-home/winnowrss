using System.Text;
using System.Text.Json.Nodes;
using Winnow.Core.Abstractions;
using Winnow.Core.Models;

namespace Winnow.Core.Filtering;

/// <summary>The two rules the model follows; the defaults are what the app uses, the bench compares variants.</summary>
public sealed record FilterPromptOptions(string ExclusionRule, string InterestRule)
{
    public static FilterPromptOptions Default { get; } = new(
        "is the article about one of the exclusion topics? Copy that exclusion's exact text, or null if none applies. Only name an exclusion the article is really about.",
        "which interest does the article relate to? Copy the exact text of the closest one, or null if it relates to none of them. When in doubt, name the closest one: missing a good article is worse than showing a useless one.");
}

/// <summary>The instructions and the JSON schema sent to the model. Kept apart from Ollama so it can be tested and tuned.</summary>
public static class FilterPrompt
{
    public static string System(FilterContext context, FilterPromptOptions? options = null)
    {
        options ??= FilterPromptOptions.Default;
        var interests = Enabled(context, CriterionKind.Interest);
        var exclusions = Enabled(context, CriterionKind.Exclusion);

        var prompt = new StringBuilder();
        prompt.AppendLine("You sort news articles for one reader.");
        prompt.AppendLine();
        prompt.AppendLine("The reader's interests:");
        AppendList(prompt, interests);
        prompt.AppendLine();
        prompt.AppendLine("Topics the reader does NOT want (exclusions):");
        AppendList(prompt, exclusions);
        prompt.AppendLine();
        prompt.AppendLine("Answer three questions about the article:");
        prompt.AppendLine($"- \"exclusion\": {options.ExclusionRule}");
        prompt.AppendLine($"- \"interest\": {options.InterestRule}");
        prompt.AppendLine($"- \"reason\": one short sentence in {context.ReasonLanguage} explaining your answers.");
        prompt.Append("Answer with the JSON object only.");
        return prompt.ToString();
    }

    public static string User(FilterInput input)
    {
        var message = new StringBuilder();
        message.AppendLine($"Title: {input.Title}");
        if (input.Description is not null)
            message.AppendLine($"Description: {input.Description}");
        if (input.Excerpt is not null)
            message.AppendLine($"Beginning of the article: {input.Excerpt}");
        return message.ToString().TrimEnd();
    }

    /// <summary>
    /// Structured output constraint: the model can only answer with this shape. The fields are generated in this order,
    /// so the reason is written after (and about) the two answers.
    /// </summary>
    public static JsonObject Schema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["exclusion"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            ["interest"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            ["reason"] = new JsonObject { ["type"] = "string" },
        },
        ["required"] = new JsonArray("exclusion", "interest", "reason"),
    };

    private static List<string> Enabled(FilterContext context, CriterionKind kind) =>
        context.Criteria.Where(c => c.Enabled && c.Kind == kind).OrderBy(c => c.SortOrder).Select(c => c.Text.Trim()).ToList();

    private static void AppendList(StringBuilder prompt, List<string> items)
    {
        if (items.Count == 0)
            prompt.AppendLine("(none)");
        for (var i = 0; i < items.Count; i++)
            prompt.AppendLine($"{i + 1}. {items[i]}");
    }
}
