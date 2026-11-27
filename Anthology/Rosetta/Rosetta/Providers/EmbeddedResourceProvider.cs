// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;
using System.Text.Json;

namespace Prowl.Rosetta;

/// <summary>
/// Loads translations from embedded JSON resources in an assembly.
/// Expects resources named like "*.{locale}.json" (e.g. "MyApp.Localization.en.json").
/// </summary>
public class EmbeddedResourceProvider : ILocalizationProvider
{
    private readonly Assembly _assembly;
    private readonly string? _resourcePrefix;

    public EmbeddedResourceProvider(Assembly assembly, string? resourcePrefix = null)
    {
        _assembly = assembly;
        _resourcePrefix = resourcePrefix;
    }

    public bool SupportsLocale(string locale)
    {
        return FindResource(locale) != null;
    }

    public IReadOnlyDictionary<string, string> Load(string locale)
    {
        var resourceName = FindResource(locale)
            ?? throw new FileNotFoundException($"No embedded resource found for locale '{locale}'.");

        using var stream = _assembly.GetManifestResourceStream(resourceName)!;
        var doc = JsonDocument.Parse(stream);
        var result = new Dictionary<string, string>();
        FlattenJson(doc.RootElement, "", result);
        return result;
    }

    private string? FindResource(string locale)
    {
        var names = _assembly.GetManifestResourceNames();
        var suffix = $".{locale}.json";

        foreach (var name in names)
        {
            if (_resourcePrefix != null && !name.StartsWith(_resourcePrefix))
                continue;
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return name;
        }
        return null;
    }

    private static void FlattenJson(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                    FlattenJson(prop.Value, key, result);
                }
                break;

            case JsonValueKind.String:
                result[prefix] = element.GetString()!;
                break;

            case JsonValueKind.Number:
                result[prefix] = element.GetRawText();
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                result[prefix] = element.GetBoolean().ToString();
                break;
        }
    }
}
