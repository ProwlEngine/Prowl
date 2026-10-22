using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

/// <summary>
/// Passes and resources of one frame, recorded by the owner and run by Execute. One graph lives as long as its owner renders.
/// Execute ends the frame and the next call starts a new one.
/// </summary>
public sealed partial class FrameGraph : IDisposable
{
    private readonly ResourceStore _store = new();
    private readonly List<FrameResource> _resources = new();
    private readonly Dictionary<(RenderResourceID, int), int> _created = new();
    private readonly Dictionary<object, int> _imported = new();
    private readonly List<FramePass> _passes = new();
    private readonly List<GroupInfo> _groups = new();
    private readonly List<int> _groupStack = new();
    private readonly PassBuilder _builder;
    private int _resourceCount;
    private int _passCount;
    private GraphLayout? _layout;
    private bool _disposed;

    public FrameGraph(GraphicsDevice device, string name = "")
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(name);
        Device = device;
        Name = name;
        _builder = new PassBuilder(this);
    }

    public GraphicsDevice Device { get; }

    public string Name { get; }

    /// <summary>The layout of the last executed frame. The same object while its hash is unchanged.</summary>
    public GraphLayout? LastLayout => _layout;

    internal FramePass? Building { get; private set; }

    internal int ResourceCount => _resourceCount;

    internal int PassCount => _passCount;

    internal ResourceStore Store => _store;

    internal FrameResource ResourceAt(int index) => _resources[index];

    internal FramePass PassAt(int index) => _passes[index];

    internal IReadOnlyList<GroupInfo> Groups => _groups;

    public TextureHandle Create(RenderResourceID id, in GraphTextureDesc desc, Lifetime lifetime = default, int scope = 0)
    {
        if (desc.Width <= 0 || desc.Height <= 0)
            throw new ArgumentException($"Texture '{RenderResourceID.ToString(id)}' needs a width and height above zero.", nameof(desc));

        int index = NewResource(id, scope, isTexture: true, lifetime, out FrameResource resource);
        resource.Texture = desc;
        if (lifetime.IsRetained)
            resource.TextureRings = _store.TexturesFor(id, lifetime.Slots);
        return new TextureHandle(id, index, 0);
    }

    public BufferHandle Create(RenderResourceID id, in GraphBufferDesc desc, Lifetime lifetime = default, int scope = 0)
    {
        if (desc.SizeInBytes == 0)
            throw new ArgumentException($"Buffer '{RenderResourceID.ToString(id)}' needs a size above zero.", nameof(desc));

        int index = NewResource(id, scope, isTexture: false, lifetime, out FrameResource resource);
        resource.Buffer = desc;
        if (lifetime.IsRetained)
            resource.BufferRings = _store.BuffersFor(id, lifetime.Slots);
        return new BufferHandle(id, index, 0);
    }

    public TextureHandle Import(RenderTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        return ImportTexture(texture, texture);
    }

    public TextureHandle Import(Texture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (_imported.TryGetValue(texture, out int existing))
            return TextureHandleOf(existing);
        return ImportTexture(texture, new RenderTexture(texture));
    }

    public BufferHandle Import(DeviceBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (_imported.TryGetValue(buffer, out int existing))
        {
            FrameResource known = _resources[existing];
            return new BufferHandle(known.Id, existing, known.Version);
        }

        int index = NewImport(buffer, isTexture: false, buffer.Name, out FrameResource resource);
        resource.ImportedBuffer = buffer;
        return new BufferHandle(resource.Id, index, 0);
    }

    public TextureHandle ImportTarget(Framebuffer target, PixelFormat? depthFormat = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_imported.TryGetValue(target, out int existing))
            return TextureHandleOf(existing);

        int index = NewImport(target, isTexture: true, null, out FrameResource resource);
        resource.IsTarget = true;
        resource.TargetFramebuffer = target;
        resource.TargetDepth = depthFormat;
        return new TextureHandle(resource.Id, index, 0);
    }

    public void AddPass(string name, Action<PassBuilder> setup, Action<PassContext> render)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(render);
        FramePass pass = BeginPass(name);
        try
        {
            setup(_builder);
            pass.Render = new PlainRenderer(render);
        }
        catch
        {
            Building = null;
            throw;
        }
        Building = null;
    }

    public void AddPass<T>(string name, Func<PassBuilder, T> setup, Action<PassContext, T> render)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(render);
        FramePass pass = BeginPass(name);
        try
        {
            pass.Render = new DataRenderer<T>(setup(_builder), render);
        }
        catch
        {
            Building = null;
            throw;
        }
        Building = null;
    }

    public GraphGroup Group(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        CheckOpen();
        int parent = _groupStack.Count > 0 ? _groupStack[^1] : -1;
        _groups.Add(new GroupInfo(name, _groups.Count, parent));
        _groupStack.Add(_groups.Count - 1);
        return new GraphGroup(this);
    }

    internal void EndGroup()
    {
        if (_groupStack.Count > 0)
            _groupStack.RemoveAt(_groupStack.Count - 1);
    }

    /// <summary>Frees a history or persistent resource now instead of after it goes unused.</summary>
    public void Release(RenderResourceID id, int scope = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _store.Release(id, scope);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _store.Dispose();
    }

    internal void CheckOpen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private FramePass BeginPass(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        CheckOpen();
        if (Building != null)
            throw new InvalidOperationException("A pass cannot be added from inside a setup callback.");
        if (_passCount == _passes.Count)
            _passes.Add(new FramePass());

        FramePass pass = _passes[_passCount++];
        pass.Reset(name, _groupStack.Count > 0 ? _groupStack[^1] : -1);
        Building = pass;
        return pass;
    }

    private TextureHandle TextureHandleOf(int index)
    {
        FrameResource resource = _resources[index];
        return new TextureHandle(resource.Id, index, resource.Version);
    }

    private TextureHandle ImportTexture(object key, RenderTexture texture)
    {
        if (_imported.TryGetValue(key, out int existing))
            return TextureHandleOf(existing);

        int index = NewImport(key, isTexture: true, texture.Name, out FrameResource resource);
        resource.ImportedTexture = texture;
        return new TextureHandle(resource.Id, index, 0);
    }

    private int NewImport(object key, bool isTexture, string? name, out FrameResource resource)
    {
        CheckOpen();
        int index = AddResource(out resource);
        resource.Id = RenderResourceID.Intern(string.IsNullOrEmpty(name) ? $"Import {index}" : name);
        resource.Scope = index;
        resource.IsTexture = isTexture;
        resource.Imported = true;
        _imported.Add(key, index);
        return index;
    }

    private int NewResource(RenderResourceID id, int scope, bool isTexture, Lifetime lifetime, out FrameResource resource)
    {
        CheckOpen();
        if (!id.IsValid)
            throw new ArgumentException("A resource needs a valid id.", nameof(id));

        int index = _resourceCount;
        if (!_created.TryAdd((id, scope), index))
            throw new RenderGraphException($"Resource '{RenderResourceID.ToString(id)}' with scope {scope} was created twice in one frame.", null);

        AddResource(out resource);
        resource.Id = id;
        resource.Scope = scope;
        resource.IsTexture = isTexture;
        resource.Lifetime = lifetime;
        return index;
    }

    private int AddResource(out FrameResource resource)
    {
        if (_resourceCount == _resources.Count)
            _resources.Add(new FrameResource());

        resource = _resources[_resourceCount];
        resource.Reset();
        resource.Writers.Add(-1);
        return _resourceCount++;
    }

    internal void ResetFrame()
    {
        _resourceCount = 0;
        _passCount = 0;
        _created.Clear();
        _imported.Clear();
        _groups.Clear();
        _groupStack.Clear();
        Building = null;
    }
}

/// <summary>Closes a group of passes when disposed.</summary>
public readonly struct GraphGroup : IDisposable
{
    private readonly FrameGraph? _graph;

    internal GraphGroup(FrameGraph graph) => _graph = graph;

    public void Dispose() => _graph?.EndGroup();
}
