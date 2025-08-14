// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections;

namespace Prowl.Echo.Formatters;

internal sealed class CollectionFormat : ISerializationFormat
{
    public bool CanHandle(Type type)
        => type.IsGenericType
        && typeof(IEnumerable).IsAssignableFrom(type)
        && type.GetInterface("ICollection`1") != null
        && !typeof(IDictionary).IsAssignableFrom(type);

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        var elementType = targetType!.GetGenericArguments()[0];
        var enumerable = (IEnumerable)value;
        List<EchoObject> tags = new();
        foreach (var item in enumerable)
            tags.Add(Serializer.Serialize(elementType, item, context));
        return new EchoObject(tags);
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        Type elementType = targetType.GetGenericArguments()[0];
        dynamic collection = Activator.CreateInstance(targetType)
            ?? throw new InvalidOperationException($"Failed to create instance of type: {targetType}");

        foreach (var tag in value.List)
        {
            var item = Serializer.Deserialize(tag, elementType, context);
            if (item != null)
                collection.Add((dynamic)item);
        }

        return collection;
    }
}
