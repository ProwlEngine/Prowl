// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections;

namespace Prowl.Echo.Formatters;

internal sealed class DictionaryFormat : ISerializationFormat
{
    public bool CanHandle(Type type) =>
        type.IsAssignableTo(typeof(IDictionary)) &&
        type.IsGenericType;

    public Echo Serialize(object value, SerializationContext context)
    {
        var dict = (IDictionary)value;
        var type = value.GetType();
        var keyType = type.GetGenericArguments()[0];

        if (keyType == typeof(string))
        {
            // string-key behavior
            var tag = Echo.NewCompound();
            foreach (DictionaryEntry kvp in dict)
                tag.Add((string)kvp.Key, Serializer.Serialize(kvp.Value, context));
            return tag;
        }
        else
        {
            // Non-string key behavior
            var compound = Echo.NewCompound();
            var entries = new List<Echo>();

            foreach (DictionaryEntry kvp in dict)
            {
                var entryCompound = Echo.NewCompound();
                entryCompound.Add("key", Serializer.Serialize(kvp.Key, context));
                entryCompound.Add("value", Serializer.Serialize(kvp.Value, context));
                entries.Add(entryCompound);
            }

            compound.Add("$type", new Echo(PropertyType.String, type.FullName));
            compound.Add("entries", new Echo(entries));
            return compound;
        }
    }

    public object? Deserialize(Echo value, Type targetType, SerializationContext context)
    {
        Type keyType = targetType.GetGenericArguments()[0];
        Type valueType = targetType.GetGenericArguments()[1];

        IDictionary dict = Activator.CreateInstance(targetType) as IDictionary
            ?? throw new InvalidOperationException($"Failed to create instance of type: {targetType}");

        if (keyType == typeof(string))
        {
            // string-key behavior
            foreach (KeyValuePair<string, Echo> tag in value.Tags)
                dict.Add(tag.Key, Serializer.Deserialize(tag.Value, valueType, context));
        }
        else
        {
            // Non-string key behavior
            var entries = value.Get("entries");
            if (entries == null || entries.TagType != PropertyType.List)
                throw new InvalidOperationException("Invalid dictionary format");

            foreach (var entry in entries.List)
            {
                if (!entry.TryGet("key", out var keyTag) || !entry.TryGet("value", out var valueTag))
                    throw new InvalidOperationException("Invalid dictionary entry format");

                var key = Serializer.Deserialize(keyTag, keyType, context);
                var val = Serializer.Deserialize(valueTag, valueType, context);

                if (key != null) // Only add if we have a valid key
                    dict.Add(key, val);
            }
        }

        return dict;
    }
}
