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
        var reference = CollectionReferences.TryWriteReference(value, context, out int id);
        if (reference != null) return reference;

        var elementType = targetType!.GetGenericArguments()[0];

        var hashSet = (IEnumerable)value;
        List<EchoObject> tags = new();
        foreach (var item in hashSet)
            tags.Add(Serializer.Serialize(elementType, item, context));

        var body = CollectionReferences.WrapListBody(new EchoObject(tags), id);
        var comparerTag = ComparerSupport.Serialize(ComparerSupport.GetComparer(value), elementType, context);
        if (comparerTag != null) body["$comparer"] = comparerTag;
        return body;
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        if (CollectionReferences.TryReadReference(value, context, out var existing))
            return existing;

        Type elementType = targetType.GetGenericArguments()[0];
        var comparer = ComparerSupport.Deserialize(value.Get("$comparer"), context);
        dynamic hashSet = ComparerSupport.CreateCollection(targetType, comparer)
            ?? throw new InvalidOperationException($"Failed to create instance of type: {targetType}");
        CollectionReferences.Register(value, hashSet, context);

        foreach (var tag in CollectionReferences.ListItems(value))
        {
            var item = Serializer.Deserialize(tag, elementType, context);
            hashSet.Add((dynamic)item);
        }
        return hashSet;
    }
}
