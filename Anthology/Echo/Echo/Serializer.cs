// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Echo.Logging;
using Prowl.Echo.Formatters;
using System.Collections.Concurrent;

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

    private static readonly ConcurrentDictionary<Type, ISerializationFormat> _formatCache = new();

    private static IReadOnlyList<ISerializationFormat> _formats;

    static Serializer()
    {
        // Register built-in formats in order of precedence
        var formatsList = new List<ISerializationFormat>
        {
            new PrimitiveFormat(),
            new NullableFormat(),
            new DateTimeFormat(),
            new GuidFormat(),
            new EnumFormat(),
            new HashSetFormat(),
            new ArrayFormat(),
            new ListFormat(),
            new QueueFormat(),
            new StackFormat(),
            new LinkedListFormat(),
            new DictionaryFormat(),
            new FixedStructureFormat(),
            new AnyObjectFormat() // Fallback format - must be last
        };
        _formats = formatsList.AsReadOnly();
    }

    /// <summary>
    /// Clears all reflection caches. Call this when you need to reload assemblies or refresh type information.
    /// </summary>
    public static void ClearCache()
    {
        _formatCache.Clear();
        ReflectionUtils.ClearCache();
    }

    public static void RegisterFormat(ISerializationFormat format)
    {
        // Clear the cache when registering new formats
        _formatCache.Clear();

        // Create a new list with the new format
        var newFormats = new List<ISerializationFormat> { format };
        newFormats.AddRange(_formats.Where(f => !(f is AnyObjectFormat)));
        newFormats.Add(_formats.Last()); // Add AnyObjectFormat back at the end
        _formats = newFormats.AsReadOnly();
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

        var valueType = value.GetType();
        var format = _formatCache.GetOrAdd(valueType, type =>
            _formats.FirstOrDefault(f => f.CanHandle(type))
            ?? throw new NotSupportedException($"No format handler found for type {type}"));

        return format.Serialize(targetType, value, context);
    }

    public static T? Deserialize<T>(EchoObject? value) => (T?)Deserialize(value, typeof(T));
    public static object? Deserialize(EchoObject? value, Type targetType) => Deserialize(value, targetType, new SerializationContext());
    public static T? Deserialize<T>(EchoObject? value, SerializationContext context) => (T?)Deserialize(value, typeof(T), context);
    public static object? Deserialize(EchoObject? value, Type targetType, SerializationContext context)
    {
        if (object.Equals(value, null) || value.TagType == EchoType.Null) return null;

        if (value.GetType() == targetType) return value;

        // Resolve actual type from $type if present
        Type actualType = targetType;
        if (value.TagType == EchoType.Compound && value.TryGet("$type", out var typeTag))
            actualType = ReflectionUtils.FindTypeByName(typeTag.StringValue) ?? targetType;

        var format = _formatCache.GetOrAdd(actualType, type =>
            _formats.FirstOrDefault(f => f.CanHandle(type))
            ?? throw new NotSupportedException($"No format handler found for type {type}"));

        return format.Deserialize(value, targetType, context);
    }
}
