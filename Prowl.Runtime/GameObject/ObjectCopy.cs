// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;

namespace Prowl.Runtime;

/// <summary>
/// Pairs objects of a copy's source with objects that already exist, for <see cref="ObjectCopy"/>.
/// </summary>
public sealed class CopyMap
{
    internal readonly Dictionary<object, object> Fills = new(ReferenceEqualityComparer.Instance);
    internal readonly Dictionary<object, object> Links = new(ReferenceEqualityComparer.Instance);
    private SerializationContext? _written, _read;

    /// <summary>The copy of <paramref name="source"/> is read into <paramref name="target"/>.</summary>
    public void Fill(object source, object target) => Fills[source] = target;

    /// <summary>References to <paramref name="source"/> land on <paramref name="target"/>, whose contents are left alone.</summary>
    public void Link(object source, object target) => Links[source] = target;

    /// <summary>What <paramref name="source"/> became: its pair, or once the copy has run, the object made for it.</summary>
    public bool TryGetTarget(object source, out object? target)
    {
        if (Fills.TryGetValue(source, out target) || Links.TryGetValue(source, out target)) return true;
        if (_written != null && _read != null && _written.objectToId.TryGetValue(source, out int id))
            return _read.idToObject.TryGetValue(id, out target);
        target = null;
        return false;
    }

    internal void Ran(SerializationContext written, SerializationContext read)
    {
        _written = written;
        _read = read;
    }
}

/// <summary>
/// Copies GameObject trees and other values in memory by writing them out and reading them back. A reference to
/// something inside the copy lands on its counterpart, and a reference to any other engine object is shared with
/// the original.
/// </summary>
public static class ObjectCopy
{
    /// <summary>A detached copy of <paramref name="source"/>.</summary>
    public static T Clone<T>(T source, CopyMap? map = null) where T : notnull
        => (T)CloneRoots([source], map ?? new CopyMap())[0];

    /// <summary>Copies several roots as one, so a reference from one root to another lands on that root's copy.</summary>
    public static List<T> CloneAll<T>(IEnumerable<T> sources, CopyMap? map = null) where T : notnull
        => CloneRoots(sources.Cast<object>().ToList(), map ?? new CopyMap()).Cast<T>().ToList();

    /// <summary>
    /// Copies a GameObject tree onto an existing one. Objects the map pairs, then components and children of the
    /// same type in the same place, are filled in place so references to them stay valid. What the target has
    /// beyond the source is kept, what it lacks is added, and its objects keep their own identifiers.
    /// </summary>
    public static void CopyTo(GameObject source, GameObject target, CopyMap? map = null)
    {
        map ??= new CopyMap();
        map.Fill(source, target);
        Pair(source, target, map, new HashSet<object>(map.Fills.Values, ReferenceEqualityComparer.Instance));

        var identities = new List<(object Target, Guid Identifier)>();
        foreach (object filled in map.Fills.Values)
            if (filled is GameObject go) identities.Add((go, go.Identifier));
            else if (filled is Component component) identities.Add((component, component.Identifier));

        var links = new SharedLinks(Owned([source], map), map);
        var written = new SerializationContext { ExternalReferences = links };
        EchoObject echo = Serializer.Serialize(typeof(object), source, written);

        var read = new SerializationContext { ExternalReferences = links };
        foreach (var (from, into) in map.Fills)
            if (written.objectToId.TryGetValue(from, out int id))
                read.ReadInto(id, into);

        Serializer.DeserializeInto(echo, target, read);
        map.Ran(written, read);

        foreach (var (filled, identifier) in identities)
            if (filled is GameObject go) go.SetIdentifier(identifier);
            else ((Component)filled).Identifier = identifier;
    }

    private static List<object> CloneRoots(List<object> roots, CopyMap map)
    {
        var links = new SharedLinks(Owned(roots, map), map);
        var written = new SerializationContext { ExternalReferences = links };
        var read = new SerializationContext { ExternalReferences = links };

        // A lone root is written as the outermost value, so one that is an asset is copied rather than referenced.
        List<object> copies;
        if (roots.Count == 1)
            copies = [Serializer.Deserialize(Serializer.Serialize(typeof(object), roots[0], written), typeof(object), read)!];
        else
            copies = Serializer.Deserialize<List<object>>(Serializer.Serialize(typeof(List<object>), roots, written), read)!;

        map.Ran(written, read);
        return copies;
    }

    // Pairs what the caller did not with what sits in the same place in the target, the way a GameObject's own
    // lists line up. A pair is only made when the target object is still free.
    private static void Pair(GameObject source, GameObject target, CopyMap map, HashSet<object> taken)
    {
        if (map.Links.ContainsKey(source)) return;

        if (!map.Fills.ContainsKey(source.Transform) && taken.Add(target.Transform))
            map.Fill(source.Transform, target.Transform);

        List<Component> sourceComponents = source._components, targetComponents = target._components;
        for (int i = 0; i < sourceComponents.Count; i++)
        {
            Component component = sourceComponents[i];
            if (component.IsNotValid() || map.Fills.ContainsKey(component) || map.Links.ContainsKey(component)) continue;
            if (i < targetComponents.Count && targetComponents[i].IsValid() && targetComponents[i].GetType() == component.GetType()
                && taken.Add(targetComponents[i]))
                map.Fill(component, targetComponents[i]);
        }

        for (int i = 0; i < source.Children.Count; i++)
        {
            GameObject child = source.Children[i];
            if (child.IsNotValid() || map.Links.ContainsKey(child)) continue;

            if (map.Fills.TryGetValue(child, out object? paired))
            {
                Pair(child, (GameObject)paired, map, taken);
                continue;
            }

            if (i < target.Children.Count && target.Children[i].IsValid() && taken.Add(target.Children[i]))
            {
                map.Fill(child, target.Children[i]);
                Pair(child, target.Children[i], map, taken);
            }
        }
    }

    // The roots and, for GameObjects, every object of their trees. Linked objects and what lies below them are not.
    private static HashSet<object> Owned(List<object> roots, CopyMap map)
    {
        var owned = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (object root in roots)
        {
            owned.Add(root);
            if (root is GameObject go) AddTree(go);
        }
        return owned;

        void AddTree(GameObject go)
        {
            if (map.Links.ContainsKey(go)) return;
            owned.Add(go);
            foreach (Component component in go._components)
                if (component.IsValid() && !map.Links.ContainsKey(component)) owned.Add(component);
            foreach (GameObject child in go.Children)
                if (child.IsValid()) AddTree(child);
        }
    }

    /// <summary>Writes every engine object outside the copy, and everything the map links, as a key to the live object.</summary>
    private sealed class SharedLinks(HashSet<object> owned, CopyMap map) : IExternalReferenceResolver
    {
        private readonly List<object> _linked = [];
        private readonly Dictionary<object, int> _keys = new(ReferenceEqualityComparer.Instance);

        public object? GetReferenceKey(object value)
        {
            bool linked = map.Links.TryGetValue(value, out object? target);
            if (!linked && (value is not EngineObject || owned.Contains(value))) return null;

            if (!_keys.TryGetValue(value, out int key))
            {
                key = _linked.Count;
                _keys[value] = key;
                _linked.Add(linked ? target! : value);
            }
            return key;
        }

        public object? ResolveReference(object key, Type targetType)
            => key is int index && index >= 0 && index < _linked.Count ? _linked[index] : null;
    }
}
