// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo.Formatters;

public sealed class AnyObjectFormat : ISerializationFormat
{
    public bool CanHandle(Type type) => true; // Fallback format for any object

    public EchoObject Serialize(object value, SerializationContext context)
    {
        var compound = EchoObject.NewCompound();
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
                        EchoObject tag = Serializer.Serialize(propValue, context);
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

        compound["$id"] = new(PropertyType.Int, id);
        compound["$type"] = new(PropertyType.String, type.FullName);
        context.EndDependencies();

        return compound;
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        if (value.TryGet("$id", out EchoObject? id) &&
            context.idToObject.TryGetValue(id.IntValue, out object? existingObj))
            return existingObj;

        if (!value.TryGet("$type", out EchoObject? typeProperty))
        {
            Serializer.Logger.Error($"Failed to deserialize object, missing type info");
            return null;
        }

        Type? objectType = ReflectionUtils.FindType(typeProperty.StringValue);
        if (objectType == null)
        {
            Serializer.Logger.Error($"Couldn't find Type: {typeProperty.StringValue}");
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
