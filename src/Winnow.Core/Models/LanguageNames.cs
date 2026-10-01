using System.Globalization;

namespace Winnow.Core.Models;

/// <summary>Names of languages as the model understands them in its instructions ("French", "Spanish"...).</summary>
public static class LanguageNames
{
    /// <summary>English name of a two-letter language code; without one, the language of the interface.</summary>
    public static string English(string? code)
    {
        try
        {
            var culture = string.IsNullOrWhiteSpace(code) ? CultureInfo.CurrentUICulture : CultureInfo.GetCultureInfo(code);
            return culture.IsNeutralCulture || culture.Parent == CultureInfo.InvariantCulture
                ? culture.EnglishName
                : culture.Parent.EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return "English";
        }
    }
}
