namespace Prowl.Clay.Formats.Fbx;

/// <summary>
/// Parsed FBX <c>Properties70</c> / <c>Properties60</c> block. Each <c>P</c> entry maps a property
/// name to a typed value (and an FBX type tag we mostly ignore). Used for both global settings,
/// object-level properties, and the per-object-type property templates declared under
/// <c>Definitions/</c>.
/// </summary>
internal sealed class FbxPropertyTable
{
    private readonly Dictionary<string, FbxProperty[]> _byName = new(StringComparer.Ordinal);

    /// <summary>Number of properties in the table.</summary>
    public int Count => _byName.Count;

    /// <summary>Returns the property values for the named entry (one entry can carry several values).</summary>
    public FbxProperty[]? GetRaw(string name) =>
        _byName.TryGetValue(name, out var v) ? v : null;

    public bool TryGetDouble(string name, out double value)
    {
        if (_byName.TryGetValue(name, out var arr) && arr.Length > 0)
        {
            value = arr[0].AsDouble();
            return true;
        }
        value = 0d;
        return false;
    }

    public bool TryGetInt(string name, out int value)
    {
        if (_byName.TryGetValue(name, out var arr) && arr.Length > 0)
        {
            value = arr[0].AsInt();
            return true;
        }
        value = 0;
        return false;
    }

    public bool TryGetString(string name, out string value)
    {
        if (_byName.TryGetValue(name, out var arr) && arr.Length > 0)
        {
            value = arr[0].AsString();
            return true;
        }
        value = string.Empty;
        return false;
    }

    /// <summary>Reads x/y/z triplet starting at <paramref name="firstIndex"/>.</summary>
    public bool TryGetVec3(string name, out double x, out double y, out double z, int firstIndex = 0)
    {
        if (_byName.TryGetValue(name, out var arr) && arr.Length >= firstIndex + 3)
        {
            x = arr[firstIndex + 0].AsDouble();
            y = arr[firstIndex + 1].AsDouble();
            z = arr[firstIndex + 2].AsDouble();
            return true;
        }
        x = y = z = 0d;
        return false;
    }

    public double GetDoubleOr(string name, double fallback) =>
        TryGetDouble(name, out double v) ? v : fallback;

    public int GetIntOr(string name, int fallback) =>
        TryGetInt(name, out int v) ? v : fallback;

    public string GetStringOr(string name, string fallback) =>
        TryGetString(name, out string v) ? v : fallback;

    /// <summary>
    /// Builds a property table from a <c>Properties70</c> / <c>Properties60</c> node.
    /// </summary>
    /// <remarks>
    /// FBX 7.x: each child is <c>P</c> with properties layout
    ///   <c>name (S), type (S), subtype (S), flags (S), value (varies, may span multiple props)</c>.
    /// FBX 6.x: each child is <c>Property</c> with a similar but slightly different layout.
    /// We only need the name -> values mapping, so we strip the type tags.
    /// </remarks>
    public static FbxPropertyTable From(FbxNode propsNode)
    {
        var table = new FbxPropertyTable();
        foreach (var child in propsNode.Children)
        {
            if (child.Properties.Count < 4) continue;
            string name = child.Properties[0].AsString();
            // The values for a property start at index 4 (after name/type/subtype/flags).
            int valueStart = 4;
            int valueCount = child.Properties.Count - valueStart;
            if (valueCount <= 0) continue;
            var values = new FbxProperty[valueCount];
            for (int i = 0; i < valueCount; i++)
                values[i] = child.Properties[valueStart + i];
            table._byName[name] = values;
        }
        return table;
    }
}
