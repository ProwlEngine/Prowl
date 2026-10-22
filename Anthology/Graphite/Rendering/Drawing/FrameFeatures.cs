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

    internal int Id { get; } = Interlocked.Increment(ref s_nextId);
    internal FrameFeatureSet? Owner;

    public abstract FrameSlot Slot { get; }

    /// <summary>Order among frame features, lowest first. Resources they declare decide the rest.</summary>
    public virtual int Order { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Copies settings the render thread reads. Host thread, at SubmitFrame.</summary>
    public virtual void Extract() { }

    /// <summary>Whether the feature runs this frame. Render thread.</summary>
    public virtual bool IsActive() => true;

    /// <summary>Declares what the feature reads, writes and exports, every frame. Render thread.</summary>
    public abstract void Setup(FrameBuilder builder);

    /// <summary>Records the feature's work. Render thread.</summary>
    public abstract void Render(FrameContext context);

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

/// <summary>What the active frame features export this frame, which every view reads.</summary>
internal sealed class FrameExports
{
    public readonly Dictionary<RenderResourceID, BufferHandle> Buffers = new();
    public readonly Dictionary<RenderResourceID, TextureHandle> Textures = new();

    public void Clear()
    {
        Buffers.Clear();
        Textures.Clear();
    }
}

/// <summary>Declares a frame feature's resources in the pass the renderer runs it in.</summary>
public sealed class FrameBuilder
{
    private readonly FrameFeaturePass _pass;

    internal FrameBuilder(FrameFeaturePass pass, PassBuilder graph)
    {
        _pass = pass;
        Graph = graph;
    }

    /// <summary>The underlying pass builder, for what the methods here do not cover.</summary>
    public PassBuilder Graph { get; }

    private FrameGraph FrameGraph => _pass.Frame.Graph;

    /// <summary>Creates a buffer the feature writes, kept between frames when persistent.</summary>
    public BufferHandle Buffer(RenderResourceID id, GraphBufferDesc desc, bool persistent = false, BufferAccess usage = BufferAccess.ShaderRead | BufferAccess.ShaderWrite)
        => Graph.Write(FrameGraph.Create(id, desc, persistent ? Lifetime.Persistent : Lifetime.Transient), usage);

    /// <summary>Creates a texture the feature writes, kept between frames when persistent.</summary>
    public TextureHandle Texture(RenderResourceID id, GraphTextureDesc desc, bool persistent = false, TextureState usage = TextureState.Storage)
        => Graph.Write(FrameGraph.Create(id, desc, persistent ? Lifetime.Persistent : Lifetime.Transient), usage);

    /// <summary>Creates a buffer with a ring of the given number of earlier frames beside the current one.</summary>
    public BufferHandle History(RenderResourceID id, GraphBufferDesc desc, int frames)
        => Graph.Write(FrameGraph.Create(id, desc, Lifetime.History(frames)));

    /// <summary>Creates a texture with a ring of the given number of earlier frames beside the current one.</summary>
    public TextureHandle History(RenderResourceID id, GraphTextureDesc desc, int frames, TextureState usage = TextureState.Storage)
        => Graph.Write(FrameGraph.Create(id, desc, Lifetime.History(frames)), usage);

    /// <summary>Writes a buffer another pass declared.</summary>
    public BufferHandle Write(BufferHandle handle, BufferAccess usage = BufferAccess.ShaderRead | BufferAccess.ShaderWrite)
        => Graph.Write(handle, usage);

    /// <summary>Writes a texture another pass declared.</summary>
    public TextureHandle Write(TextureHandle handle, TextureState usage = TextureState.Storage)
        => Graph.Write(handle, usage);

    /// <summary>Reads the world's tables, so the feature runs after the frame's uploads, probes and deformation.</summary>
    public void ReadWorld() => _pass.Frame.Buffers.ReadWorld(FrameGraph, Graph, _pass.World);

    /// <summary>Makes a buffer the feature wrote readable by every view, and by indirect items that name it.</summary>
    public void Export(BufferHandle handle) => _pass.Frame.Exports.Buffers[handle.Id] = handle;

    /// <summary>Makes a texture the feature wrote readable by every view.</summary>
    public void Export(TextureHandle handle) => _pass.Frame.Exports.Textures[handle.Id] = handle;
}

/// <summary>What a frame feature records with.</summary>
public sealed class FrameContext
{
    private readonly FrameFeaturePass _pass;

    internal FrameContext(FrameFeaturePass pass, PassContext graph)
    {
        _pass = pass;
        Graph = graph;
    }

    /// <summary>The underlying pass context, for resources the feature declared through <see cref="FrameBuilder.Graph"/>.</summary>
    public PassContext Graph { get; }

    public CommandBuffer Command => Graph.Command;

    /// <summary>The frame's time as the host gave it.</summary>
    public FrameTime Time => _pass.Frame.CurrentTime;

    /// <summary>A buffer the feature declared.</summary>
    public DeviceBuffer Get(BufferHandle buffer) => Graph.Get(buffer);

    /// <summary>A texture the feature declared.</summary>
    public RenderTexture Get(TextureHandle texture) => Graph.Get(texture);

    /// <summary>A history buffer as it was some frames ago, or null before it holds one.</summary>
    public DeviceBuffer? History(BufferHandle buffer, int framesAgo = 1) => Graph.History(buffer, framesAgo);

    /// <summary>A history texture as it was some frames ago, or null before it holds one.</summary>
    public RenderTexture? History(TextureHandle texture, int framesAgo = 1) => Graph.History(texture, framesAgo);

    /// <summary>Sets the world's tables in a property set, for compute shaders that read geometry, objects or lights.</summary>
    public void BindWorld(PropertySet properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        _pass.Frame.BindWorld(properties, _pass.Feature.World!);
    }
}

/// <summary>A frame feature's pass.</summary>
internal sealed class FrameFeaturePass(FramePipeline frame, FrameFeature feature, WorldFrame world)
{
    public FramePipeline Frame => frame;
    public FrameFeature Feature => feature;
    public WorldFrame World => world;

    public string Name => $"Graphite.Frame {feature.GetType().Name}";

    public void Add()
    {
        frame.Graph.AddPass(Name, builder =>
        {
            feature.Setup(new FrameBuilder(this, builder));
            return this;
        }, static (context, pass) => pass.Render(context));
    }

    private void Render(PassContext context)
    {
        try
        {
            feature.Render(new FrameContext(this, context));
        }
        catch (Exception e)
        {
            frame.Report(new RendererError("frame.feature", $"{feature.GetType().Name}.Render threw: {e}", RendererErrorSeverity.Error, feature) { Key = $"frame.feature.{feature.GetType().FullName}" });
        }
    }
}

/// <summary>Copies the instance counts frame features wrote into every view's commands, so indirect items draw what this frame made.</summary>
internal sealed class ProviderCountsPass(FramePipeline frame)
{
    private readonly Dictionary<RenderResourceID, BufferHandle> _sources = new();
    private readonly Dictionary<DeviceBuffer, BufferHandle> _targets = new();

    /// <summary>The copies this frame, as each source, its byte offset, the view whose commands it lands in and the byte offset there.</summary>
    public readonly List<(RenderResourceID Source, uint Offset, CameraFrame Target, uint Destination)> Copies = new();

    public void Add(FrameGraph graph)
    {
        if (Copies.Count == 0)
            return;
        graph.AddPass("Graphite.ProviderCounts", builder =>
        {
            _sources.Clear();
            _targets.Clear();
            foreach ((RenderResourceID source, _, CameraFrame target, _) in Copies)
            {
                if (!_sources.ContainsKey(source))
                {
                    if (!frame.Exports.Buffers.TryGetValue(source, out BufferHandle exported))
                        throw new InvalidOperationException($"An indirect item reads the count buffer '{RenderResourceID.ToString(source)}', which no active frame feature exported.");
                    _sources[source] = builder.Read(exported, BufferAccess.TransferRead);
                }
                if (!_targets.ContainsKey(target.Commands.Buffer))
                    _targets[target.Commands.Buffer] = builder.Write(graph.Import(target.Commands.Buffer), BufferAccess.TransferWrite);
            }
        }, context =>
        {
            foreach ((RenderResourceID source, uint offset, CameraFrame target, uint destination) in Copies)
                context.Command.CopyBuffer(context.Get(_sources[source]), offset, context.Get(_targets[target.Commands.Buffer]), destination, 4);
        });
    }
}
