// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;

namespace Prowl.Vector.Geometry;

public partial class GeometryData
{
    internal const int VertexKind = 0, EdgeKind = 1, LoopKind = 2, FaceKind = 3;

    /// <summary>
    /// What vertices, edges, loops and faces share: where they sit in their mesh, and their attributes. While an
    /// element is in a mesh its attributes live in the mesh, one column per attribute, which is what keeps large meshes
    /// cheap to copy. An element that is in no mesh, new or removed, carries its own.
    /// </summary>
    public abstract class Element
    {
        internal GeometryData? Owner;
        internal int Slot = -1;
        internal Dictionary<string, AttributeValue>? Loose;

        internal abstract int Kind { get; }

        /// <summary>Where the element is in its mesh's list, or -1 when it is in no mesh. Removing another element can change it.</summary>
        public int Index => Slot;

        /// <summary>
        /// The element's attributes by name. Reads and writes go straight to the mesh. Assigning a dictionary copies its
        /// entries in, replacing what was there.
        /// </summary>
        public AttributeView Attributes
        {
            get => new AttributeView(this);
            set => value.CopyTo(this);
        }

        /// <summary>The value of one attribute. A declared attribute always has one, its default until it is set.</summary>
        public bool TryGetAttribute(string name, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out AttributeValue value)
        {
            if (Owner != null) return Owner._stores[Kind].TryGet(name, Slot, out value);
            if (Loose != null) return Loose.TryGetValue(name, out value);
            value = null;
            return false;
        }

        public void SetAttribute(string name, AttributeValue value)
        {
            if (Owner != null) Owner._stores[Kind].Set(name, Slot, value);
            else (Loose ??= new Dictionary<string, AttributeValue>())[name] = value;
        }

        /// <summary>Takes the value of one attribute away. A declared attribute then reads its default again. True when there was a value.</summary>
        public bool ClearAttribute(string name)
        {
            if (Owner != null) return Owner._stores[Kind].Clear(name, Slot);
            return Loose?.Remove(name) ?? false;
        }
    }

    /// <summary>
    /// The attributes of one element, used like a dictionary of name to value. It is a window, not a copy: it reads and
    /// writes the element it came from. <see cref="ToDictionary"/> gives a separate copy. Code that reads many elements
    /// in a loop is better off with <see cref="Element.TryGetAttribute"/> and <see cref="Element.SetAttribute"/>, which
    /// make no window.
    /// </summary>
    public sealed class AttributeView : IEnumerable<KeyValuePair<string, AttributeValue>>
    {
        private readonly Element? _element;
        private readonly Dictionary<string, AttributeValue>? _map;

        internal AttributeView(Element element)
        {
            _element = element;
            _map = null;
        }

        private AttributeView(Dictionary<string, AttributeValue> map)
        {
            _element = null;
            _map = map;
        }

        /// <summary>Lets a dictionary be assigned to an element's attributes.</summary>
        public static implicit operator AttributeView(Dictionary<string, AttributeValue> map) => new(map);

        public AttributeValue this[string name]
        {
            get => TryGetValue(name, out AttributeValue? value) ? value : throw new KeyNotFoundException($"The element has no attribute '{name}'.");
            set
            {
                if (_map != null) _map[name] = value;
                else _element!.SetAttribute(name, value);
            }
        }

        public bool TryGetValue(string name, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out AttributeValue value)
        {
            if (_map != null) return _map.TryGetValue(name, out value);
            return _element!.TryGetAttribute(name, out value);
        }

        public bool ContainsKey(string name) => TryGetValue(name, out _);

        /// <summary>Takes the value away. A declared attribute goes back to its default.</summary>
        public bool Remove(string name)
        {
            if (_map != null) return _map.Remove(name);
            return _element!.ClearAttribute(name);
        }

        public void Clear()
        {
            if (_map != null) _map.Clear();
            else if (_element?.Owner is { } mesh) mesh._stores[_element.Kind].ClearSlot(_element.Slot);
            else _element?.Loose?.Clear();
        }

        public int Count
        {
            get
            {
                int count = 0;
                foreach (var _ in this) count++;
                return count;
            }
        }

        /// <summary>A separate dictionary of the same values. The values themselves are shared, not copied.</summary>
        public Dictionary<string, AttributeValue> ToDictionary()
        {
            var copy = new Dictionary<string, AttributeValue>();
            foreach (var pair in this) copy[pair.Key] = pair.Value;
            return copy;
        }

        internal void CopyTo(Element target)
        {
            if (_element == target) return;
            // Read fully before the target is cleared, in case the source reads from the same mesh
            Dictionary<string, AttributeValue> source = _map ?? ToDictionary();
            AttributeView view = new(target);
            view.Clear();
            foreach (var pair in source) view[pair.Key] = pair.Value;
        }

        public IEnumerator<KeyValuePair<string, AttributeValue>> GetEnumerator()
        {
            if (_map != null)
            {
                foreach (var pair in _map) yield return pair;
            }
            else if (_element?.Owner is { } mesh)
            {
                AttributeStore store = mesh._stores[_element.Kind];
                int slot = _element.Slot;
                for (int c = 0; c < store.Columns.Count; c++)
                {
                    if (store.TryGet(store.Columns[c], slot, out AttributeValue? value))
                        yield return new KeyValuePair<string, AttributeValue>(store.Columns[c].Name, value);
                }
            }
            else if (_element?.Loose != null)
            {
                foreach (var pair in _element.Loose) yield return pair;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>One attribute of one kind of element: a value per element, by the element's place in its list. Null is the default.</summary>
    internal sealed class AttributeColumn
    {
        public readonly string Name;
        public AttributeDefinition? Definition;
        public AttributeValue?[] Values = [];

        public AttributeColumn(string name) => Name = name;
    }

    /// <summary>The attribute columns of one kind of element.</summary>
    internal sealed class AttributeStore
    {
        public readonly List<AttributeColumn> Columns = new();
        private readonly Dictionary<string, AttributeColumn> _byName = new();

        public AttributeColumn? Find(string name) => _byName.TryGetValue(name, out AttributeColumn? column) ? column : null;

        public AttributeColumn Column(string name)
        {
            if (!_byName.TryGetValue(name, out AttributeColumn? column))
            {
                column = new AttributeColumn(name);
                _byName[name] = column;
                Columns.Add(column);
            }
            return column;
        }

        public void Reset()
        {
            Columns.Clear();
            _byName.Clear();
        }

        public bool Remove(string name)
        {
            if (!_byName.Remove(name, out AttributeColumn? column)) return false;
            Columns.Remove(column);
            return true;
        }

        public bool TryGet(string name, int slot, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out AttributeValue value)
        {
            if (_byName.TryGetValue(name, out AttributeColumn? column)) return TryGet(column, slot, out value);
            value = null;
            return false;
        }

        // A declared attribute always has a value: the default is put in place the first time it is asked for, so it can be changed in place
        public bool TryGet(AttributeColumn column, int slot, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out AttributeValue value)
        {
            value = slot < column.Values.Length ? column.Values[slot] : null;
            if (value != null) return true;
            if (column.Definition == null) return false;
            value = AttributeValue.Copy(column.Definition.DefaultValue);
            Write(column, slot, value);
            return true;
        }

        public void Set(string name, int slot, AttributeValue value) => Write(Column(name), slot, value);

        private static void Write(AttributeColumn column, int slot, AttributeValue? value)
        {
            if (slot >= column.Values.Length)
            {
                if (value == null) return;
                Array.Resize(ref column.Values, Math.Max(slot + 1, Math.Max(16, column.Values.Length * 2)));
            }
            column.Values[slot] = value;
        }

        public bool Clear(string name, int slot)
        {
            AttributeColumn? column = Find(name);
            if (column == null || slot >= column.Values.Length || column.Values[slot] == null) return false;
            column.Values[slot] = null;
            return true;
        }

        public void ClearSlot(int slot)
        {
            foreach (AttributeColumn column in Columns)
                if (slot < column.Values.Length) column.Values[slot] = null;
        }

        /// <summary>The values an element has been given, for it to carry once it leaves the mesh. Null when it has none.</summary>
        public Dictionary<string, AttributeValue>? Take(int slot)
        {
            Dictionary<string, AttributeValue>? taken = null;
            foreach (AttributeColumn column in Columns)
            {
                if (slot >= column.Values.Length || column.Values[slot] is not { } value) continue;
                (taken ??= new Dictionary<string, AttributeValue>())[column.Name] = value;
                column.Values[slot] = null;
            }
            return taken;
        }

        /// <summary>Moves an element's values to another place in the list, as when the last element fills a gap.</summary>
        public void Move(int from, int to)
        {
            foreach (AttributeColumn column in Columns)
            {
                AttributeValue? value = from < column.Values.Length ? column.Values[from] : null;
                if (from < column.Values.Length) column.Values[from] = null;
                Write(column, to, value);
                if (value == null && to < column.Values.Length) column.Values[to] = null;
            }
        }
    }

    private readonly AttributeStore[] _stores = [new AttributeStore(), new AttributeStore(), new AttributeStore(), new AttributeStore()];

    /// <summary>Whether the vertex is in this mesh. Unlike searching the list this takes no time.</summary>
    public bool Contains(Vertex vertex) => vertex != null && vertex.Owner == this && vertex.Slot >= 0;

    /// <inheritdoc cref="Contains(Vertex)"/>
    public bool Contains(Edge edge) => edge != null && edge.Owner == this && edge.Slot >= 0;

    /// <inheritdoc cref="Contains(Vertex)"/>
    public bool Contains(Loop loop) => loop != null && loop.Owner == this && loop.Slot >= 0;

    /// <inheritdoc cref="Contains(Vertex)"/>
    public bool Contains(Face face) => face != null && face.Owner == this && face.Slot >= 0;

    /// <summary>
    /// The names of every attribute any vertex holds a value for, whether or not it was declared. Flags and other
    /// optional data are set without a declaration, so the declared list alone misses them.
    /// </summary>
    public List<string> VertexAttributeNames() => ColumnNames(VertexKind, Vertices.Count);

    /// <inheritdoc cref="VertexAttributeNames"/>
    public List<string> EdgeAttributeNames() => ColumnNames(EdgeKind, Edges.Count);

    /// <inheritdoc cref="VertexAttributeNames"/>
    public List<string> LoopAttributeNames() => ColumnNames(LoopKind, Loops.Count);

    /// <inheritdoc cref="VertexAttributeNames"/>
    public List<string> FaceAttributeNames() => ColumnNames(FaceKind, Faces.Count);

    /// <summary>Takes an attribute away from every vertex: its declaration, when it has one, and every value. True when there was anything to take.</summary>
    public bool RemoveVertexAttribute(string name) => Remove(VertexKind, VertexAttributes, name);

    /// <inheritdoc cref="RemoveVertexAttribute"/>
    public bool RemoveEdgeAttribute(string name) => Remove(EdgeKind, EdgeAttributes, name);

    /// <inheritdoc cref="RemoveVertexAttribute"/>
    public bool RemoveLoopAttribute(string name) => Remove(LoopKind, LoopAttributes, name);

    /// <inheritdoc cref="RemoveVertexAttribute"/>
    public bool RemoveFaceAttribute(string name) => Remove(FaceKind, FaceAttributes, name);

    private bool Remove(int kind, List<AttributeDefinition> definitions, string name)
    {
        bool declared = definitions.RemoveAll(d => d.Name == name) > 0;
        return _stores[kind].Remove(name) || declared;
    }

    private List<string> ColumnNames(int kind, int count)
    {
        var names = new List<string>();
        foreach (AttributeColumn column in _stores[kind].Columns)
        {
            int end = Math.Min(count, column.Values.Length);
            for (int i = 0; i < end; i++)
            {
                if (column.Values[i] == null) continue;
                names.Add(column.Name);
                break;
            }
        }
        return names;
    }

    /// <summary>
    /// The places in the vertex list of the vertices whose flag attribute is set, found by reading the attribute's
    /// column, which is far quicker than asking every vertex. A flag is set when it holds an integer other than zero.
    /// </summary>
    public List<int> FlaggedVertices(string attribute) => Flagged(VertexKind, attribute, Vertices.Count);

    /// <inheritdoc cref="FlaggedVertices"/>
    public List<int> FlaggedEdges(string attribute) => Flagged(EdgeKind, attribute, Edges.Count);

    /// <inheritdoc cref="FlaggedVertices"/>
    public List<int> FlaggedFaces(string attribute) => Flagged(FaceKind, attribute, Faces.Count);

    private List<int> Flagged(int kind, string attribute, int count)
    {
        var found = new List<int>();
        if (_stores[kind].Find(attribute) is not { } column) return found;
        AttributeValue?[] values = column.Values;
        int end = Math.Min(count, values.Length);
        for (int i = 0; i < end; i++)
            if (values[i] is IntAttributeValue { Data.Length: > 0 } flag && flag.Data[0] != 0) found.Add(i);
        return found;
    }

    // Puts an element at the end of its list and takes over the attributes it carried
    private void Attach<T>(List<T> list, T element) where T : Element
    {
        element.Owner = this;
        element.Slot = list.Count;
        list.Add(element);
        if (element.Loose is not { } loose) return;
        element.Loose = null;
        AttributeStore store = _stores[element.Kind];
        foreach (var pair in loose) store.Set(pair.Key, element.Slot, pair.Value);
    }

    // Takes an element out of its list by moving the last one into its place, and hands it the attributes it had.
    // The order of the list is not kept. False when the element was not in this mesh.
    private bool Detach<T>(List<T> list, T element) where T : Element
    {
        if (element.Owner != this || element.Slot < 0) return false;
        AttributeStore store = _stores[element.Kind];
        int slot = element.Slot, last = list.Count - 1;
        element.Loose = store.Take(slot);
        if (slot != last)
        {
            T moved = list[last];
            list[slot] = moved;
            moved.Slot = slot;
            store.Move(last, slot);
        }
        list.RemoveAt(last);
        element.Owner = null;
        element.Slot = -1;
        return true;
    }

    // A separate copy of every column of one kind of element that holds anything, values included. With slots, the
    // value at place i of the copy comes from the element at slots[i].
    internal (string Name, AttributeValue?[] Values)[] CopyColumns(int kind, int count, int[]? slots)
    {
        var copies = new List<(string, AttributeValue?[])>();
        foreach (AttributeColumn column in _stores[kind].Columns)
        {
            AttributeValue?[]? values = null;
            for (int i = 0; i < count; i++)
            {
                int slot = slots == null ? i : slots[i];
                if (slot >= column.Values.Length || column.Values[slot] is not { } value) continue;
                (values ??= new AttributeValue?[count])[i] = AttributeValue.Copy(value);
            }
            if (values != null) copies.Add((column.Name, values));
        }
        return copies.ToArray();
    }

    /// <summary>
    /// Takes every element out of the mesh at once. With keepAttributes each element carries off what it had, so it
    /// can be added again as it was. Without, the values are dropped. Definitions stay either way.
    /// </summary>
    internal void DetachAll(bool keepAttributes)
    {
        DetachList(Vertices, keepAttributes);
        DetachList(Edges, keepAttributes);
        DetachList(Loops, keepAttributes);
        DetachList(Faces, keepAttributes);
        if (!keepAttributes)
        {
            foreach (AttributeStore store in _stores)
                foreach (AttributeColumn column in store.Columns) column.Values = [];
        }
    }

    private void DetachList<T>(List<T> list, bool keepAttributes) where T : Element
    {
        if (list.Count == 0) return;
        AttributeStore store = _stores[list[0].Kind];
        bool carry = keepAttributes && store.Columns.Count > 0;
        foreach (T element in list)
        {
            if (carry) element.Loose = store.Take(element.Slot);
            element.Owner = null;
            element.Slot = -1;
        }
        list.Clear();
    }

    // Declares an attribute: its column gets the definition, so elements read the default until they are given a value
    private AttributeDefinition Declare(int kind, List<AttributeDefinition> definitions, string name, AttributeBaseType baseType, int dimensions)
    {
        foreach (AttributeDefinition existing in definitions)
            if (existing.Name == name) return existing;
        var definition = new AttributeDefinition(name, baseType, dimensions);
        definitions.Add(definition);
        _stores[kind].Column(name).Definition = definition;
        return definition;
    }
}
