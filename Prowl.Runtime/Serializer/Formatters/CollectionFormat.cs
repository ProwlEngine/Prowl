// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;

namespace Prowl.Runtime.Serialization.Formatters;

public class CollectionFormat : ISerializationFormat
{
    public bool CanHandle(Type type) =>
        type.IsArray ||
        (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>));

    public SerializedProperty Serialize(object value, Serializer.SerializationContext context)
    {
        if (value is Array array)
        {
            if (array.Rank == 1)
            {
                // Single dimensional array
                List<SerializedProperty> tags = [];
                foreach (var item in array)
                    tags.Add(Serializer.Serialize(item, context));
                return new SerializedProperty(tags);
            }
            else
            {
                // Multi-dimensional array
                var compound = SerializedProperty.NewCompound();

                // Store dimensions
                var dimensions = new int[array.Rank];
                for (int i = 0; i < array.Rank; i++)
                    dimensions[i] = array.GetLength(i);

                compound["dimensions"] = Serializer.Serialize(dimensions, context);

                // Store elements
                List<SerializedProperty> elements = [];
                SerializeMultiDimensionalArray(array, new int[array.Rank], 0, elements, context);
                compound["elements"] = new SerializedProperty(elements);

                return compound;
            }
        }
        else
        {
            var list = value as IList ?? throw new InvalidOperationException("Expected IList type");
            List<SerializedProperty> tags = [];
            foreach (var item in list)
                tags.Add(Serializer.Serialize(item, context));
            return new SerializedProperty(tags);
        }
    }

    private void SerializeMultiDimensionalArray(Array array, int[] indices, int dimension, List<SerializedProperty> elements, Serializer.SerializationContext context)
    {
        if (dimension == array.Rank)
        {
            elements.Add(Serializer.Serialize(array.GetValue(indices), context));
            return;
        }

        for (int i = 0; i < array.GetLength(dimension); i++)
        {
            indices[dimension] = i;
            SerializeMultiDimensionalArray(array, indices, dimension + 1, elements, context);
        }
    }

    public object? Deserialize(SerializedProperty value, Type targetType, Serializer.SerializationContext context)
    {
        if (targetType.IsArray)
        {
            Type elementType = targetType.GetElementType()
                ?? throw new InvalidOperationException("Array element type is null");

            if (value.TagType == PropertyType.List)
            {
                // Single dimensional array
                var array = Array.CreateInstance(elementType, value.Count);
                for (int idx = 0; idx < array.Length; idx++)
                    array.SetValue(Serializer.Deserialize(value[idx], elementType, context), idx);
                return array;
            }
            else if (value.TagType == PropertyType.Compound)
            {
                // Multi-dimensional array
                var dimensionsTag = value.Get("dimensions")
                    ?? throw new InvalidOperationException("Missing dimensions in multi-dimensional array");
                var dimensions = (int[])Serializer.Deserialize(dimensionsTag, typeof(int[]), context)!;

                var elementsTag = value.Get("elements")
                    ?? throw new InvalidOperationException("Missing elements in multi-dimensional array");
                var elements = elementsTag.List;

                var array = Array.CreateInstance(elementType, dimensions);
                var indices = new int[dimensions.Length];
                int elementIndex = 0;

                DeserializeMultiDimensionalArray(array, indices, 0, elements, ref elementIndex, elementType, context);
                return array;
            }
            else
            {
                throw new InvalidOperationException("Invalid tag type for array deserialization");
            }
        }
        else
        {
            Type elementType = targetType.GetGenericArguments()[0];
            var list = Activator.CreateInstance(targetType) as IList
                ?? throw new InvalidOperationException($"Failed to create instance of type: {targetType}");

            foreach (var tag in value.List)
                list.Add(Serializer.Deserialize(tag, elementType, context));
            return list;
        }
    }

    private void DeserializeMultiDimensionalArray(Array array, int[] indices, int dimension, List<SerializedProperty> elements, ref int elementIndex, Type elementType, Serializer.SerializationContext context)
    {
        if (dimension == array.Rank)
        {
            array.SetValue(Serializer.Deserialize(elements[elementIndex], elementType, context), indices);
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
