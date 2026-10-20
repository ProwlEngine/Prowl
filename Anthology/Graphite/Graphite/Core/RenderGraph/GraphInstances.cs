using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

/// <summary>A texture or buffer an instance binds to an import slot a pass declared.</summary>
public readonly struct GraphImport
{
    /// <summary>The slot the pass declared.</summary>
    public RenderResourceID Id { get; }

    internal RenderTexture? Texture { get; }

    internal DeviceBuffer? Buffer { get; }

    private GraphImport(RenderResourceID id, RenderTexture? texture, DeviceBuffer? buffer)
    {
        Id = id;
        Texture = texture;
        Buffer = buffer;
    }

    /// <summary>Binds a texture to a slot.</summary>
    public static GraphImport Of(RenderResourceID id, RenderTexture texture)
        => new(id, texture ?? throw new ArgumentNullException(nameof(texture)), null);

    /// <summary>Binds a texture to a slot. The graph can read or copy it, not draw into it.</summary>
    public static GraphImport Of(RenderResourceID id, Texture texture)
        => Of(id, new RenderTexture(texture ?? throw new ArgumentNullException(nameof(texture))));

    /// <summary>Binds a buffer to a slot.</summary>
    public static GraphImport Of(RenderResourceID id, DeviceBuffer buffer)
        => new(id, null, buffer ?? throw new ArgumentNullException(nameof(buffer)));
}

/// <summary>One run of the passes of a kind in a dispatch. Its passes read their own resources and those of every ancestor.</summary>
public sealed class GraphInstance
{
    private readonly List<GraphInstance> _children = new();
    private readonly Dictionary<RenderResourceID, GraphImport>? _imports;

    internal GraphInstance(GraphInstanceKey key, GraphInstance? parent, int index, GraphExtent extent, Framebuffer? target, object? data, string? name, Dictionary<RenderResourceID, GraphImport>? imports)
    {
        _imports = imports;
        Key = key;
        Parent = parent;
        Index = index;
        Extent = extent;
        Target = target;
        Data = data;
        Name = name ?? key.ToString();
    }

    public GraphInstanceKey Key { get; }

    /// <summary>The kind of instance it is, which decides the passes it runs.</summary>
    public GraphKind Kind => Key.Kind;

    /// <summary>The instance it runs under, null for the root.</summary>
    public GraphInstance? Parent { get; }

    /// <summary>Position in the dispatch, the root at 0.</summary>
    public int Index { get; }

    public GraphExtent Extent { get; }

    /// <summary>Framebuffer passes declaring the instance target draw into, presented after dispatch when it is a swapchain's.</summary>
    public Framebuffer? Target { get; }

    /// <summary>Whatever the caller attached for its passes to read.</summary>
    public object? Data { get; }

    /// <summary>Display name for profilers and debuggers.</summary>
    public string Name { get; }

    public IReadOnlyList<GraphInstance> Children => _children;

    /// <summary>The attached data as its concrete type.</summary>
    public T DataAs<T>() => (T)Data!;

    internal void AddChild(GraphInstance child) => _children.Add(child);

    internal bool TryGetImport(RenderResourceID id, out GraphImport import)
    {
        import = default;
        return _imports != null && _imports.TryGetValue(id, out import);
    }
}

/// <summary>The instances one dispatch runs: the root, then each instance after its parent, children in the order added.</summary>
public sealed class GraphInstances
{
    private readonly List<GraphInstance> _all = new();
    private readonly HashSet<GraphInstanceKey> _keys = new();

    public GraphInstances(GraphExtent extent = default, Framebuffer? target = null, object? data = null)
        => Reset(extent, target, data);

    public GraphInstance Root { get; private set; } = null!;

    /// <summary>Every instance by index, the root first.</summary>
    public IReadOnlyList<GraphInstance> All => _all;

    /// <summary>Drops every instance and starts over with a new root.</summary>
    public void Reset(GraphExtent extent = default, Framebuffer? target = null, object? data = null)
    {
        _all.Clear();
        _keys.Clear();
        Root = new GraphInstance(new GraphInstanceKey(GraphKind.Root, 0), null, 0, extent, target, data, "Root", null);
        _all.Add(Root);
        _keys.Add(Root.Key);
    }

    /// <summary>Adds an instance of a kind under a parent, the root when none is given. Its key is the kind with the id.</summary>
    public GraphInstance Add(GraphKind kind, int id, GraphInstance? parent = null, GraphExtent extent = default, Framebuffer? target = null, object? data = null, string? name = null, IReadOnlyList<GraphImport>? imports = null)
    {
        if (!kind.IsValid || kind == GraphKind.Root)
            throw new ArgumentException("An instance needs a kind of its own, not the default or root kind.", nameof(kind));
        parent ??= Root;
        if (_all.Count <= parent.Index || _all[parent.Index] != parent)
            throw new ArgumentException("The parent belongs to another set of instances.", nameof(parent));

        GraphInstanceKey key = new(kind, id);
        if (_keys.Contains(key))
            throw new InvalidOperationException($"Two instances share the key {key} in one dispatch.");

        Dictionary<RenderResourceID, GraphImport>? bound = null;
        if (imports != null && imports.Count > 0)
        {
            bound = new Dictionary<RenderResourceID, GraphImport>(imports.Count);
            foreach (GraphImport import in imports)
            {
                if (!import.Id.IsValid || !bound.TryAdd(import.Id, import))
                    throw new ArgumentException("Imports need distinct valid ids.", nameof(imports));
            }
        }

        _keys.Add(key);
        GraphInstance instance = new(key, parent, _all.Count, extent, target, data, name, bound);
        _all.Add(instance);
        parent.AddChild(instance);
        return instance;
    }
}
