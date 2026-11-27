// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text.Json;

namespace Prowl.Clay.Formats.Gltf;

/// <summary>
/// Converts a glTF <c>extras</c> object into plain CLR values.
/// </summary>
/// <remarks>
/// <c>extras</c> is where an exporter puts anything the spec has no field for, which in practice is
/// the pipeline data a game needs: spawn tags, collision hints, gameplay flags an artist set on a
/// node. It is free-form JSON, so it lands as nested dictionaries and lists rather than a schema.
/// </remarks>
internal static class GltfExtras
{
    /// <summary>Reads an <c>extras</c> object, or returns null when there is nothing usable.</summary>
    public static Dictionary<string, object?>? Read(JsonElement? extras)
    {
        if (extras is not { } e || e.ValueKind != JsonValueKind.Object)
            return null;

        var result = new Dictionary<string, object?>();
        foreach (var property in e.EnumerateObject())
            result[property.Name] = Convert(property.Value);
        return result.Count == 0 ? null : result;
    }

    private static object? Convert(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => Read(value),
        JsonValueKind.Array => value.EnumerateArray().Select(Convert).ToList(),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        // Integers keep their exactness, which matters for the ids and indices that turn up here.
        // Cast to object per branch, or the ternary unifies long and double and boxes everything
        // as double.
        JsonValueKind.Number => value.TryGetInt64(out long i) ? (object)i : value.GetDouble(),
        _ => null,
    };
}
