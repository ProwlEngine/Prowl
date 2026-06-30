// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Rosetta;

/// <summary>
/// Provides translation key-value pairs for a given locale.
/// </summary>
public interface ILocalizationProvider
{
    /// <summary>
    /// Load all translations for the given locale.
    /// Returns a flat key → value dictionary.
    /// </summary>
    IReadOnlyDictionary<string, string> Load(string locale);

    /// <summary>
    /// Returns true if this provider can supply translations for the given locale.
    /// </summary>
    bool SupportsLocale(string locale);
}
