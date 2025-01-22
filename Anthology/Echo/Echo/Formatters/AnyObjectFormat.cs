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

        if (id.HasValue)
            compound["$id"] = new(EchoType.Int, id.Value);

        // Handle type information based on TypeMode
        bool shouldIncludeType = context.TypeMode switch {
            TypeMode.Aggressive => true, // Always include type information
            TypeMode.None => false, // Never include type information
            TypeMode.Auto => targetType == typeof(object) || targetType != actualType, // Include type information if target is object or actual type is different
            _ => true // Default to aggressive for safety
        };

        if (shouldIncludeType)
            compound["$type"] = new(EchoType.String, actualType.FullName);

        context.EndDependencies();

        return compound;
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        // Sometimes compounds can be direct values
        // For example if you serialize object[] { 1, 2, 3 } you get a ListTag with 3 IntTags
        // So deserialize needs to support returning the direct value
        if (value.TagType != EchoType.Compound)
        {
            if (value.TagType == EchoType.Null)
                return null;
            else if (value.TagType == EchoType.Byte)
                return value.ByteValue;
            else if (value.TagType == EchoType.sByte)
                return value.sByteValue;
            else if (value.TagType == EchoType.Short)
                return value.ShortValue;
            else if (value.TagType == EchoType.UShort)
                return value.UShortValue;
            else if (value.TagType == EchoType.Int)
                return value.IntValue;
            else if (value.TagType == EchoType.UInt)
                return value.UIntValue;
            else if (value.TagType == EchoType.Long)
                return value.LongValue;
            else if (value.TagType == EchoType.ULong)
                return value.ULongValue;
            else if (value.TagType == EchoType.Float)
                return value.FloatValue;
            else if (value.TagType == EchoType.Double)
                return value.DoubleValue;
            else if (value.TagType == EchoType.Decimal)
                return value.DecimalValue;
            else if (value.TagType == EchoType.Bool)
                return value.BoolValue;
            else if (value.TagType == EchoType.String)
                return value.StringValue;
            else if (value.TagType == EchoType.ByteArray)
                return value.ByteArrayValue;
            else
            {
                Serializer.Logger.Error($"Failed to deserialize value of type {value.TagType}, EchoObject is not a compound and not a known value type.");
                return null;
            }
        }

        EchoObject? id = null;
        if (!targetType.IsValueType &&
            value.TryGet("$id", out id) &&
            context.idToObject.TryGetValue(id.IntValue, out object? existingObj))
        {
            return existingObj;
        }

        // Determine the actual type to instantiate
        Type objectType;
        if (value.TryGet("$type", out EchoObject? typeProperty))
        {
            Type? resolvedType = ReflectionUtils.FindTypeByName(typeProperty.StringValue);
            if (resolvedType == null)
            {
                Serializer.Logger.Error($"Couldn't find Type: {typeProperty.StringValue}");
                return null;
            }
            objectType = resolvedType;
        }
        else
        {
            // If no type information is present, use the target type
            objectType = targetType;
        }

        if (objectType.IsInterface || objectType.IsAbstract)
        {
            Serializer.Logger.Error($"Cannot deserialize to interface or abstract type: {objectType.FullName}.");
            return null;
        }

        object result;
        try
        {
            result = Activator.CreateInstance(objectType, nonPublic: true);
            if (result == null)
                throw new Exception(); // Throw, it will get caught
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

        if (!objectType.IsValueType && id != null)
            context.idToObject[id.IntValue] = result;

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

    private bool TryGetFieldValue(EchoObject compound, System.Reflection.FieldInfo field, out EchoObject value)
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
        foreach (FormerlySerializedAsAttribute formerName in Attribute.GetCustomAttributes(field, typeof(FormerlySerializedAsAttribute)))
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
