// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections;
using System.Collections.Concurrent;

namespace Prowl.Echo.Formatters;

internal sealed class ListFormat : ISerializationFormat
{
    private static readonly ConcurrentDictionary<Type, Type> _elementTypeCache = new();

    public bool CanHandle(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);

    private static Type GetElementType(Type listType)
    {
        if (_elementTypeCache.TryGetValue(listType, out var cached))
            return cached;
        var elementType = listType.GetGenericArguments()[0];
        _elementTypeCache.TryAdd(listType, elementType);
        return elementType;
    }

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        var elementType = GetElementType(targetType!);
        var list = value as IList ?? throw new InvalidOperationException("Expected IList type");

        List<EchoObject> tags = new(list.Count);
        for (int i = 0; i < list.Count; i++)
            tags.Add(Serializer.Serialize(elementType, list[i], context));
        return new EchoObject(tags);
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        Type elementType = GetElementType(targetType);
        var list = Activator.CreateInstance(targetType) as IList
            ?? throw new InvalidOperationException($"Failed to create instance of type: {targetType}");

        foreach (var tag in value.List)
            list.Add(Serializer.Deserialize(tag, elementType, context));
        return list;
    }
}
