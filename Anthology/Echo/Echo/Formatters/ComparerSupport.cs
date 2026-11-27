// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

namespace Prowl.Echo.Formatters;

// Preserves a dictionary/set comparer across a round trip. Without it a collection built with a custom
// comparer (e.g. StringComparer.OrdinalIgnoreCase) comes back with the default comparer.
internal static class ComparerSupport
{
    public static object? GetComparer(object collection)
        => collection.GetType().GetProperty("Comparer")?.GetValue(collection);

    // A tag describing the comparer, or null when it is the element type's default (nothing to store).
    public static EchoObject? Serialize(object? comparer, Type elementType, SerializationContext ctx)
    {
        if (comparer == null || IsDefault(comparer, elementType))
            return null;

        string? known = KnownName(comparer);
        if (known != null)
            return new EchoObject("string:" + known);

        // Best effort for custom comparers: reconstructs if the type is serializable.
        return Serializer.Serialize(typeof(object), comparer, ctx);
    }

    public static object? Deserialize(EchoObject? tag, SerializationContext ctx)
    {
        if (tag == null || tag.TagType == EchoType.Null)
            return null;

        if (tag.TagType == EchoType.String && tag.StringValue.StartsWith("string:", StringComparison.Ordinal))
            return FromName(tag.StringValue["string:".Length..]);

        try { return Serializer.Deserialize<object>(tag, ctx); }
        catch { return null; }
    }

    // Creates the collection through its comparer-taking constructor when a comparer is present.
    public static object CreateCollection(Type targetType, object? comparer)
    {
        if (comparer != null)
        {
            foreach (ConstructorInfo ctor in targetType.GetConstructors())
            {
                ParameterInfo[] ps = ctor.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType.IsInstanceOfType(comparer))
                    return ctor.Invoke(new[] { comparer });
            }
        }
        return Activator.CreateInstance(targetType)
            ?? throw new InvalidOperationException($"Failed to create instance of type: {targetType}");
    }

    private static bool IsDefault(object comparer, Type elementType)
    {
        object? eqDefault = typeof(EqualityComparer<>).MakeGenericType(elementType).GetProperty("Default")!.GetValue(null);
        if (ReferenceEquals(comparer, eqDefault)) return true;
        object? cmpDefault = typeof(Comparer<>).MakeGenericType(elementType).GetProperty("Default")!.GetValue(null);
        return ReferenceEquals(comparer, cmpDefault);
    }

    private static string? KnownName(object comparer)
    {
        if (ReferenceEquals(comparer, StringComparer.Ordinal)) return "Ordinal";
        if (ReferenceEquals(comparer, StringComparer.OrdinalIgnoreCase)) return "OrdinalIgnoreCase";
        if (ReferenceEquals(comparer, StringComparer.InvariantCulture)) return "InvariantCulture";
        if (ReferenceEquals(comparer, StringComparer.InvariantCultureIgnoreCase)) return "InvariantCultureIgnoreCase";
        if (ReferenceEquals(comparer, StringComparer.CurrentCulture)) return "CurrentCulture";
        if (ReferenceEquals(comparer, StringComparer.CurrentCultureIgnoreCase)) return "CurrentCultureIgnoreCase";
        return null;
    }

    private static object? FromName(string name) => name switch
    {
        "Ordinal" => StringComparer.Ordinal,
        "OrdinalIgnoreCase" => StringComparer.OrdinalIgnoreCase,
        "InvariantCulture" => StringComparer.InvariantCulture,
        "InvariantCultureIgnoreCase" => StringComparer.InvariantCultureIgnoreCase,
        "CurrentCulture" => StringComparer.CurrentCulture,
        "CurrentCultureIgnoreCase" => StringComparer.CurrentCultureIgnoreCase,
        _ => null
    };
}
