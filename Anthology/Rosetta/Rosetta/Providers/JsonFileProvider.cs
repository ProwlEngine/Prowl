// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text.Json;

namespace Prowl.Rosetta;

/// <summary>
/// Loads translations from JSON files on disk.
/// Path supports a {locale} token, e.g. "Localization/{locale}.json".
/// </summary>
public class JsonFileProvider : ILocalizationProvider
{
    private readonly string _pathTemplate;

    public JsonFileProvider(string pathTemplate)
    {
        _pathTemplate = pathTemplate;
    }

    public bool SupportsLocale(string locale)
    {
        return File.Exists(ResolvePath(locale));
    }

    public IReadOnlyDictionary<string, string> Load(string locale)
    {
        var path = ResolvePath(locale);
        var json = File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>();
        FlattenJson(doc.RootElement, "", result);
        return result;
    }

    private string ResolvePath(string locale) => _pathTemplate.Replace("{locale}", locale);

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
