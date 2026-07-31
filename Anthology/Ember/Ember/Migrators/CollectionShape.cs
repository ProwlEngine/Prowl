using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Prowl.Ember;

internal enum CollectionKind
{
    None,

    /// <summary>Ordered and indexable. Elements can be replaced in place.</summary>
    Sequence,

    /// <summary>Keyed by hash or comparison. Has to be rebuilt, because the keys' hash codes change.</summary>
    HashKeyed,

    /// <summary>Thread safe, drained through a snapshot rather than indexed.</summary>
    ProducerConsumer,

    /// <summary>A conditional weak table, which is keyed but is not a dictionary and cannot be counted.</summary>
    WeakTable,
}

/// <summary>
/// What a container is, worked out from the interfaces it implements rather than from a table of known types.
/// One probe replaces a class per concrete container, and picks up user types deriving from them for free.
/// </summary>
internal sealed class CollectionShape
{
    public CollectionKind Kind { get; private init; }
    public Type[] ItemTypes { get; private init; } = Array.Empty<Type>();

    public static readonly CollectionShape Unknown = new() { Kind = CollectionKind.None };

    // Probing walks every interface a type implements, and every migrator asks about every type it is offered,
    // so the answer is memoised. Keyed weakly so an unloaded assembly's types are not pinned.
    private static readonly ConditionalWeakTable<Type, CollectionShape> s_probed = new();

    public static CollectionShape Probe(Type type) => s_probed.GetValue(type, static t => ProbeUncached(t));

    private static CollectionShape ProbeUncached(Type type)
    {
        if (type.ContainsGenericParameters) return Unknown;
        if (type.IsArray || type.IsPrimitive || type == typeof(string)) return Unknown;
        if (IsUnsuitable(type)) return Unknown;

        if (Definition(type) == typeof(ConditionalWeakTable<,>))
            return new CollectionShape { Kind = CollectionKind.WeakTable, ItemTypes = type.GetGenericArguments() };

        Type[]? dictionary = null, set = null, producerConsumer = null, list = null;

        foreach (var contract in Interfaces(type))
        {
            if (!contract.IsConstructedGenericType) continue;

            var definition = contract.GetGenericTypeDefinition();

            if (definition == typeof(IDictionary<,>)) dictionary ??= contract.GetGenericArguments();
            else if (definition == typeof(ISet<>)) set ??= contract.GetGenericArguments();
            else if (definition == typeof(IProducerConsumerCollection<>)) producerConsumer ??= contract.GetGenericArguments();
            else if (definition == typeof(IList<>)) list ??= contract.GetGenericArguments();
        }

        // Keyed shapes win: a type implementing both is keyed, and being keyed is what forces a rebuild.
        if (dictionary != null) return new CollectionShape { Kind = CollectionKind.HashKeyed, ItemTypes = dictionary };
        if (set != null) return new CollectionShape { Kind = CollectionKind.HashKeyed, ItemTypes = set };
        if (producerConsumer != null) return new CollectionShape { Kind = CollectionKind.ProducerConsumer, ItemTypes = producerConsumer };
        if (list != null) return new CollectionShape { Kind = CollectionKind.Sequence, ItemTypes = list };

        return Unknown;
    }

    /// <summary>
    /// Types that present a mutable interface they do not honour, or whose order a drain and refill would not
    /// preserve. Rebuilding these is worse than letting the field walk handle them.
    /// </summary>
    private static bool IsUnsuitable(Type type)
    {
        // A struct container is boxed by the time a plan sees it, so draining and refilling would mutate the box
        // and leave the field holding its original value. Field walking carries its storage across instead.
        if (type.IsValueType) return true;

        // Immutable and frozen collections implement IList and IDictionary with throwing mutators. The two
        // whose hash trees genuinely have to be rebuilt are claimed by ImmutableMigrator, ahead of this one.
        var space = type.Namespace;
        if (space is "System.Collections.Immutable" or "System.Collections.Frozen") return true;

        // A stack hands back its contents top first, so refilling in that order would invert it.
        var definition = Definition(type);
        return definition == typeof(ConcurrentStack<>) || definition == typeof(Stack<>);
    }

    private static IEnumerable<Type> Interfaces(Type type)
    {
        if (type.IsInterface) yield return type;
        foreach (var contract in type.GetInterfaces())
            yield return contract;
    }

    private static Type? Definition(Type type)
        => type.IsConstructedGenericType ? type.GetGenericTypeDefinition() : null;

    public bool IsKeyed => Kind is CollectionKind.HashKeyed or CollectionKind.WeakTable;
    public Type ElementType => ItemTypes[0];
    public Type? ValueType => ItemTypes.Length > 1 ? ItemTypes[1] : null;

    public IKeyedAccessor CreateKeyedAccessor()
    {
        var accessor = Kind switch
        {
            CollectionKind.WeakTable => typeof(WeakTableAccessor<,>).MakeGenericType(ItemTypes),
            _ when ItemTypes.Length == 2 => typeof(DictionaryAccessor<,>).MakeGenericType(ItemTypes),
            _ => typeof(SetAccessor<>).MakeGenericType(ItemTypes),
        };

        return (IKeyedAccessor)Activator.CreateInstance(accessor)!;
    }

    public ISequenceAccessor CreateSequenceAccessor()
    {
        var accessor = Kind == CollectionKind.ProducerConsumer
            ? typeof(ProducerConsumerAccessor<>).MakeGenericType(ItemTypes)
            : typeof(ListAccessor<>).MakeGenericType(ItemTypes);

        return (ISequenceAccessor)Activator.CreateInstance(accessor)!;
    }
}

/// <summary>Reads and rebuilds a keyed container without the caller knowing its element types.</summary>
internal interface IKeyedAccessor
{
    IEnumerable<KeyValuePair<object?, object?>> Entries(object collection);
    void Clear(object collection);
    bool Add(object collection, object? key, object? value);

    /// <summary>Whether this instance rejects mutation, which no probe of its type alone can tell.</summary>
    bool IsReadOnly(object collection);
}

internal interface ISequenceAccessor
{
    int Count(object collection);
    IEnumerable<object?> Items(object collection);
    void Clear(object collection);
    void Add(object collection, object? item);
    bool SupportsIndexer { get; }
    void SetAt(object collection, int index, object? item);
}

internal sealed class DictionaryAccessor<TKey, TValue> : IKeyedAccessor where TKey : notnull
{
    public IEnumerable<KeyValuePair<object?, object?>> Entries(object collection)
        => ((IDictionary<TKey, TValue>)collection).Select(x => new KeyValuePair<object?, object?>(x.Key, x.Value));

    public void Clear(object collection) => ((IDictionary<TKey, TValue>)collection).Clear();

    public bool IsReadOnly(object collection) => ((IDictionary<TKey, TValue>)collection).IsReadOnly;

    public bool Add(object collection, object? key, object? value)
    {
        var dictionary = (IDictionary<TKey, TValue>)collection;
        if (key is not TKey typedKey) return false;
        if (dictionary.ContainsKey(typedKey)) return false;

        dictionary.Add(typedKey, (TValue)value!);
        return true;
    }
}

internal sealed class SetAccessor<T> : IKeyedAccessor
{
    public IEnumerable<KeyValuePair<object?, object?>> Entries(object collection)
        => ((ISet<T>)collection).Select(x => new KeyValuePair<object?, object?>(x, x));

    public void Clear(object collection) => ((ISet<T>)collection).Clear();

    public bool IsReadOnly(object collection) => ((ISet<T>)collection).IsReadOnly;

    public bool Add(object collection, object? key, object? value)
        => key is T typed && ((ISet<T>)collection).Add(typed);
}

internal sealed class WeakTableAccessor<TKey, TValue> : IKeyedAccessor
    where TKey : class where TValue : class
{
    public IEnumerable<KeyValuePair<object?, object?>> Entries(object collection)
        => ((ConditionalWeakTable<TKey, TValue>)collection).Select(x => new KeyValuePair<object?, object?>(x.Key, x.Value));

    public void Clear(object collection) => ((ConditionalWeakTable<TKey, TValue>)collection).Clear();

    public bool IsReadOnly(object collection) => false;

    public bool Add(object collection, object? key, object? value)
        => key is TKey typedKey && ((ConditionalWeakTable<TKey, TValue>)collection).TryAdd(typedKey, (TValue)value!);
}

internal sealed class ListAccessor<T> : ISequenceAccessor
{
    public int Count(object collection) => ((IList<T>)collection).Count;

    public IEnumerable<object?> Items(object collection)
    {
        // Only the live elements. A backing array's slack is not part of the collection.
        var list = (IList<T>)collection;
        for (int i = 0; i < list.Count; i++)
            yield return list[i];
    }

    public void Clear(object collection) => ((IList<T>)collection).Clear();
    public void Add(object collection, object? item) => ((IList<T>)collection).Add((T)item!);

    public bool SupportsIndexer => true;
    public void SetAt(object collection, int index, object? item) => ((IList<T>)collection)[index] = (T)item!;
}

internal sealed class ProducerConsumerAccessor<T> : ISequenceAccessor
{
    public int Count(object collection) => ((IProducerConsumerCollection<T>)collection).Count;

    public IEnumerable<object?> Items(object collection)
        => ((IProducerConsumerCollection<T>)collection).ToArray().Select(x => (object?)x);

    public void Clear(object collection)
    {
        var target = (IProducerConsumerCollection<T>)collection;

        var clear = collection.GetType().GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes);
        if (clear != null)
        {
            clear.Invoke(collection, null);
            return;
        }

        while (target.TryTake(out _)) { }
    }

    public void Add(object collection, object? item) => ((IProducerConsumerCollection<T>)collection).TryAdd((T)item!);

    public bool SupportsIndexer => false;
    public void SetAt(object collection, int index, object? item) => throw new NotSupportedException();
}
