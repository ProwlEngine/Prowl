// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Echo.Logging;
using Prowl.Echo.Formatters;
using System.Collections.Concurrent;

namespace Prowl.Echo;

// Core type envelope that wraps all serialized data
public class TypeEnvelope
{
    public string? TypeInfo { get; set; }
    public EchoObject Data { get; set; }
    public bool IsTypePreserved => TypeInfo != null;
}

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
            new DateTimeOffsetFormat(),
            new TimeSpanFormat(),
            new GuidFormat(),
            new EnumFormat(),
            new AnonymousTypeFormat(),
            new HashSetFormat(),
            new ArrayFormat(),
            new ListFormat(),
            new QueueFormat(),
            new StackFormat(),
            new LinkedListFormat(),
            new CollectionFormat(),
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
        TypeNameRegistry.ClearCache();
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

    #region Public API

    public static EchoObject Serialize(object? value, TypeMode typeMode = TypeMode.Auto)
        => Serialize(value, new SerializationContext(typeMode));

    public static EchoObject Serialize(Type? targetType, object? value, TypeMode typeMode = TypeMode.Auto)
        => Serialize(targetType, value, new SerializationContext(typeMode));

    public static EchoObject Serialize(object? value, SerializationContext context)
        => Serialize(value?.GetType(), value, context);

    public static EchoObject Serialize(Type? targetType, object? value, SerializationContext context)
    {
        if (value == null) return new EchoObject(EchoType.Null, null);

        if (value is EchoObject echoObject)
        {
            EchoObject clone = echoObject.Clone();
            HashSet<Guid> deps = new();
            GetAllDependencyRefsInEcho?.Invoke(clone, deps);
            foreach (Guid dep in deps)
                context.AddDependency(dep);
            return clone;
        }

        var actualType = value.GetType();

        // STEP 1: Determine if we need type preservation (centralized logic)
        bool needsTypeInfo = ShouldPreserveType(targetType, actualType, context);

        // STEP 2: Serialize the actual data (formatters don't worry about types)
        var format = GetFormatForType(actualType);
        var serializedData = format.Serialize(actualType, value, context);

        // STEP 3: Wrap with type envelope if needed (centralized)
        return WrapWithTypeEnvelope(serializedData, needsTypeInfo ? actualType : null, context);
    }

    public static T? Deserialize<T>(EchoObject? value) => (T?)Deserialize(value, typeof(T));
    public static object? Deserialize(EchoObject? value, Type targetType) => Deserialize(value, targetType, new SerializationContext());
    public static T? Deserialize<T>(EchoObject? value, SerializationContext context) => (T?)Deserialize(value, typeof(T), context);

    public static object? Deserialize(EchoObject? value, Type targetType, SerializationContext context)
    {
        if (value?.TagType == EchoType.Null || value == null) return null;

        if (value.GetType() == targetType) return value;

        // STEP 1: Extract type information and data (centralized)
        var envelope = ExtractTypeEnvelope(value, targetType);

        // STEP 2: Determine actual type to deserialize to
        var actualType = envelope.ActualType ?? targetType;

        // STEP 3: Get formatter and deserialize data (no type logic in formatter)
        var format = GetFormatForType(actualType);
        return format.Deserialize(envelope.Data, actualType, context);
    }

    #endregion

    #region Type Preservation Logic

    private static bool ShouldPreserveType(Type? targetType, Type actualType, SerializationContext context)
    {
        return context.TypeMode switch {
            TypeMode.Aggressive => true,
            TypeMode.None => false,
            TypeMode.Auto => IsTypePreservationNeeded(targetType, actualType, context),
            _ => true
        };
    }

    private static bool IsTypePreservationNeeded(Type? targetType, Type actualType, SerializationContext context)
    {
        // Never preserve type for exact matches
        if (targetType == actualType) return false;

        return true;

        //// Always preserve for these cases:
        //if (targetType == null ||                           // Unknown target
        //    targetType == typeof(object) ||                 // Boxed objects
        //    targetType.IsInterface ||                       // Interface implementations
        //    targetType.IsAbstract)                          // Abstract class implementations
        //    return true;
        //
        //// Check if actual type is assignable to target (polymorphism)
        //if (!targetType.IsAssignableFrom(actualType))
        //    return true;
        //
        //// Preserve type for derived classes
        //if (targetType != actualType)
        //    return true;
        //
        //return false;
    }

    private static EchoObject WrapWithTypeEnvelope(EchoObject data, Type? typeToPreserve, SerializationContext context)
    {
        if (typeToPreserve == null)
            return data; // No wrapping needed

        // For primitives and simple types, use compact representation
        if (IsSimpleType(typeToPreserve))
            return CreateCompactTypeWrapper(data, typeToPreserve);

        // For complex types, use full representation
        return CreateFullTypeWrapper(data, typeToPreserve);
    }

    private static bool IsSimpleType(Type type)
    {
        return type.IsPrimitive ||
               type == typeof(string) ||
               type == typeof(decimal) ||
               type == typeof(DateTime) ||
               type == typeof(Guid) ||
               type.IsEnum;
    }

    private static EchoObject CreateCompactTypeWrapper(EchoObject data, Type type)
    {
        // For primitives, embed type in the tag itself using a special format
        var compound = EchoObject.NewCompound();
        compound["$t"] = new EchoObject(TypeNameRegistry.GetCompactTypeName(type)); // Compact type name
        compound["$v"] = data; // Value
        return compound;
    }

    private static EchoObject CreateFullTypeWrapper(EchoObject data, Type type)
    {
        if (data.TagType == EchoType.Compound)
        {
            // Only add type wrapper if the data isn't already a compound with type info
            if (data.Contains("$type"))
                return data; // Already has type info
            else
            {
                // Merge with existing compound
                data["$type"] = new EchoObject(TypeNameRegistry.GetFullTypeName(type));
                return data; // Already has type info
            }
        }

        var compound = EchoObject.NewCompound();
        compound["$type"] = new EchoObject(TypeNameRegistry.GetFullTypeName(type));
        compound["$value"] = data;
        return compound;
    }

    #endregion

    #region Type Extraction Logic

    private static TypeEnvelope ExtractTypeEnvelope(EchoObject value, Type targetType)
    {
        // Handle compact type wrapper (for primitives)
        if (value.TagType == EchoType.Compound &&
            value.TryGet("$t", out var compactType) &&
            value.TryGet("$v", out var compactValue))
        {
            var type = TypeNameRegistry.ResolveCompactTypeName(compactType.StringValue);
            return new TypeEnvelope { ActualType = type, Data = compactValue };
        }

        // Handle full type wrapper
        if (value.TagType == EchoType.Compound && value.TryGet("$type", out var typeTag))
        {
            var type = TypeNameRegistry.ResolveFullTypeName(typeTag.StringValue) ?? targetType;

            // If there's a $value, use that as data
            if (value.TryGet("$value", out var dataValue))
                return new TypeEnvelope { ActualType = type, Data = dataValue };

            //// Otherwise, the compound itself is the data (minus type info)
            //var dataCompound = EchoObject.NewCompound();
            //foreach (var kvp in value.Tags.Where(k => !k.Key.StartsWith("$")))
            //    dataCompound[kvp.Key] = kvp.Value;
            // We dont actually have to minus the type info, since it is prefixed with $, formatters should ignore it.

            return new TypeEnvelope { ActualType = type, Data = value };
        }

        // No type wrapper - use as-is
        return new TypeEnvelope { ActualType = null, Data = value };
    }

    private class TypeEnvelope
    {
        public Type? ActualType { get; set; }
        public EchoObject Data { get; set; } = null!;
    }

    #endregion

    #region Format Management

    private static ISerializationFormat GetFormatForType(Type type)
    {
        return _formatCache.GetOrAdd(type, t =>
            _formats.FirstOrDefault(f => f.CanHandle(t))
            ?? throw new NotSupportedException($"No format handler found for type {t}"));
    }

    #endregion
}
