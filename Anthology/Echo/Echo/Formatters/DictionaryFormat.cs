// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections;
using System.Collections.Concurrent;

namespace Prowl.Echo.Formatters;

internal sealed class DictionaryFormat : ISerializationFormat
{
    private static readonly ConcurrentDictionary<Type, (Type keyType, Type valueType)> _typeArgCache = new();

    public bool CanHandle(Type type) =>
        type.IsAssignableTo(typeof(IDictionary)) &&
        type.IsGenericType;

    private static (Type keyType, Type valueType) GetTypeArgs(Type dictType)
    {
        if (_typeArgCache.TryGetValue(dictType, out var cached))
            return cached;
        var args = dictType.GetGenericArguments();
        var result = (args[0], args[1]);
        _typeArgCache.TryAdd(dictType, result);
        return result;
    }

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        var dict = (IDictionary)value;
        var type = value.GetType();
        var (keyType, valueType) = GetTypeArgs(type);

        if (keyType == typeof(string))
        {
            // string-key behavior
            var tag = EchoObject.NewCompound();
            foreach (DictionaryEntry kvp in dict)
                tag.Add((string)kvp.Key, Serializer.Serialize(valueType, kvp.Value, context));
            return tag;
        }
        else
        {
            // Non-string key behavior
            var compound = EchoObject.NewCompound();
            var entries = new List<EchoObject>();

            foreach (DictionaryEntry kvp in dict)
            {
                var entryCompound = EchoObject.NewCompound();
                entryCompound.Add("key", Serializer.Serialize(keyType, kvp.Key, context));
                entryCompound.Add("value", Serializer.Serialize(valueType, kvp.Value, context));
                entries.Add(entryCompound);
            }

            compound.Add("entries", new EchoObject(entries));
            return compound;
        }
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        var (keyType, valueType) = GetTypeArgs(targetType);

        IDictionary dict = Activator.CreateInstance(targetType) as IDictionary
            ?? throw new InvalidOperationException($"Failed to create instance of type: {targetType}");

        if (keyType == typeof(string))
        {
            // string-key behavior (skip $-prefixed keys like $type, $id)
            foreach (KeyValuePair<string, EchoObject> tag in value.Tags)
                if (!tag.Key.StartsWith('$'))
                    dict.Add(tag.Key, Serializer.Deserialize(tag.Value, valueType, context));
        }
        else
        {
            // Non-string key behavior
            var entries = value.Get("entries");
            if (entries is null || entries.TagType != EchoType.List)
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
