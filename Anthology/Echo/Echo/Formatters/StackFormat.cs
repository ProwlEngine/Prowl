// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections;

namespace Prowl.Echo.Formatters;

internal sealed class StackFormat : ISerializationFormat
{
    public bool CanHandle(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Stack<>);

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        var elementType = targetType!.GetGenericArguments()[0];
        var stack = (IEnumerable)value;
        List<EchoObject> tags = new();

        // Convert stack to array to preserve order
        var array = stack.Cast<object>().ToArray();
        // Serialize in reverse order so we can maintain order when deserializing
        for (int i = array.Length - 1; i >= 0; i--)
        {
            tags.Add(Serializer.Serialize(elementType, array[i], context));
        }

        return new EchoObject(tags);
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        Type elementType = targetType.GetGenericArguments()[0];
        dynamic stack = Activator.CreateInstance(targetType)
            ?? throw new InvalidOperationException($"Failed to create instance of type: {targetType}");

        // Process elements in order - they were serialized in reverse,
        // so this will maintain proper order
        foreach (var tag in value.List)
        {
            var item = Serializer.Deserialize(tag, elementType, context);
            stack.Push((dynamic)item);
        }

        return stack;
    }
}