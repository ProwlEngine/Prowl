// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

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
            foreach (var cachedField in value.GetSerializableFields())
            {
                try
                {
                    // Check SerializeIf condition (using cached attribute data)
                    if (cachedField.SerializeIfCondition != null)
                    {
                        if (!EvaluateSerializeCondition(value, actualType, cachedField.SerializeIfCondition))
                            continue;
                    }

                    object? propValue = cachedField.Field.GetValue(value);
                    if (propValue == null)
                    {
                        // Use cached IgnoreOnNull flag
                        if (cachedField.HasIgnoreOnNull)
                            continue;
                        compound.Add(cachedField.Field.Name, new(EchoType.Null, null));
                    }
                    else
                    {
                        // Serialize with field type as target to enable polymorphism detection
                        EchoObject tag = Serializer.Serialize(cachedField.Field.FieldType, propValue, context);
                        compound.Add(cachedField.Field.Name, tag);
                    }
                }
                catch (Exception ex)
                {
                    Serializer.Logger.Error($"Failed to serialize field {cachedField.Field.Name}", ex);
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
        if (!objectType.IsValueType && id is not null)
            context.idToObject[id.IntValue] = result;

        // Deserialize the object's data
        if (result is ISerializable serializable)
        {
            serializable.Deserialize(value, context);
        }
        else
        {
            foreach (var cachedField in result.GetSerializableFields())
            {
                if (!TryGetFieldValue(value, cachedField, out EchoObject? fieldValue))
                    continue;

                try
                {
                    // Let the centralized deserializer handle type resolution for fields
                    object? deserializedValue = Serializer.Deserialize(fieldValue, cachedField.Field.FieldType, context);

                    if (cachedField.Field.IsInitOnly)
                        Serializer.Logger.Warning($"Setting readonly field '{cachedField.Field.Name}' in type '{objectType.FullName}'.");

                    cachedField.Field.SetValue(result, deserializedValue);
                }
                catch (Exception ex)
                {
                    Serializer.Logger.Error($"Failed to deserialize field {cachedField.Field.Name}", ex);
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

    private static bool TryGetFieldValue(EchoObject compound, CachedFieldInfo cachedField, out EchoObject? value)
    {
        var fieldName = cachedField.Field.Name;

        if (compound.TryGet(fieldName, out value))
            return true;

        // Case-insensitive fallback
        foreach (var key in compound.GetNames())
        {
            if (string.Equals(key, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                value = compound[key];
                return true;
            }
        }

        // Check former names (using cached attribute data)
        if (cachedField.FormerNames != null)
        {
            foreach (var formerName in cachedField.FormerNames)
            {
                if (compound.TryGet(formerName, out value))
                    return true;

                // Case-insensitive check for former names
                foreach (var key in compound.GetNames())
                {
                    if (string.Equals(key, formerName, StringComparison.OrdinalIgnoreCase))
                    {
                        value = compound[key];
                        return true;
                    }
                }
            }
        }

        value = null;
        return false;
    }

    private static bool EvaluateSerializeCondition(object instance, Type type, string conditionMemberName)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // Try to find a property first
        PropertyInfo? property = type.GetProperty(conditionMemberName, flags);
        if (property != null && property.PropertyType == typeof(bool) && property.CanRead)
        {
            try
            {
                return (bool)property.GetValue(instance)!;
            }
            catch (Exception ex)
            {
                Serializer.Logger.Error($"Failed to evaluate SerializeIf property '{conditionMemberName}'", ex);
                return true; // Default to serializing on error
            }
        }

        // Try to find a field
        System.Reflection.FieldInfo? field = type.GetField(conditionMemberName, flags);
        if (field != null && field.FieldType == typeof(bool))
        {
            try
            {
                return (bool)field.GetValue(instance)!;
            }
            catch (Exception ex)
            {
                Serializer.Logger.Error($"Failed to evaluate SerializeIf field '{conditionMemberName}'", ex);
                return true; // Default to serializing on error
            }
        }

        // Try to find a method
        MethodInfo? method = type.GetMethod(conditionMemberName, flags, null, Type.EmptyTypes, null);
        if (method != null && method.ReturnType == typeof(bool))
        {
            try
            {
                return (bool)method.Invoke(instance, null)!;
            }
            catch (Exception ex)
            {
                Serializer.Logger.Error($"Failed to evaluate SerializeIf method '{conditionMemberName}'", ex);
                return true; // Default to serializing on error
            }
        }

        Serializer.Logger.Warning($"SerializeIf condition member '{conditionMemberName}' not found or does not return bool on type '{type.FullName}'");
        return true; // Default to serializing if condition not found
    }
}
