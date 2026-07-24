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

    // Every key/value is stored inside an entry compound rather than as a top-level compound key. That
    // keeps any key string intact (including "", "$id", "$type") and leaves the outer compound free to
    // carry an $id for reference tracking.
    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        var reference = CollectionReferences.TryWriteReference(value, context, out int id);
        if (reference != null) return reference;

        var dict = (IDictionary)value;
        var (keyType, valueType) = GetTypeArgs(value.GetType());

        var entries = new List<EchoObject>();
        foreach (DictionaryEntry kvp in dict)
        {
            var entry = EchoObject.NewCompound();
            entry.Add("key", Serializer.Serialize(keyType, kvp.Key, context));
            entry.Add("value", Serializer.Serialize(valueType, kvp.Value, context));
            entries.Add(entry);
        }

        var compound = EchoObject.NewCompound();
        compound["entries"] = new EchoObject(entries);
        return CollectionReferences.WrapCompoundBody(compound, id);
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        if (CollectionReferences.TryReadReference(value, context, out var existing))
            return existing;

        var (keyType, valueType) = GetTypeArgs(targetType);

        IDictionary dict = Activator.CreateInstance(targetType) as IDictionary
            ?? throw new InvalidOperationException($"Failed to create instance of type: {targetType}");
        CollectionReferences.Register(value, dict, context);

        // Current form (and the legacy non-string-key form): an "entries" list of {key,value} compounds.
        if (value.TryGet("entries", out var entriesTag) && entriesTag.TagType == EchoType.List)
        {
            foreach (var entry in entriesTag.List)
            {
                if (!entry.TryGet("key", out var keyTag) || !entry.TryGet("value", out var valueTag))
                    throw new InvalidOperationException("Invalid dictionary entry format");

                var key = Serializer.Deserialize(keyTag, keyType, context);
                if (key != null)
                    dict[key] = Serializer.Deserialize(valueTag, valueType, context);
            }
            return dict;
        }

        // Legacy string-key form: keys were stored as compound keys. Read them all, skipping only Echo's
        // own metadata (older readers dropped every "$"-prefixed key, losing legitimate user keys).
        foreach (KeyValuePair<string, EchoObject> tag in value.Tags)
        {
            if (tag.Key is "$id" or "$type")
                continue;
            dict[tag.Key] = Serializer.Deserialize(tag.Value, valueType, context);
        }
        return dict;
    }
}
