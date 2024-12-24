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
        return type.GetCustomAttribute<FixedStructureAttribute>() != null;
    }

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        var compound = EchoObject.NewCompound();

        // Create a list to store field values in order
        var list = EchoObject.NewList();

        // Get all serializable fields in declaration order
        var fields = value.GetSerializableFields()
            .OrderBy(f => f.MetadataToken) // Use metadata token to preserve declaration order
            .ToArray();

        // Serialize each field value in order
        foreach (var field in fields)
        {
            try
            {
                object? fieldValue = field.GetValue(value);
                EchoObject serializedValue = Serializer.Serialize(field.FieldType, fieldValue, context);
                list.ListAdd(serializedValue);
            }
            catch (Exception ex)
            {
                Serializer.Logger.Error($"Failed to serialize field {field.Name} in fixed structure", ex);
                // Add null as placeholder to maintain field order
                list.ListAdd(new EchoObject(EchoType.Null, null));
            }
        }

        // This is an unfortunate workaround due to the rest of the library expecting serialization to always output a compound
        compound["V"] = list;
        return compound;
    }

    public object? Deserialize(EchoObject valueCompound, Type targetType, SerializationContext context)
    {
        var value = valueCompound["V"];

        if (value.TagType != EchoType.List)
            throw new InvalidOperationException("Expected list for fixed structure deserialization");

        var listValue = (List<EchoObject>)value.Value!;

        // Create instance of target type
        object result = Activator.CreateInstance(targetType, true)
            ?? throw new InvalidOperationException($"Failed to create instance of type: {targetType}");

        // Get fields in same order as serialization
        var fields = result.GetSerializableFields()
            .OrderBy(f => f.MetadataToken)
            .ToArray();

        // Verify field count matches
        if (fields.Length != listValue.Count)
        {
            throw new InvalidOperationException(
                $"Field count mismatch during fixed structure deserialization. " +
                $"Expected {fields.Length} fields but got {listValue.Count} values.");
        }

        // Deserialize each field value in order
        for (int i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            var fieldValue = listValue[i];

            try
            {
                object? deserializedValue = Serializer.Deserialize(fieldValue, field.FieldType, context);
                field.SetValue(result, deserializedValue);
            }
            catch (Exception ex)
            {
                Serializer.Logger.Error($"Failed to deserialize field {field.Name} in fixed structure", ex);
                // Skip setting this field and continue with others
            }
        }

        return result;
    }
}