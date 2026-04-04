// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Rosetta;

/// <summary>
/// CLDR plural category resolution.
/// Implements Unicode CLDR plural rules for the major language families.
/// </summary>
internal static class PluralRules
{
    /// <summary>
    /// Returns the CLDR plural category for the given count and locale.
    /// Categories: "zero", "one", "two", "few", "many", "other"
    /// </summary>
    public static string GetCategory(string locale, int count)
    {
        // Normalize locale to language code (e.g. "en-US" → "en")
        var lang = locale.Split('-', '_')[0].ToLowerInvariant();

        return lang switch
        {
            // East Asian — no plural forms
            "ja" or "ko" or "zh" or "vi" or "th" or "id" or "ms" => "other",

            // Germanic, Romance, etc. — one/other
            "en" or "de" or "nl" or "sv" or "da" or "no" or "nb" or "nn"
            or "es" or "it" or "pt" or "el" or "fi" or "he" or "hu"
            or "tr" or "bg" or "ca" or "et" or "gl" or "hi"
                => count == 1 ? "one" : "other",

            // French — 0 and 1 are singular
            "fr" => count is 0 or 1 ? "one" : "other",

            // Arabic — zero/one/two/few/many/other
            "ar" => count switch
            {
                0 => "zero",
                1 => "one",
                2 => "two",
                _ when count % 100 >= 3 && count % 100 <= 10 => "few",
                _ when count % 100 >= 11 && count % 100 <= 99 => "many",
                _ => "other"
            },

            // Polish — one / few / many / other
            "pl" => count switch
            {
                1 => "one",
                _ when count % 10 >= 2 && count % 10 <= 4
                      && (count % 100 < 12 || count % 100 > 14) => "few",
                _ when count != 1
                      && (count % 10 is 0 or 1
                          || (count % 10 >= 5 && count % 10 <= 9)
                          || (count % 100 >= 12 && count % 100 <= 14)) => "many",
                _ => "other"
            },

            // Russian / Ukrainian — one / few / many / other
            "ru" or "uk" or "hr" or "sr" or "bs" => count switch
            {
                _ when count % 10 == 1 && count % 100 != 11 => "one",
                _ when count % 10 >= 2 && count % 10 <= 4
                      && (count % 100 < 12 || count % 100 > 14) => "few",
                _ when count % 10 == 0
                      || (count % 10 >= 5 && count % 10 <= 9)
                      || (count % 100 >= 11 && count % 100 <= 14) => "many",
                _ => "other"
            },

            // Czech / Slovak — one / few / other
            "cs" or "sk" => count switch
            {
                1 => "one",
                >= 2 and <= 4 => "few",
                _ => "other"
            },

            // Romanian — one / few / other
            "ro" => count switch
            {
                1 => "one",
                _ when count == 0 || (count % 100 >= 2 && count % 100 <= 19) => "few",
                _ => "other"
            },

            // Default — one/other (English-like)
            _ => count == 1 ? "one" : "other"
        };
    }
}
