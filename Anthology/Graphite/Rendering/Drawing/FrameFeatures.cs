using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Prowl.Graphite.Rendering;

/// <summary>When in the frame a frame feature runs.</summary>
public enum FrameSlot
{
    /// <summary>After the frame's uploads, probes and deformation, before shadows and every view.</summary>
    BeforeViews,
}

/// <summary>
/// GPU work a world's host runs once a frame, outside any view: simulation, generation, the instance counts of indirect
/// provider items. Its resources are owned by the frame and named by the feature. Exports are readable by every view.
/// </summary>
public abstract class FrameFeature
{
    private static int s_nextId;
    private int _version;

    internal int Id { get; } = Interlocked.Increment(ref s_nextId);
    internal FrameFeatureSet? Owner;

    public abstract FrameSlot Slot { get; }

    /// <summary>Order among frame features, lowest first. Resources they declare decide the rest.</summary>
    public virtual int Order { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Copies settings the render thread reads. Host thread, at SubmitFrame.</summary>
    public virtual void Extract() { }

    /// <summary>Whether the feature runs this frame. Turning it on or off rebuilds the graph. Render thread.</summary>
    public virtual bool IsActive() => true;

    /// <summary>Declares what the feature reads, writes and exports, when the graph is built. Render thread.</summary>
    public abstract void Setup(FrameBuilder builder);

    /// <summary>Records the feature's work. Render thread.</summary>
    public abstract void Render(FrameContext context);

    /// <summary>Rebuilds the graph after a change to what Setup declares. Any thread.</summary>
    public void Invalidate() => Interlocked.Increment(ref _version);

    internal int Version => Volatile.Read(ref _version);

    internal RenderWorld? World => Owner?.World;
    internal bool EnabledSnapshot;
    internal int OrderSnapshot;

    internal void TakeSnapshot()
    {
        EnabledSnapshot = Enabled;
        OrderSnapshot = Order;
    }
}

/// <summary>Frame features added to a world. A feature belongs to one world. Host thread.</summary>
public sealed class FrameFeatureSet : IEnumerable<FrameFeature>
{
    private readonly List<FrameFeature> _features = new();
    private FrameFeature[] _snapshot = [];
    private int _version, _snapshotVersion = -1;

    internal FrameFeatureSet(RenderWorld world) => World = world;

    internal RenderWorld World { get; }

    public int Count => _features.Count;

    public void Add(FrameFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (feature.Owner is { } owner && owner != this)
            throw new InvalidOperationException($"{feature.GetType().Name} is already added to another world, and its resources are named once.");
        if (_features.Contains(feature))
            return;
        feature.Owner = this;
        _features.Add(feature);
        _version++;
    }

    public bool Remove(FrameFeature feature)
    {
        if (!_features.Remove(feature))
            return false;
        feature.Owner = null;
        _version++;
        return true;
    }

    public bool Contains(FrameFeature feature) => _features.Contains(feature);

    public IEnumerator<FrameFeature> GetEnumerator() => _features.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal FrameFeature[] Snapshot()
    {
        if (_snapshotVersion != _version)
        {
            _snapshot = [.. _features];
            _snapshotVersion = _version;
        }
        return _snapshot;
    }
}

/// <summary>What the active frame features export, which every view of the graph reads.</summary>
internal sealed class FrameExports
{
    public readonly List<RenderResourceID> Buffers = new();
    public readonly List<RenderResourceID> Textures = new();

    public void Clear()
    {
        Buffers.Clear();
        Textures.Clear();
    }

    public void Read(RenderContextBuilder builder)
    {
        foreach (RenderResourceID id in Buffers)
            builder.DeclareInputBuffer(id);
        foreach (RenderResourceID id in Textures)
            builder.DeclareInputTexture(id);
    }
}

/// <summary>Declares a frame feature's resources, in the root pass the renderer runs it in.</summary>
public sealed class FrameBuilder
{
    private readonly FrameFeaturePass _pass;

    internal FrameBuilder(FrameFeaturePass pass, RenderContextBuilder graph)
    {
        _pass = pass;
        Graph = graph;
    }

    /// <summary>The underlying graph builder, for what the methods here do not cover.</summary>
    public RenderContextBuilder Graph { get; }

    /// <summary>Declares a buffer the feature writes, kept between frames when persistent.</summary>
    public BufferHandle Buffer(RenderResourceID id, GraphBufferDesc desc, bool persistent = false)
        => Graph.DeclareOutputBuffer(id, desc, persistent: persistent);

    /// <summary>Declares a texture the feature writes, kept between frames when persistent.</summary>
    public TextureHandle Texture(RenderResourceID id, GraphTextureDesc desc, bool persistent = false, TextureState usage = TextureState.Storage)
        => Graph.DeclareOutputTexture(id, desc, usage: usage, persistent: persistent);

    /// <summary>Declares a buffer with a ring of the given number of earlier frames beside the current one.</summary>
    public BufferHandle History(RenderResourceID id, GraphBufferDesc desc, int frames)
        => Graph.DeclareOutputBuffer(id, desc, history: frames);

    /// <summary>Declares a texture with a ring of the given number of earlier frames beside the current one.</summary>
    public TextureHandle History(RenderResourceID id, GraphTextureDesc desc, int frames, TextureState usage = TextureState.Storage)
        => Graph.DeclareOutputTexture(id, desc, history: frames, usage: usage);

    /// <summary>Writes a buffer another pass declared.</summary>
    public BufferHandle Write(BufferHandle handle, BufferAccess usage = BufferAccess.ShaderRead | BufferAccess.ShaderWrite)
        => Graph.DeclareWriteBuffer(handle.Id, usage);

    /// <summary>Writes a texture another pass declared.</summary>
    public TextureHandle Write(TextureHandle handle, TextureState usage = TextureState.Storage)
        => Graph.DeclareWriteTexture(handle.Id, usage);

    /// <summary>Reads the world's tables, so the feature runs after the frame's uploads, probes and deformation.</summary>
    public void ReadWorld() => _pass.Frame.Buffers.Read(Graph);

    /// <summary>Makes a buffer the feature wrote readable by every view, and by indirect items that name it.</summary>
    public void Export(BufferHandle handle) => _pass.Frame.Exports.Buffers.Add(handle.Id);

    /// <summary>Makes a texture the feature wrote readable by every view.</summary>
    public void Export(TextureHandle handle) => _pass.Frame.Exports.Textures.Add(handle.Id);
}

/// <summary>What a frame feature records with.</summary>
public sealed class FrameContext
{
    private readonly FrameFeaturePass _pass;

    internal FrameContext(FrameFeaturePass pass, RenderContext graph, CommandBuffer command)
    {
        _pass = pass;
        Graph = graph;
        Command = command;
    }

    /// <summary>The underlying graph, for resources the feature declared through <see cref="FrameBuilder.Graph"/>.</summary>
    public RenderContext Graph { get; }

    public CommandBuffer Command { get; }

    /// <summary>The frame's time as the host gave it.</summary>
    public FrameTime Time => _pass.Frame.CurrentTime;

    /// <summary>A buffer the feature declared.</summary>
    public DeviceBuffer Get(BufferHandle buffer) => Graph.GetRenderBuffer(buffer);

    /// <summary>A texture the feature declared.</summary>
    public RenderTexture Get(TextureHandle texture) => Graph.GetRenderTexture(texture);

    /// <summary>A history buffer as it was some frames ago, or null before it holds one.</summary>
    public DeviceBuffer? History(BufferHandle buffer, int framesAgo = 1)
        => Graph.IsHistoryValid(buffer) ? Graph.GetRenderBuffer(buffer, framesAgo) : null;

    /// <summary>A history texture as it was some frames ago, or null before it holds one.</summary>
    public RenderTexture? History(TextureHandle texture, int framesAgo = 1)
        => Graph.IsHistoryValid(texture) ? Graph.GetRenderTexture(texture, framesAgo) : null;

    /// <summary>Sets the world's tables in a property set, for compute shaders that read geometry, objects or lights.</summary>
    public void BindWorld(PropertySet properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        _pass.Frame.BindWorld(properties, _pass.Feature.World!);
    }
}

/// <summary>A frame feature's root pass.</summary>
internal sealed class FrameFeaturePass(FramePipeline frame, FrameFeature feature) : IExecutionPass
{
    public FramePipeline Frame => frame;
    public FrameFeature Feature => feature;
    public int BuiltVersion;

    public string Name => $"Graphite.Frame {feature.GetType().Name}";

    public void Setup(RenderContextBuilder builder)
    {
        BuiltVersion = feature.Version;
        feature.Setup(new FrameBuilder(this, builder));
    }

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        try
        {
            feature.Render(new FrameContext(this, context, cmd));
        }
        catch (Exception e)
        {
            frame.Report(new RendererError("frame.feature", $"{feature.GetType().Name}.Render threw: {e}", RendererErrorSeverity.Error, feature) { Key = $"frame.feature.{feature.GetType().FullName}" });
        }
    }
}

/// <summary>Copies the instance counts frame features wrote into every view's commands, so indirect items draw what this frame made.</summary>
internal sealed class ProviderCountsPass : IExecutionPass
{
    private readonly Dictionary<RenderResourceID, BufferHandle> _handles = new();

    /// <summary>The copies this frame, as each source, its byte offset, the commands buffer it lands in and the byte offset there.</summary>
    public readonly List<(RenderResourceID Source, uint Offset, PersistentBuffer Target, uint Destination)> Copies = new();

    /// <summary>The buffers the copies touch, as of the last graph build and as of this frame.</summary>
    public RenderResourceID[] Declared = [];
    public readonly List<RenderResourceID> Planned = new();

    public string Name => "Graphite.ProviderCounts";

    public bool IsEnabled() => Copies.Count > 0;

    public void Plan()
    {
        Planned.Clear();
        foreach ((RenderResourceID source, _, PersistentBuffer target, _) in Copies)
        {
            if (!Planned.Contains(source))
                Planned.Add(source);
            if (!Planned.Contains(target.Id))
                Planned.Add(target.Id);
        }
    }

    public bool Changed() => !CollectionsMarshal.AsSpan(Planned).SequenceEqual(Declared);

    public void Setup(RenderContextBuilder builder)
    {
        Declared = [.. Planned];
        _handles.Clear();
        foreach ((RenderResourceID source, _, PersistentBuffer target, _) in Copies)
        {
            if (!_handles.ContainsKey(source))
                _handles[source] = builder.DeclareInputBuffer(source, BufferAccess.TransferRead);
            if (!_handles.ContainsKey(target.Id))
                _handles[target.Id] = builder.DeclareWriteBuffer(target.Id, BufferAccess.TransferWrite);
        }
    }

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        foreach ((RenderResourceID source, uint offset, PersistentBuffer target, uint destination) in Copies)
            cmd.CopyBuffer(context.GetRenderBuffer(_handles[source]), offset, context.GetRenderBuffer(_handles[target.Id]), destination, 4);
    }
}
