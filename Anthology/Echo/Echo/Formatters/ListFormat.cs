// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections;

namespace Prowl.Echo.Formatters;

internal sealed class ListFormat : ISerializationFormat
{
    public bool CanHandle(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        var elementType = targetType!.GetGenericArguments()[0];
        var list = value as IList ?? throw new InvalidOperationException("Expected IList type");

        List<EchoObject> tags = new(list.Count);
        foreach (var item in list)
            tags.Add(Serializer.Serialize(elementType, item, context));
        return new EchoObject(tags);
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        Type elementType = targetType.GetGenericArguments()[0];
        var list = Activator.CreateInstance(targetType) as IList
            ?? throw new InvalidOperationException($"Failed to create instance of type: {targetType}");

        foreach (var tag in value.List)
            list.Add(Serializer.Deserialize(tag, elementType, context));
        return list;
    }
}
