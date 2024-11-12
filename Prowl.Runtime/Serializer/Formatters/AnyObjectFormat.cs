// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

namespace Prowl.Runtime.Serialization.Formatters;

// DO NOT CACHE REFLECTION! It actually makes it slower! (Atleast with every attempt i've made)
// Modern .NET have internal caching for reflection as well as Source Generators

public class AnyObjectFormat : ISerializationFormat
{
    public bool CanHandle(Type type) => true; // Fallback format for any object

    public SerializedProperty Serialize(object value, Serializer.SerializationContext context)
    {
        var compound = SerializedProperty.NewCompound();
        Type type = value.GetType();

        if (context.objectToId.TryGetValue(value, out int id))
        {
            compound["$id"] = new(PropertyType.Int, id);
            return compound;
        }

        id = context.nextId++;
        context.objectToId[value] = id;
        context.idToObject[id] = value;

        context.BeginDependencies();

        if (value is ISerializationCallbackReceiver callback)
            callback.OnBeforeSerialize();

        if (value is ISerializable serializable)
        {
            compound = serializable.Serialize(context);
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
                        compound.Add(field.Name, new(PropertyType.Null, null));
                    }
                    else
                    {
                        SerializedProperty tag = Serializer.Serialize(propValue, context);
                        compound.Add(field.Name, tag);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogException(new Exception($"Failed to serialize field {field.Name}", ex));
                    // We don't want to stop the serialization process because of a single field, so we just skip it and continue
                }
            }
        }

        compound["$id"] = new(PropertyType.Int, id);
        compound["$type"] = new(PropertyType.String, type.FullName);
        context.EndDependencies();

        return compound;
    }

    public object? Deserialize(SerializedProperty value, Type targetType, Serializer.SerializationContext context)
    {
        if (value.TryGet("$id", out SerializedProperty? id) &&
            context.idToObject.TryGetValue(id.IntValue, out object? existingObj))
            return existingObj;

        if (!value.TryGet("$type", out SerializedProperty? typeProperty))
        {
            Debug.LogError($"Failed to deserialize object, missing type info");
            return null;
        }

        Type? objectType = RuntimeUtils.FindType(typeProperty.StringValue);
        if (objectType == null)
        {
            Debug.LogError($"Couldn't find Type: {typeProperty.StringValue}");
            return null;
        }

        object result = Activator.CreateInstance(objectType, true)
            ?? throw new InvalidOperationException($"Failed to create instance of type: {objectType}");

        if (id != null)
            context.idToObject[id.IntValue] = result;

        if (result is ISerializable serializable)
        {
            serializable.Deserialize(value, context);
        }
        else
        {
            foreach (System.Reflection.FieldInfo field in result.GetSerializableFields())
            {
                if (!TryGetFieldValue(value, field, out SerializedProperty? fieldValue))
                    continue;

                try
                {
                    object? deserializedValue = Serializer.Deserialize(fieldValue, field.FieldType, context);

                    field.SetValue(result, deserializedValue);
                }
                catch (Exception ex)
                {
                    Debug.LogException(new Exception($"Failed to deserialize field {field.Name}", ex));
                    // We don't want to stop the deserialization process because of a single field, so we just skip it and continue
                }
            }
        }

        if (result is ISerializationCallbackReceiver callback)
            callback.OnAfterDeserialize();

        return result;
    }

    private bool TryGetFieldValue(SerializedProperty compound, System.Reflection.FieldInfo field, out SerializedProperty value)
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
