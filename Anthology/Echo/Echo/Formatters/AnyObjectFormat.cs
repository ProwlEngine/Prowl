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

        object result = Activator.CreateInstance(objectType, true)
            ?? throw new InvalidOperationException($"Failed to create instance of type: {objectType}");

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

        Attribute[] formerNames = Attribute.GetCustomAttributes(field, typeof(FormerlySerializedAsAttribute));
        foreach (FormerlySerializedAsAttribute formerName in formerNames.Cast<FormerlySerializedAsAttribute>())
        {
            if (compound.TryGet(formerName.oldName, out value))
                return true;
        }

        return false;
    }
}
