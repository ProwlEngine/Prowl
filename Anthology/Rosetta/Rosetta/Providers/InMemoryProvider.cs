// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Rosetta;

/// <summary>
/// Provides translations from an in-memory dictionary. Useful for testing.
/// </summary>
public class InMemoryProvider : ILocalizationProvider
{
    private readonly Dictionary<string, Dictionary<string, string>> _data = new();

    public InMemoryProvider(string locale, Dictionary<string, string> translations)
    {
        _data[locale] = new Dictionary<string, string>(translations);
    }

    public InMemoryProvider AddLocale(string locale, Dictionary<string, string> translations)
    {
        _data[locale] = new Dictionary<string, string>(translations);
        return this;
    }

    public bool SupportsLocale(string locale) => _data.ContainsKey(locale);

    public IReadOnlyDictionary<string, string> Load(string locale) => _data[locale];
}
