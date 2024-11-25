// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections;

namespace Prowl.Echo.Formatters;

internal sealed class HashSetFormat : ISerializationFormat
{
    public bool CanHandle(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>);

    public Echo Serialize(object value, SerializationContext context)
    {
        var hashSet = (IEnumerable)value;
        List<Echo> tags = new();
        foreach (var item in hashSet)
            tags.Add(Serializer.Serialize(item, context));
        return new Echo(tags);
    }

    public object? Deserialize(Echo value, Type targetType, SerializationContext context)
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
