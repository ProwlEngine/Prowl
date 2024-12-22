// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo;

public sealed partial class EchoObject
{
    /// <inheritdoc cref="Find"/>"
    public bool TryFind(string path, out EchoObject? tag)
    {
        tag = Find(path);
        return tag != null;
    }

    /// <summary>
    /// Find a tag by path. For example, if you have a compound tag with a tag called "stats" and that tag has a tag called "stamina",
    /// you can find the health tag by calling Find("stats/stamina").
    /// Lists can be indexed by their position in the list, for example Find("Players/0") will return the first player tag in a players list.
    /// </summary>
    /// <param name="path">The path to the tag</param>
    /// <returns>The tag if found, otherwise null</returns>
    /// <exception cref="InvalidOperationException">Thrown if this tag is not a compound tag</exception>
    public EchoObject? Find(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return this;

        EchoObject current = this;
        var segments = path.Split('/');

        foreach (var segment in segments)
        {
            if (current == null) return null;

            // Try parse as list index
            if (current.TagType == EchoType.List && int.TryParse(segment, out int index))
            {
                if (index < 0 || index >= current.List.Count)
                    return null;
                current = current.List[index];
            }
            // Handle as compound property
            else if (current.TagType == EchoType.Compound)
            {
                if (!current.TryGet(segment, out var next))
                    return null;
                current = next!;
            }
            else return null;
        }
        return current;
    }

    public T? GetValue<T>(string path, T? defaultValue = default)
    {
        var tag = Find(path);
        if (tag == null) return defaultValue;

        try
        {
            // Handle EchoObject requests directly
            if (typeof(T) == typeof(EchoObject))
                return (T)(object)tag;

            // Handle primitive value types
            var value = tag.Value;
            if (value != null)
            {
                // If the value is already the correct type, return it
                if (value is T directValue)
                    return directValue;

                // Try converting the value
                return (T)Convert.ChangeType(value, typeof(T));
            }

            // Handle collections
            return tag.TagType switch {
                EchoType.List => typeof(T).IsAssignableTo(typeof(IEnumerable<EchoObject>))
                    ? (T)(object)tag.List
                    : defaultValue,

                EchoType.Compound => typeof(T).IsAssignableTo(typeof(IDictionary<string, EchoObject>))
                    ? (T)(object)tag.Tags
                    : defaultValue,

                _ => defaultValue
            };
        }
        catch
        {
            return defaultValue;
        }
    }

    public EchoObject? GetEchoAt(string path) => GetValue<EchoObject>(path);
    public List<EchoObject>? GetListAt(string path) => GetValue<List<EchoObject>>(path);
    public Dictionary<string, EchoObject>? GetDictionaryAt(string path) => GetValue<Dictionary<string, EchoObject>>(path);

    public IEnumerable<EchoObject> Where(Func<EchoObject, bool> predicate)
    {
        if (TagType == EchoType.List)
            return List.Where(predicate);
        else if (TagType == EchoType.Compound)
            return Tags.Values.Where(predicate);
        return Enumerable.Empty<EchoObject>();
    }

    public IEnumerable<T> Select<T>(Func<EchoObject, T> selector)
    {
        if (TagType == EchoType.List)
            return List.Select(selector);
        else if (TagType == EchoType.Compound)
            return Tags.Values.Select(selector);
        return Enumerable.Empty<T>();
    }

    public IEnumerable<EchoObject> FindAll(Func<EchoObject, bool> predicate)
    {
        var results = new List<EchoObject>();
        FindAllRecursive(this, predicate, results);
        return results;
    }

    private void FindAllRecursive(EchoObject current, Func<EchoObject, bool> predicate, List<EchoObject> results)
    {
        if (predicate(current))
            results.Add(current);

        if (current.TagType == EchoType.List)
        {
            foreach (var item in current.List)
                FindAllRecursive(item, predicate, results);
        }
        else if (current.TagType == EchoType.Compound)
        {
            foreach (var item in current.Tags.Values)
                FindAllRecursive(item, predicate, results);
        }
    }

    public bool Exists(string path) => Find(path) != null;

    public IEnumerable<string> GetPathsTo(Func<EchoObject, bool> predicate)
    {
        var paths = new List<string>();
        GetPathsToRecursive(this, "", predicate, paths);
        return paths;
    }

    private void GetPathsToRecursive(EchoObject current, string currentPath, Func<EchoObject, bool> predicate, List<string> paths)
    {
        if (predicate(current))
            paths.Add(currentPath);

        if (current.TagType == EchoType.List)
        {
            for (int i = 0; i < current.List.Count; i++)
                GetPathsToRecursive(current.List[i], $"{currentPath}{(currentPath == "" ? "" : "/")}{i}", predicate, paths);
        }
        else if (current.TagType == EchoType.Compound)
        {
            foreach (var (key, value) in current.Tags)
                GetPathsToRecursive(value, $"{currentPath}{(currentPath == "" ? "" : "/")}{key}", predicate, paths);
        }
    }
}
