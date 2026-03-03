// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

namespace Prowl.Echo.Formatters;

/// <summary>
/// Provides efficient serialization for types marked with [FixedStructure]
/// by using ordinal-based serialization instead of name-based.
/// </summary>
public sealed class FixedStructureFormat : ISerializationFormat
{
    public bool CanHandle(Type type)
    {
        return type.GetCustomAttribute<FixedEchoStructureAttribute>() != null;
    }

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        // If the type has a source-generated ISerializable, use it
        if (value is ISerializable serializable)
        {
            var result = EchoObject.NewCompound();
            serializable.Serialize(ref result, context);
            return result;
        }

        // Reflection fallback
        var list = EchoObject.NewList();

        var fields = value.GetSerializableFields()
            .OrderBy(f => f.Field.MetadataToken)
            .ToArray();

        foreach (var cachedField in fields)
        {
            try
            {
                object? fieldValue = cachedField.Field.GetValue(value);
                EchoObject serializedValue = Serializer.Serialize(cachedField.Field.FieldType, fieldValue, context);
                list.ListAdd(serializedValue);
            }
            catch (Exception ex)
            {
                Serializer.Logger.Error($"Failed to serialize field {cachedField.Field.Name} in fixed structure", ex);
                list.ListAdd(new EchoObject(EchoType.Null, null));
            }
        }

        return list;
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        if (value.TagType != EchoType.List)
            throw new InvalidOperationException("Expected list for fixed structure deserialization");

        // Create instance of target type
        object result = Activator.CreateInstance(targetType, true)
            ?? throw new InvalidOperationException($"Failed to create instance of type: {targetType}");

        // If the type has a source-generated ISerializable, use it
        if (result is ISerializable serializable)
        {
            serializable.Deserialize(value, context);
            return result;
        }

        // Reflection fallback
        var listValue = (List<EchoObject>)value.Value!;

        var fields = result.GetSerializableFields()
            .OrderBy(f => f.Field.MetadataToken)
            .ToArray();

        if (fields.Length != listValue.Count)
        {
            throw new InvalidOperationException(
                $"Field count mismatch during fixed structure deserialization. " +
                $"Expected {fields.Length} fields but got {listValue.Count} values.");
        }

        for (int i = 0; i < fields.Length; i++)
        {
            var cachedField = fields[i];
            var fieldValue = listValue[i];

            try
            {
                object? deserializedValue = Serializer.Deserialize(fieldValue, cachedField.Field.FieldType, context);
                cachedField.Field.SetValue(result, deserializedValue);
            }
            catch (Exception ex)
            {
                Serializer.Logger.Error($"Failed to deserialize field {cachedField.Field.Name} in fixed structure", ex);
            }
        }

        return result;
    }
}