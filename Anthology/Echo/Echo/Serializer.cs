// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Echo.Logging;
using Prowl.Echo.Formatters;

namespace Prowl.Echo;

public static class Serializer
{
    /// <summary>
    /// Since the serializer supports serializing EchoObjects
    /// Its possible the EchoObject may have more dependencies inside it
    /// Prowl handles these dependencies with something like:
    /// public static void GetAllAssetRefsInEcho(EchoObject echo, ref HashSet<Guid> refs)
    /// {
    ///     if (echo.TagType == EchoType.List)
    ///     {
    ///         foreach (var tag in (List<EchoObject>)echo.Value!)
    ///             GetAllAssetRefs(tag, ref refs);
    ///     }
    ///     else if (echo.TagType == EchoType.Compound)
    ///     {
    ///         var dict = (Dictionary<string, EchoObject>)echo.Value!;
    ///         if (TryGet("$type", out var typeName)) // See if we are an asset ref
    ///         {
    ///             if (typeName!.StringValue.Contains("Prowl.Runtime.AssetRef") && echo.TryGet("AssetID", out var assetId))
    ///             {
    ///                 if (Guid.TryParse(assetId!.StringValue, out var id) && id != Guid.Empty)
    ///                     refs.Add(id);
    ///             }
    ///         }
    ///         foreach (var (_, tag) in dict)
    ///             GetAllAssetRefs(tag, ref refs);
    ///     }
    /// }
    /// </summary>
    public static Action<EchoObject, HashSet<Guid>>? GetAllDependencyRefsInEcho { get; set; }

    public static IEchoLogger Logger { get; set; } = new NullEchoLogger();


    private static readonly List<ISerializationFormat> formats = new();

    static Serializer()
    {
        // Register built-in formats in order of precedence
        formats.Add(new PrimitiveFormat());
        formats.Add(new NullableFormat());
        formats.Add(new DateTimeFormat());
        formats.Add(new GuidFormat());
        formats.Add(new EnumFormat());
        formats.Add(new HashSetFormat());
        formats.Add(new ArrayFormat());
        formats.Add(new ListFormat());
        formats.Add(new QueueFormat());
        formats.Add(new DictionaryFormat());
        formats.Add(new AnyObjectFormat()); // Fallback format - must be last
    }

    public static void RegisterFormat(ISerializationFormat format)
    {
        formats.Insert(0, format); // Add to start for precedence - Also ensures ObjectFormat is last
    }

    public static EchoObject Serialize(object? value, TypeMode typeMode = TypeMode.Auto) => Serialize(value, new SerializationContext(typeMode));

    public static EchoObject Serialize(object? value, SerializationContext context) => Serialize(value?.GetType(), value, context);

    public static EchoObject Serialize(Type? targetType, object? value, SerializationContext context)
    {
        if (value == null) return new EchoObject(EchoType.Null, null);

        if (value is EchoObject property)
        {
            EchoObject clone = property.Clone();
            HashSet<Guid> deps = new();
            GetAllDependencyRefsInEcho?.Invoke(clone, deps);
            foreach (Guid dep in deps)
                context.AddDependency(dep);
            return clone;
        }

        ISerializationFormat? format = formats.FirstOrDefault(f => f.CanHandle(value.GetType()))
            ?? throw new NotSupportedException($"No format handler found for type {value.GetType()}");

        return format.Serialize(targetType, value, context);
    }

    public static T? Deserialize<T>(EchoObject value) => (T?)Deserialize(value, typeof(T));
    public static object? Deserialize(EchoObject value, Type targetType) => Deserialize(value, targetType, new SerializationContext());
    public static T? Deserialize<T>(EchoObject value, SerializationContext context) => (T?)Deserialize(value, typeof(T), context);
    public static object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        if (value == null || value.TagType == EchoType.Null) return null;

        if (value.GetType() == targetType) return value;

        ISerializationFormat format = formats.FirstOrDefault(f => f.CanHandle(targetType))
            ?? throw new NotSupportedException($"No format handler found for type {targetType}");

        return format.Deserialize(value, targetType, context);
    }
}
