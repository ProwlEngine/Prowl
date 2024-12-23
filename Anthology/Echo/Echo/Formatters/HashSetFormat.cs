// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections;

namespace Prowl.Echo.Formatters;

internal sealed class HashSetFormat : ISerializationFormat
{
    public bool CanHandle(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>);

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        // target type is the Array itself, we want the element type
        var elementType = targetType!.GetGenericArguments()[0];

        var hashSet = (IEnumerable)value;
        List<EchoObject> tags = new();
        foreach (var item in hashSet)
            tags.Add(Serializer.Serialize(elementType, item, context));
        return new EchoObject(tags);
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        Type elementType = targetType.GetGenericArguments()[0];
        dynamic hashSet = Activator.CreateInstance(targetType)
            ?? throw new InvalidOperationException($"Failed to create instance of type: {targetType}");

        foreach (var tag in value.List)
        {
            var item = Serializer.Deserialize(tag, elementType, context);
            if (item != null)
                hashSet.Add((dynamic)item);
        }
        return hashSet;
    }
}
