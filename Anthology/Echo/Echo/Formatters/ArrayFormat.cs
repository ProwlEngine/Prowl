// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections;

namespace Prowl.Echo.Formatters;

internal sealed class ArrayFormat : ISerializationFormat
{
    public bool CanHandle(Type type) => type.IsArray;

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        var array = (Array)value;
        var elementType = targetType!.GetElementType() ?? typeof(object);
        Type actualType = value.GetType();

        // Create a compound to store array data and type info
        var arrCompound = EchoObject.NewCompound();

        if (array.Rank == 1)
        {
            // Single dimensional array
            List<EchoObject> tags = new();
            foreach (var item in array)
                tags.Add(Serializer.Serialize(elementType, item, context));
            arrCompound["array"] = new EchoObject(tags);
        }
        else
        {
            // Multi-dimensional array
            var dimensions = new int[array.Rank];
            for (int i = 0; i < array.Rank; i++)
                dimensions[i] = array.GetLength(i);

            arrCompound["dimensions"] = Serializer.Serialize(dimensions, context);

            // Store elements
            List<EchoObject> elements = new();
            SerializeMultiDimensionalArray(elementType, array, new int[array.Rank], 0, elements, context);
            arrCompound["elements"] = new EchoObject(elements);
        }

        return arrCompound;
    }

    private static void SerializeMultiDimensionalArray(
        Type elementType,
        Array array,
        int[] indices,
        int dimension,
        List<EchoObject> elements,
        SerializationContext context)
    {
        if (dimension == array.Rank)
        {
            elements.Add(Serializer.Serialize(elementType, array.GetValue(indices), context));
            return;
        }

        for (int i = 0; i < array.GetLength(dimension); i++)
        {
            indices[dimension] = i;
            SerializeMultiDimensionalArray(elementType, array, indices, dimension + 1, elements, context);
        }
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        Type elementType = targetType.GetElementType()
            ?? throw new InvalidOperationException("Array element type is null");

        // If value is a compound with type info
        if (value.TagType == EchoType.Compound)
        {
            // Single dimensional array
            if (value.TryGet("array", out var arrayTag) && arrayTag.TagType == EchoType.List)
            {
                var array = Array.CreateInstance(elementType, arrayTag.Count);
                for (int idx = 0; idx < array.Length; idx++)
                {
                    var item = arrayTag[idx];

                    // Special handling for object arrays - check if the element has its own type info
                    if (elementType == typeof(object) && item.TagType == EchoType.Compound && item.TryGet("$type", out var typeTag))
                    {
                        var actualType = ReflectionUtils.FindTypeByName(typeTag.StringValue);
                        if (actualType != null)
                        {
                            // Deserialize using the specific type found in the element
                            array.SetValue(Serializer.Deserialize(item, actualType, context), idx);
                            continue;
                        }
                    }

                    // Regular deserialization
                    array.SetValue(Serializer.Deserialize(item, elementType, context), idx);
                }
                return array;
            }
            // Multi-dimensional array
            else if (value.TryGet("dimensions", out var dimensionsTag))
            {
                var dimensions = (int[])Serializer.Deserialize(dimensionsTag, typeof(int[]), context)!;
                var elementsTag = value.Get("elements")
                    ?? throw new InvalidOperationException("Missing elements in multi-dimensional array");

                var array = Array.CreateInstance(elementType, dimensions);
                var indices = new int[dimensions.Length];
                int elementIndex = 0;

                DeserializeMultiDimensionalArray(array, indices, 0, elementsTag.List, ref elementIndex, elementType, context);
                return array;
            }
        }

        throw new InvalidOperationException("Invalid tag type for array deserialization");
    }

    private static void DeserializeMultiDimensionalArray(
        Array array,
        int[] indices,
        int dimension,
        List<EchoObject> elements,
        ref int elementIndex,
        Type elementType,
        SerializationContext context)
    {
        if (dimension == array.Rank)
        {
            var item = elements[elementIndex];

            // Special handling for object arrays - check if the element has its own type info
            if (elementType == typeof(object) && item.TagType == EchoType.Compound && item.TryGet("$type", out var typeTag))
            {
                var actualType = ReflectionUtils.FindTypeByName(typeTag.StringValue);
                if (actualType != null)
                {
                    // Deserialize using the specific type found in the element
                    array.SetValue(Serializer.Deserialize(item, actualType, context), indices);
                    elementIndex++;
                    return;
                }
            }

            // Regular deserialization
            array.SetValue(Serializer.Deserialize(item, elementType, context), indices);
            elementIndex++;
            return;
        }

        for (int i = 0; i < array.GetLength(dimension); i++)
        {
            indices[dimension] = i;
            DeserializeMultiDimensionalArray(array, indices, dimension + 1, elements, ref elementIndex, elementType, context);
        }
    }
}
