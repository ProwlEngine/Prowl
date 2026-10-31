using System;
using System.Collections.Generic;
using System.Threading;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// When in the frame a frame feature runs, in this order: after the renderer's uploads, Deform, LightProbes and BeforeViews,
/// then each world's Shadows, its capture views and its Captures, then the host's views, then AfterViews and Readback.
/// </summary>
public enum FrameSlot : byte
{
    Deform,
    LightProbes,

    /// <summary>After the frame's uploads, probes and deformation, before shadows and every view.</summary>
    BeforeViews,
    Shadows,
    Captures,
    AfterViews,
    Readback,
}

/// <summary>
/// GPU work a world's host runs once a frame, outside any view: simulation, generation, the instance counts of indirect
/// provider items. Its resources are owned by the frame and named by the feature, so it belongs to one set. Exports are
/// readable by every view. Its slot and group are fixed when it is made, Enabled and Order are read at SubmitFrame.
/// </summary>
public abstract class FrameFeature : RenderExtension
{
    private static int s_nextId;
    private ExtensionSet<FrameFeature>? _owner;

    /// <param name="slot">When in the frame it runs.</param>
    /// <param name="order">Its order among frame features of the slot, lowest first.</param>
    /// <param name="group">The group it replaces the built in feature of, see <see cref="FeatureGroup"/>.</param>
    protected FrameFeature(FrameSlot slot, int order = 0, FeatureGroup? group = null)
    {
        Slot = slot;
        Order = order;
        Group = group;
    }

    internal int Id { get; } = Interlocked.Increment(ref s_nextId);

    public FrameSlot Slot { get; }

    /// <summary>
    /// Of features sharing a group in one set the last added runs, and any feature replaces the built in one of its group:
    /// Deform, LightProbes, LocalShadows, CaptureFill or Readback.
    /// </summary>
    public FeatureGroup? Group { get; }

    /// <summary>Order among frame features of a slot, lowest first. Resources they declare decide the rest.</summary>
    public int Order { get; set; }

    /// <summary>Whether the feature runs this frame. Render thread.</summary>
    public virtual bool IsActive() => true;

    /// <summary>Declares what the feature reads, writes and exports, every frame. Render thread.</summary>
    public abstract void Setup(FrameBuilder builder);

    /// <summary>Records the feature's work. Render thread.</summary>
    public abstract void Render(FrameContext context);

    internal RenderWorld? World => _owner?.World;
    internal int OrderSnapshot;

    internal virtual bool IsBuiltIn => false;

    /// <summary>Adds a built in feature's passes and provides its outputs. The world is null for the renderer's.</summary>
    internal virtual void AddBuiltIn(FramePipeline frame, WorldFrame? world) => throw new NotSupportedException();

    internal override void Snapshot()
    {
        base.Snapshot();
        OrderSnapshot = Order;
    }

    internal override void Attach(object set)
    {
        if (_owner != null && _owner != set)
            throw new InvalidOperationException($"{GetType().Name} is already added to another set, and its resources are named once.");
        _owner = (ExtensionSet<FrameFeature>)set;
    }

    internal override void Detach(object set) => _owner = null;
}

/// <summary>
/// What the active frame features export this frame, which every view reads, and the names they provide, which the
/// renderer's passes require. A feature of a world provides for that world, the renderer's for all.
/// </summary>
internal sealed class FrameExports
{
    public readonly Dictionary<RenderResourceID, BufferHandle> Buffers = new();
    public readonly Dictionary<RenderResourceID, TextureHandle> Textures = new();
    private readonly HashSet<(RenderResourceID Name, RenderWorld? World)> _provided = new();
    private readonly Dictionary<(RenderResourceID Name, RenderWorld World), int> _required = new();

    public void Clear()
    {
        Buffers.Clear();
        Textures.Clear();
        _provided.Clear();
        _required.Clear();
    }

    public void Provide(RenderResourceID name, RenderWorld? world) => _provided.Add((name, world));

    /// <summary>Asks for a name a view's draws read, checked by <see cref="Verify"/> once every feature has been added.</summary>
    public void Require(RenderResourceID name, RenderWorld world, int viewId) => _required.TryAdd((name, world), viewId);

    public void Verify()
    {
        foreach (((RenderResourceID name, RenderWorld world), int viewId) in _required)
        {
            if (!_provided.Contains((name, world)) && !_provided.Contains((name, null)))
                throw new InvalidOperationException($"The draws of view {viewId} read '{RenderResourceID.ToString(name)}', which no active frame feature provided.");
        }
    }

    public bool Provided(RenderResourceID name) => _provided.Contains((name, null));
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

    /// <summary>Writes a buffer the host owns, such as one set in the globals, so the draws reading it wait for the write.</summary>
    public BufferHandle Write(DeviceBuffer buffer, BufferAccess usage = BufferAccess.ShaderRead | BufferAccess.ShaderWrite)
        => Graph.Write(FrameGraph.Import(buffer), usage);

    /// <summary>Reads the world's tables, so the feature runs after the frame's uploads, probes and deformation.</summary>
    public void ReadWorld()
    {
        if (_pass.World is { } world)
            _pass.Frame.Buffers.ReadWorld(FrameGraph, Graph, world);
        else
            _pass.Frame.Buffers.ReadAll(FrameGraph, Graph);
    }

    /// <summary>Makes a buffer the feature wrote readable by every view, and by indirect items that name it.</summary>
    public void Export(BufferHandle handle)
    {
        _pass.Frame.Exports.Buffers[handle.Id] = handle;
        _pass.Frame.Exports.Provide(handle.Id, _pass.Feature.World);
    }

    /// <summary>Makes a texture the feature wrote readable by every view.</summary>
    public void Export(TextureHandle handle)
    {
        _pass.Frame.Exports.Textures[handle.Id] = handle;
        _pass.Frame.Exports.Provide(handle.Id, _pass.Feature.World);
    }
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
        if (_pass.Feature.World is not { } world)
            throw new InvalidOperationException($"{_pass.Feature.GetType().Name} binds a world's tables, but belongs to the renderer and not to a world.");
        _pass.Frame.BindWorld(properties, world);
    }
}

/// <summary>A frame feature's pass.</summary>
internal sealed class FrameFeaturePass(FramePipeline frame, FrameFeature feature, WorldFrame? world)
{
    public FramePipeline Frame => frame;
    public FrameFeature Feature => feature;
    public WorldFrame? World => world;

    public string Name { get; } = $"Graphite.Frame {feature.GetType().Name}";

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
