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
    private FileSystemWatcher? _watcher;

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
        WatchFile(path);
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

    /// <summary>
    /// Triggers when the translations for a locale have changed. Warning: this event is inherently asynchronous and
    /// may be triggered on the <see cref="FileSystemWatcher"/> background thread. Subscribers should not block or
    /// perform any significant work.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Watches a file for changes.</summary>
    private void WatchFile(string path)
    {
        string fileName = Path.GetFileName(path);
        if (_watcher is null)
        {
            string directoryPath = Path.GetDirectoryName(path) ?? throw new ArgumentException("Invalid file path.", nameof(path));
            _watcher = new FileSystemWatcher(directoryPath);
            _watcher.Filter = fileName;
            _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName;
            _watcher.Created += OnFileChanged;
            _watcher.Changed += OnFileChanged;
            _watcher.Renamed += OnFileRenamed;
            _watcher.EnableRaisingEvents = true;
        }
        else
        {
            if (!_watcher.Filters.Contains(fileName))
                _watcher.Filters.Add(fileName);
        }
    }

    /// <summary>Called when a locale JSON file is modified.</summary>
    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Called when a locale JSON file is renamed.</summary>
    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        // Note: VS and other editors modify files by following these steps:
        // 1. The changes are saved to a new temporary file.
        // 2. The original file is deleted.
        // 3. The temporary file is renamed to the original file name.
        // Since we are filtering by file name, we only see the rename.
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
