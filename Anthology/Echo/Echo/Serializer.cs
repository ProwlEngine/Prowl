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
            new Formatters.UriFormat(),
            new VersionFormat(),
            new EnumFormat(),
            new TupleFormat(),
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
    {
        var result = Serialize(value?.GetType(), value, context);

        // No targetType was provided by the caller — in Auto mode, ensure the
        // root object includes its type so deserialization can work without
        // the caller needing to know the type upfront.
        if (value != null && context.TypeMode == TypeMode.Auto)
            result = WrapWithTypeEnvelope(result, value.GetType(), context);

        return result;
    }

    public static EchoObject Serialize(Type? targetType, object? value, SerializationContext context)
    {
        if (value == null) return new EchoObject(EchoType.Null, null);

        // Fast path: primitives, string, enum — skip entire pipeline
        if (targetType != null)
        {
            if (targetType.IsEnum)
                return new EchoObject(EchoType.Int, Convert.ToInt32(value));

            if (targetType.IsValueType || targetType == typeof(string) || targetType == typeof(byte[]))
            {
                var result = SerializeFast(targetType, value);
                if (result != null) return result;
            }
        }

        var actualType = value.GetType();

        // Check for serialization override (e.g. external asset references)
        if (context.OnSerialize != null)
        {
            var overrideResult = context.OnSerialize(value, context);
            if (overrideResult != null)
            {
                bool needsType = ShouldPreserveType(targetType, actualType, context);
                return WrapWithTypeEnvelope(overrideResult, needsType ? actualType : null, context);
            }
        }

        // STEP 1: Determine if we need type preservation (centralized logic)
        bool needsTypeInfo = ShouldPreserveType(targetType, actualType, context);

        // STEP 2: Serialize the actual data (formatters don't worry about types)
        var format = GetFormatForType(actualType);
        var serializedData = format.Serialize(actualType, value, context);

        // STEP 3: Wrap with type envelope if needed (centralized)
        return WrapWithTypeEnvelope(serializedData, needsTypeInfo ? actualType : null, context);
    }

    private static EchoObject? SerializeFast(Type type, object value)
    {
        return Type.GetTypeCode(type) switch
        {
            TypeCode.Int32   => new EchoObject((int)value),
            TypeCode.Single  => new EchoObject((float)value),
            TypeCode.Double  => new EchoObject((double)value),
            TypeCode.Boolean => new EchoObject((bool)value),
            TypeCode.String  => new EchoObject((string)value),
            TypeCode.Int64   => new EchoObject((long)value),
            TypeCode.Byte    => new EchoObject((byte)value),
            TypeCode.Char    => new EchoObject((byte)(char)value),
            TypeCode.UInt32  => new EchoObject((uint)value),
            TypeCode.Int16   => new EchoObject((short)value),
            TypeCode.UInt64  => new EchoObject((ulong)value),
            TypeCode.UInt16  => new EchoObject((ushort)value),
            TypeCode.SByte   => new EchoObject((sbyte)value),
            TypeCode.Decimal => new EchoObject((decimal)value),
            TypeCode.Object when type == typeof(byte[]) => new EchoObject((byte[])value),
            _ => null // DateTime, Guid, TimeSpan etc. — use normal path
        };
    }

    public static T? Deserialize<T>(EchoObject? value) => (T?)Deserialize(value, typeof(T));
    public static object? Deserialize(EchoObject? value, Type targetType) => Deserialize(value, targetType, new SerializationContext());
    public static T? Deserialize<T>(EchoObject? value, SerializationContext context) => (T?)Deserialize(value, typeof(T), context);

    public static object? Deserialize(EchoObject? value, Type targetType, SerializationContext context)
    {
        if (value?.TagType == EchoType.Null || value is null) return null;

        if (value.GetType() == targetType) return value;

        // STEP 1: Extract type information and data (centralized)
        var envelope = ExtractTypeEnvelope(value, targetType);

        // STEP 2: Determine actual type to deserialize to
        var actualType = envelope.ActualType ?? targetType;

        // Check for deserialization override (e.g. external asset references)
        if (context.OnDeserialize != null)
        {
            var (handled, result) = context.OnDeserialize(envelope.Data, actualType, context);
            if (handled) return result;
        }

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

            // Remove $type from the data so formatters don't see it as a data entry
            value.Tags.Remove("$type");

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

    internal static ISerializationFormat GetFormatForType(Type type)
    {
        if (_formatCache.TryGetValue(type, out var cached))
            return cached;

        ISerializationFormat? format = null;
        foreach (var f in _formats)
        {
            if (f.CanHandle(type))
            {
                format = f;
                break;
            }
        }

        if (format == null)
            throw new NotSupportedException($"No format handler found for type {type}");

        _formatCache.TryAdd(type, format);
        return format;
    }

    #endregion
}
