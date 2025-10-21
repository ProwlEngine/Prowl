// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
namespace Prowl.Echo.Formatters;

internal sealed class TupleFormat : ISerializationFormat
{
    public bool CanHandle(Type type)
    {
        if (!type.IsGenericType)
            return false;

        var genericDef = type.GetGenericTypeDefinition();

        // Check for ValueTuple (1-8 generic arguments)
        if (genericDef == typeof(ValueTuple<>) ||
            genericDef == typeof(ValueTuple<,>) ||
            genericDef == typeof(ValueTuple<,,>) ||
            genericDef == typeof(ValueTuple<,,,>) ||
            genericDef == typeof(ValueTuple<,,,,>) ||
            genericDef == typeof(ValueTuple<,,,,,>) ||
            genericDef == typeof(ValueTuple<,,,,,,>) ||
            genericDef == typeof(ValueTuple<,,,,,,,>))
            return true;

        // Check for classic Tuple (1-8 generic arguments)
        if (genericDef == typeof(Tuple<>) ||
            genericDef == typeof(Tuple<,>) ||
            genericDef == typeof(Tuple<,,>) ||
            genericDef == typeof(Tuple<,,,>) ||
            genericDef == typeof(Tuple<,,,,>) ||
            genericDef == typeof(Tuple<,,,,,>) ||
            genericDef == typeof(Tuple<,,,,,,>) ||
            genericDef == typeof(Tuple<,,,,,,,>))
            return true;

        return false;
    }

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        var type = value.GetType();
        var compound = EchoObject.NewCompound();

        // Store tuple type (ValueTuple or Tuple)
        bool isValueTuple = type.FullName?.StartsWith("System.ValueTuple") ?? false;
        compound["isValueTuple"] = new EchoObject(isValueTuple);

        // Get generic arguments
        var genericArgs = type.GetGenericArguments();
        compound["count"] = new EchoObject(EchoType.Int, genericArgs.Length);

        // Serialize each item (ValueTuple uses fields, Tuple uses properties)
        var items = EchoObject.NewCompound();
        for (int i = 0; i < genericArgs.Length; i++)
        {
            object? itemValue = null;

            // Try field first (ValueTuple)
            var field = type.GetField($"Item{i + 1}");
            if (field != null)
            {
                itemValue = field.GetValue(value);
            }
            else
            {
                // Try property (classic Tuple)
                var property = type.GetProperty($"Item{i + 1}");
                if (property != null)
                {
                    itemValue = property.GetValue(value);
                }
            }

            if (itemValue != null || genericArgs[i].IsClass || Nullable.GetUnderlyingType(genericArgs[i]) != null)
            {
                items[$"Item{i + 1}"] = Serializer.Serialize(genericArgs[i], itemValue, context);
            }
        }
        compound["items"] = items;

        return compound;
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        if (value.TagType == EchoType.Compound)
        {
            if (value.TryGet("isValueTuple", out var isValueTupleTag) &&
                value.TryGet("count", out var countTag) && countTag.TagType == EchoType.Int &&
                value.TryGet("items", out var itemsTag) && itemsTag.TagType == EchoType.Compound)
            {
                bool isValueTuple = isValueTupleTag.BoolValue;
                int count = countTag.IntValue;

                // Get generic arguments from target type
                var genericArgs = targetType.GetGenericArguments();

                if (genericArgs.Length != count)
                    throw new InvalidOperationException($"Tuple generic argument count mismatch. Expected {count}, got {genericArgs.Length}");

                // Deserialize each item
                var itemValues = new object?[count];
                for (int i = 0; i < count; i++)
                {
                    if (itemsTag.TryGet($"Item{i + 1}", out var itemTag))
                    {
                        itemValues[i] = Serializer.Deserialize(itemTag, genericArgs[i], context);
                    }
                }

                // Create the tuple using Activator
                return Activator.CreateInstance(targetType, itemValues);
            }
            else
            {
                throw new InvalidOperationException("Invalid Tuple format.");
            }
        }
        throw new NotSupportedException($"Type '{value.TagType}' is not supported by TupleFormat.");
    }
}
