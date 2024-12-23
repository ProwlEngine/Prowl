// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections;

namespace Prowl.Echo.Formatters;

internal sealed class CollectionFormat : ISerializationFormat
{
    public bool CanHandle(Type type) =>
        type.IsArray ||
        (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>));

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        if (value is Array array)
        {
            // target type is the Array itself, we want the element type
            var elementType = targetType!.GetElementType()
                ?? throw new InvalidOperationException("Array element type is null");

            if (array.Rank == 1)
            {
                // Single dimensional array
                List<EchoObject> tags = new();
                foreach (var item in array)
                    tags.Add(Serializer.Serialize(elementType, item, context));
                return new EchoObject(tags);
            }
            else
            {
                // Multi-dimensional array
                var compound = EchoObject.NewCompound();

                // Store dimensions
                var dimensions = new int[array.Rank];
                for (int i = 0; i < array.Rank; i++)
                    dimensions[i] = array.GetLength(i);

                compound["dimensions"] = Serializer.Serialize(dimensions, context); // Just serializing an int array so dont need to pass target type

                // Store elements
                List<EchoObject> elements = new();
                SerializeMultiDimensionalArray(elementType, array, new int[array.Rank], 0, elements, context);
                compound["elements"] = new EchoObject(elements);

                return compound;
            }
        }
        else
        {
            // target type is the Array itself, we want the element type
            var elementType = targetType!.GetGenericArguments()[0];

            var list = value as IList ?? throw new InvalidOperationException("Expected IList type");
            List<EchoObject> tags = new();
            foreach (var item in list)
                tags.Add(Serializer.Serialize(elementType, item, context));
            return new EchoObject(tags);
        }
    }

    private void SerializeMultiDimensionalArray(Type elementType, Array array, int[] indices, int dimension, List<EchoObject> elements, SerializationContext context)
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
        if (targetType.IsArray)
        {
            Type elementType = targetType.GetElementType()
                ?? throw new InvalidOperationException("Array element type is null");

            if (value.TagType == EchoType.List)
            {
                // Single dimensional array
                var array = Array.CreateInstance(elementType, value.Count);
                for (int idx = 0; idx < array.Length; idx++)
                    array.SetValue(Serializer.Deserialize(value[idx], elementType, context), idx);
                return array;
            }
            else if (value.TagType == EchoType.Compound)
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

    private void DeserializeMultiDimensionalArray(Array array, int[] indices, int dimension, List<EchoObject> elements, ref int elementIndex, Type elementType, SerializationContext context)
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
