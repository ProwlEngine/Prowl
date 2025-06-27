// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Prowl.Echo.Formatters;

public sealed class AnyObjectFormat : ISerializationFormat
{
    public bool CanHandle(Type type) => true; // Fallback format for any object

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        var compound = EchoObject.NewCompound();
        Type actualType = value.GetType();
        int? id = null;

        // Handle reference tracking for non-value types
        if (!actualType.IsValueType)
        {
            if (context.objectToId.TryGetValue(value, out int existingId))
            {
                compound["$id"] = new(EchoType.Int, existingId);
                return compound;
            }

            id = context.nextId++;
            context.objectToId[value] = id.Value;
            context.idToObject[id.Value] = value;
        }

        context.BeginDependencies();

        if (value is ISerializationCallbackReceiver callback)
            callback.OnBeforeSerialize();

        // Serialize the object's data
        if (value is ISerializable serializable)
        {
            serializable.Serialize(ref compound, context);
        }
        else
        {
            foreach (System.Reflection.FieldInfo field in value.GetSerializableFields())
            {
                try
                {
                    object? propValue = field.GetValue(value);
                    if (propValue == null)
                    {
                        if (Attribute.GetCustomAttribute(field, typeof(IgnoreOnNullAttribute)) != null)
                            continue;
                        compound.Add(field.Name, new(EchoType.Null, null));
                    }
                    else
                    {
                        // Serialize with field type as target to enable polymorphism detection
                        EchoObject tag = Serializer.Serialize(field.FieldType, propValue, context);
                        compound.Add(field.Name, tag);
                    }
                }
                catch (Exception ex)
                {
                    Serializer.Logger.Error($"Failed to serialize field {field.Name}", ex);
                    // We don't want to stop the serialization process because of a single field, so we just skip it and continue
                }
            }
        }

        // Add reference ID if needed
        if (id.HasValue)
            compound["$id"] = new(EchoType.Int, id.Value);

        // NOTE: Type information is now handled by the centralized Serializer class
        // We don't add $type here - the Serializer will wrap this with type info if needed

        context.EndDependencies();

        return compound;
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        // Handle primitive values that might come through (for backward compatibility)
        if (value.TagType != EchoType.Compound)
        {
            return DeserializePrimitiveValue(value, targetType);
        }

        // Handle reference resolution for non-value types
        EchoObject? id = null;
        if (!targetType.IsValueType &&
            value.TryGet("$id", out id) &&
            context.idToObject.TryGetValue(id.IntValue, out object? existingObj))
        {
            return existingObj;
        }

        // The target type is now passed in correctly by the centralized system
        // We don't need to extract $type here - it's already been handled
        Type objectType = targetType;

        if (objectType.IsInterface || objectType.IsAbstract)
        {
            Serializer.Logger.Error($"Cannot deserialize to interface or abstract type: {objectType.FullName}.");
            return null;
        }

        // Create the object instance
        object result;
        try
        {
            result = Activator.CreateInstance(objectType, nonPublic: true)!;
        }
        catch (MissingMethodException ex)
        {
            Serializer.Logger.Error($"No parameterless constructor found for type: {objectType.FullName}.", ex);
            return null;
        }
        catch (Exception ex)
        {
            Serializer.Logger.Error($"Failed to create instance of type: {objectType.FullName}.", ex);
            return null;
        }

        // Register the object for reference resolution
        if (!objectType.IsValueType && id != null)
            context.idToObject[id.IntValue] = result;

        // Deserialize the object's data
        if (result is ISerializable serializable)
        {
            serializable.Deserialize(value, context);
        }
        else
        {
            foreach (System.Reflection.FieldInfo field in result.GetSerializableFields())
            {
                if (!TryGetFieldValue(value, field, out EchoObject? fieldValue))
                    continue;

                try
                {
                    // Let the centralized deserializer handle type resolution for fields
                    object? deserializedValue = Serializer.Deserialize(fieldValue, field.FieldType, context);

                    if (field.IsInitOnly)
                        Serializer.Logger.Warning($"Setting readonly field '{field.Name}' in type '{objectType.FullName}'.");

                    field.SetValue(result, deserializedValue);
                }
                catch (Exception ex)
                {
                    Serializer.Logger.Error($"Failed to deserialize field {field.Name}", ex);
                    // We don't want to stop the deserialization process because of a single field, so we just skip it and continue
                }
            }
        }

        if (result is ISerializationCallbackReceiver callback)
            callback.OnAfterDeserialize();

        return result;
    }

    private static object? DeserializePrimitiveValue(EchoObject value, Type targetType)
    {
        // Handle primitive values that might be passed directly
        // This provides backward compatibility and handles edge cases
        try
        {
            return value.TagType switch {
                EchoType.Null => null,
                EchoType.Byte => Convert.ChangeType(value.ByteValue, targetType),
                EchoType.sByte => Convert.ChangeType(value.sByteValue, targetType),
                EchoType.Short => Convert.ChangeType(value.ShortValue, targetType),
                EchoType.UShort => Convert.ChangeType(value.UShortValue, targetType),
                EchoType.Int => Convert.ChangeType(value.IntValue, targetType),
                EchoType.UInt => Convert.ChangeType(value.UIntValue, targetType),
                EchoType.Long => Convert.ChangeType(value.LongValue, targetType),
                EchoType.ULong => Convert.ChangeType(value.ULongValue, targetType),
                EchoType.Float => Convert.ChangeType(value.FloatValue, targetType),
                EchoType.Double => Convert.ChangeType(value.DoubleValue, targetType),
                EchoType.Decimal => Convert.ChangeType(value.DecimalValue, targetType),
                EchoType.Bool => Convert.ChangeType(value.BoolValue, targetType),
                EchoType.String => Convert.ChangeType(value.StringValue, targetType),
                EchoType.ByteArray => targetType == typeof(byte[]) ? value.ByteArrayValue :
                                     throw new InvalidCastException($"Cannot convert byte array to {targetType}"),
                _ => throw new NotSupportedException($"Cannot deserialize {value.TagType} as {targetType}")
            };
        }
        catch (Exception ex)
        {
            Serializer.Logger.Error($"Failed to deserialize primitive value of type {value.TagType} to {targetType}", ex);
            return null;
        }
    }

    private static bool TryGetFieldValue(EchoObject compound, System.Reflection.FieldInfo field, out EchoObject? value)
    {
        if (compound.TryGet(field.Name, out value))
            return true;

        // Case-insensitive fallback
        foreach (var key in compound.GetNames())
        {
            if (string.Equals(key, field.Name, StringComparison.OrdinalIgnoreCase))
            {
                value = compound[key];
                return true;
            }
        }

        // Check former names with case-insensitivity
        foreach (FormerlySerializedAsAttribute formerName in Attribute.GetCustomAttributes(field, typeof(FormerlySerializedAsAttribute)).Cast<FormerlySerializedAsAttribute>())
        {
            if (compound.TryGet(formerName.oldName, out value))
                return true;

            // Case-insensitive check for former names
            foreach (var key in compound.GetNames())
            {
                if (string.Equals(key, formerName.oldName, StringComparison.OrdinalIgnoreCase))
                {
                    value = compound[key];
                    return true;
                }
            }
        }

        value = null;
        return false;
    }
}