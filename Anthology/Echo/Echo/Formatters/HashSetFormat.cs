// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections;
using System.Reflection;

namespace Prowl.Echo.Formatters;

internal sealed class HashSetFormat : ISerializationFormat, Cloning.ICloneFormat
{
    public bool CanHandle(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>);

    #region Cloning

    // Buckets are indexed by the hashes of the original items, which cloned items do not share.

    public bool CanClone(Type type) => CanHandle(type);

    public object CreateCloneTarget(object source, object? existingTarget)
    {
        if (existingTarget != null && existingTarget.GetType() == source.GetType())
            return existingTarget;

        return ComparerSupport.CreateCollection(source.GetType(), ComparerSupport.GetComparer(source));
    }

    public void SetupCloneTargets(object source, object target, Cloning.ICloneSetup setup)
    {
        foreach (object item in (IEnumerable)source)
            setup.HandleObject(item, null);
    }

    public void CopyCloneTo(object source, object target, Cloning.ICloneOperation operation)
    {
        // Reflection rather than dynamic, which cannot bind an element type it has no access to.
        var (clear, add) = GetAccessors(target.GetType());

        clear.Invoke(target, null);

        object?[] arguments = new object?[1];
        foreach (object item in (IEnumerable)source)
        {
            object? mapped = operation.GetTarget(item);
            operation.HandleObject(item, mapped);
            arguments[0] = mapped;
            add.Invoke(target, arguments);
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, (MethodInfo Clear, MethodInfo Add)> _accessorCache = new();

    private static (MethodInfo Clear, MethodInfo Add) GetAccessors(Type setType) =>
        _accessorCache.GetOrAdd(setType, static t =>
        (
            t.GetMethod("Clear", Type.EmptyTypes)!,
            t.GetMethod("Add", [t.GetGenericArguments()[0]])!
        ));

    #endregion

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
