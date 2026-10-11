using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite.RenderGraph;
using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// The Level 0 programs of draw programs: the variant compiled with the view's global keywords, put through the
/// material rewrite, with its pass state, the material's state override and the winding of mirrored objects.
/// Programs that fail to compile are reported once and draw nothing.
/// </summary>
internal sealed class ProgramCache(GraphicsDevice device, IShaderCompiler compiler, GlobalRegistry globals, DrawPrograms programs, Action<string, string> report) : IDisposable
{
    private readonly Dictionary<(ushort Program, bool Mirrored, ulong Globals), GraphicsProgram?> _programs = new();

    public static readonly ResourceLayoutDescription FrameLayout = GraphiteBindings.FrameTable(RendererTextures.Textures2D, RendererTextures.OtherTextures);
    public static readonly ResourceLayoutDescription ViewLayout = GraphiteBindings.ViewTable(RendererTextures.Samplers);

    public GraphicsProgram? Get(ushort id, bool mirrored, GlobalProperties keywords)
    {
        DrawProgram program = programs[id];
        IReadOnlyList<VariantSpace> axes = program.Shader.GlobalAxes;
        ulong key = 0, stride = 1;
        Span<int> values = stackalloc int[axes.Count];
        for (int i = 0; i < axes.Count; i++)
        {
            string? value = keywords.GetKeyword(axes[i].Name);
            int index = 0;
            for (int v = 0; v < axes[i].Values.Count; v++)
            {
                if (axes[i].Values[v] == value)
                    index = v;
            }
            values[i] = index;
            key += (ulong)index * stride;
            stride *= (ulong)axes[i].Values.Count;
        }

        if (_programs.TryGetValue((id, mirrored, key), out GraphicsProgram? cached))
            return cached;

        Keyword[] combo = new Keyword[program.Keywords.Length + axes.Count];
        program.Keywords.CopyTo(combo, 0);
        for (int i = 0; i < axes.Count; i++)
            combo[program.Keywords.Length + i] = new Keyword(axes[i].Name, axes[i].Values[values[i]]);

        GraphicsProgram? built = null;
        try
        {
            built = Build(program, combo, mirrored);
        }
        catch (Exception e)
        {
            report($"program.{program.Shader.Name}.{program.PassIndex}.{key}", $"{program.Shader.Name}: {e.Message}");
        }
        _programs[(id, mirrored, key)] = built;
        return built;
    }

    public void Dispose()
    {
        foreach (GraphicsProgram? program in _programs.Values)
            program?.Dispose();
        _programs.Clear();
    }

    private GraphicsProgram Build(DrawProgram program, Keyword[] combo, bool mirrored)
    {
        ShaderPass pass = program.Shader.Definition.Passes![program.PassIndex];
        ShaderDescription description = compiler.Compile(pass, combo, device.BackendType);
        description = MaterialRewrite.Rewrite(program.Shader.Name, description, program.Shader.Schema, compiler.GetGlobals(pass), globals, FrameLayout, ViewLayout);

        PassState state = program.StateOverride is { } over ? over.Apply(pass.State) : pass.State;
        BlendStateDescription blend = BlendStateDescription.SingleOverrideBlend;
        if (ShaderDefinition.PassHasTag(pass, PassType.Tag, PassType.Prepass.Name))
        {
            BlendAttachmentDescription attachment = blend.AttachmentStates[0];
            attachment.ColorWriteMask = ColorWriteMask.None;
            blend.AttachmentStates = [attachment];
        }

        description.BlendState = state.ToBlendState(blend);
        description.DepthStencilState = state.ToDepthStencilState(DepthStencilStateDescription.DepthOnlyGreaterEqual);
        RasterizerStateDescription raster = state.ToRasterizerState(RasterizerStateDescription.Default);
        if (mirrored)
            raster.FrontFace = raster.FrontFace == FrontFace.Clockwise ? FrontFace.CounterClockwise : FrontFace.Clockwise;
        description.RasterizerState = raster;

        GraphicsProgram built = device.ResourceFactory.CreateGraphicsProgram(description);
        built.Name = $"{program.Shader.Name} {pass.Name}";
        return built;
    }
}

/// <summary>
/// A device local buffer the CPU fills every frame through the frame's scatter, like instance lists and indirect
/// commands. It grows by doubling, and a grown buffer changes the frame's buffer set.
/// </summary>
internal sealed class GpuArray(GraphicsDevice device, BufferUsage usage, string name) : IDisposable
{
    public DeviceBuffer Buffer { get; private set; } = Create(device, usage, name, 256);

    public void Write<T>(Uploader uploader, ReadOnlySpan<T> data) where T : unmanaged
    {
        uint bytes = (uint)(data.Length * Marshal.SizeOf<T>());
        if (bytes > Buffer.SizeInBytes)
        {
            uint size = Buffer.SizeInBytes;
            Buffer.Dispose();
            while (size < bytes)
                size *= 2;
            Buffer = Create(device, usage, name, size);
        }
        if (bytes > 0)
            uploader.Write(Buffer, 0, MemoryMarshal.Cast<T, uint>(data));
    }

    public void Dispose() => Buffer.Dispose();

    private static DeviceBuffer Create(GraphicsDevice device, BufferUsage usage, string name, uint size)
    {
        DeviceBuffer buffer = device.ResourceFactory.CreateBuffer(new BufferDescription(size, usage | BufferUsage.StructuredBufferReadWrite));
        buffer.Name = name;
        return buffer;
    }
}

/// <summary>The view a camera pipeline draws for: its target and size.</summary>
internal readonly record struct CameraView(Framebuffer? Target, uint PixelWidth, uint PixelHeight, int ViewId) : IRenderView;

/// <summary>The FrameStart pass: the frame's single scatter into every persistent buffer, with each declared as written.</summary>
internal sealed class UploadPass(Uploader uploader) : IPass
{
    public DeviceBuffer[] Buffers = [];

    public string Name => "Graphite.FrameStart";

    public void Setup(RenderContextBuilder builder)
    {
        for (int i = 0; i < Buffers.Length; i++)
            builder.DeclareImportedBuffer(FramePipeline.BufferId(i), Buffers[i], BufferAccess.ShaderWrite);
    }

    public void Render(RenderContext context, CommandBuffer cmd) => uploader.Record(cmd, context.Task);
}

/// <summary>Everything a camera pipeline draws in one frame, prepared before dispatch.</summary>
internal sealed class CameraFrame : IDisposable
{
    public readonly DrawList Prepass = new();
    public readonly DrawList Forward = new();
    public readonly RenderPipeline Pipeline = new();
    public readonly ScenePass Scene;
    public readonly PresentPass Present;
    public readonly TableRing ViewTable;
    public readonly GpuArray Instances;
    public readonly GpuArray Records;
    public readonly GpuArray Commands;
    public readonly GpuArray Views;
    public ViewDesc Desc;
    public WorldFrame World = null!;
    public int LastFrame;

    private IndirectDrawIndexedArguments[] _commands = new IndirectDrawIndexedArguments[64];
    private DrawRecord[] _records = new DrawRecord[64];
    private uint[] _instances = new uint[256];

    public CameraFrame(GraphicsDevice device, FramePipeline frame, int viewId)
    {
        Scene = new ScenePass(frame, this);
        Present = new PresentPass(frame);
        Pipeline.Name = $"Graphite.Camera {viewId}";
        Pipeline.SetPasses([Scene, Present]);
        ViewTable = new TableRing(device, ProgramCache.ViewLayout, $"View table {viewId}");
        Instances = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"Instances {viewId}");
        Records = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"Draw records {viewId}");
        Commands = new GpuArray(device, BufferUsage.IndirectBuffer, $"Indirect commands {viewId}");
        Views = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"View blocks {viewId}");
    }

    public IEnumerable<DeviceBuffer> Buffers => [Instances.Buffer, Records.Buffer, Commands.Buffer, Views.Buffer];

    /// <summary>Queues the frame's commands, draw records, instances and view block. Prepass commands come first, then forward ones.</summary>
    public void Upload(Uploader uploader, in ViewBlock block)
    {
        int commands = Prepass.CommandCount + Forward.CommandCount;
        int instances = Prepass.InstanceCount + Forward.InstanceCount;
        if (_commands.Length < commands)
        {
            Array.Resize(ref _commands, commands * 2);
            Array.Resize(ref _records, commands * 2);
        }
        if (_instances.Length < instances)
            Array.Resize(ref _instances, instances * 2);

        Array.Copy(Prepass.Commands, _commands, Prepass.CommandCount);
        Array.Copy(Prepass.Records, _records, Prepass.CommandCount);
        Array.Copy(Prepass.Instances, _instances, Prepass.InstanceCount);
        for (int i = 0; i < Forward.CommandCount; i++)
        {
            IndirectDrawIndexedArguments command = Forward.Commands[i];
            command.FirstInstance += (uint)Prepass.InstanceCount;
            _commands[Prepass.CommandCount + i] = command;
            _records[Prepass.CommandCount + i] = Forward.Records[i];
        }
        Array.Copy(Forward.Instances, 0, _instances, Prepass.InstanceCount, Forward.InstanceCount);

        Commands.Write<IndirectDrawIndexedArguments>(uploader, _commands.AsSpan(0, commands));
        Records.Write<DrawRecord>(uploader, _records.AsSpan(0, commands));
        Instances.Write<uint>(uploader, _instances.AsSpan(0, instances));
        Views.Write<ViewBlock>(uploader, [block]);
    }

    public void Dispose()
    {
        Pipeline.Dispose();
        ViewTable.Dispose();
        Instances.Dispose();
        Records.Dispose();
        Commands.Dispose();
        Views.Dispose();
    }
}

/// <summary>Draws a view's prepass and opaque lists into its HDR scene target.</summary>
internal sealed class ScenePass(FramePipeline frame, CameraFrame camera) : IPass
{
    public static readonly RenderResourceID SceneId = RenderResourceID.Intern("Graphite.Scene");

    private static readonly GraphTextureDesc s_scene = GraphTextureDesc.ViewSized(PixelFormat.R16_G16_B16_A16_Float, depth: true) with { DepthFormat = PixelFormat.D32_Float_S8_UInt };

    private readonly PropertySet _properties = new();
    private TextureHandle _target;

    public string Name => "Graphite.Scene";

    public void Setup(RenderContextBuilder builder)
    {
        _target = builder.DeclareOutputTexture(SceneId, s_scene);
        DeviceBuffer[] buffers = frame.Buffers;
        for (int i = 0; i < buffers.Length; i++)
            builder.DeclareImportedBuffer(FramePipeline.BufferId(i), buffers[i], BufferAccess.AllReads);
    }

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        uint ring = context.Task.RingSlot;
        GlobalRegistry registry = frame.Globals;
        camera.ViewTable.Set("Graphite_Globals", 0, new DeviceBufferRange(registry.Buffer, (uint)camera.World.GlobalRegion * registry.RegionWords * 4, registry.RegionWords * 4));
        camera.ViewTable.Set("Graphite_Views", 0, camera.Views.Buffer);
        DescriptorTable viewTable = camera.ViewTable.Get(ring);
        DescriptorTable frameTable = camera.World.FrameTable.Get(ring);

        Float4 clear = camera.Desc.ClearColor;
        cmd.SetFramebuffer(context.GetRenderTexture(_target).Framebuffer, new TargetLoadStoreOps(AttachmentOps.Clear(new Color(clear.X, clear.Y, clear.Z, clear.W)), AttachmentOps.Clear(0f)));
        cmd.SetFullViewport();
        _properties.SetBuffer(GraphiteBindings.InstancesName, camera.Instances.Buffer);
        _properties.SetBuffer(GraphiteBindings.DrawRecordsName, camera.Records.Buffer);

        Draw(cmd, camera.Prepass, 0, frameTable, viewTable);
        Draw(cmd, camera.Forward, camera.Prepass.CommandCount, frameTable, viewTable);
    }

    private void Draw(CommandBuffer cmd, DrawList list, int commandBase, DescriptorTable frameTable, DescriptorTable viewTable)
    {
        uint stride = (uint)Marshal.SizeOf<IndirectDrawIndexedArguments>();
        foreach (DrawCall call in list.Calls)
        {
            GraphicsProgram? program = frame.Programs.Get(call.Program, call.Mirrored, camera.World.World.Globals);
            if (program == null)
                continue;

            Shader shader = frame.DrawPrograms[call.Program].Shader;
            if (!frame.Materials.Tables.TryGetValue(shader, out MaterialTable? table))
                continue;

            GeometryPage page = frame.Geometry.Pages[call.Page];
            cmd.SetShader(program);
            cmd.SetVertexSource(frame.VertexSource(call.Page, call.Topology));
            cmd.SetDescriptorTable(GraphiteBindings.FrameSet, frameTable);
            cmd.SetDescriptorTable(GraphiteBindings.ViewSet, viewTable);
            _properties.SetBuffer(GraphiteBindings.MaterialsName, table.Records.Buffer);
            _properties.SetBuffer(GraphiteBindings.VerticesName, page.Vertices);
            cmd.SetProperties(_properties);
            cmd.SetPushConstants(new FramePipeline.Push { DrawRecordBase = (uint)(commandBase + call.FirstCommand) });
            cmd.DrawIndexedIndirect(camera.Commands.Buffer, (uint)(commandBase + call.FirstCommand) * stride, (uint)call.CommandCount, stride);
        }
    }
}

/// <summary>Copies the scene colour to the view's target.</summary>
internal sealed class PresentPass(FramePipeline frame) : IPass
{
    private readonly PropertySet _properties = new();
    private TextureHandle _scene;
    private TextureHandle _target;

    public string Name => "Graphite.Present";

    public void Setup(RenderContextBuilder builder)
    {
        _scene = builder.DeclareInputTexture(ScenePass.SceneId);
        _target = builder.DeclareViewTarget();
    }

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        cmd.SetFramebuffer(context.GetRenderTexture(_target).Framebuffer, new TargetLoadStoreOps(new AttachmentOps(LoadAction.DontCare, StoreAction.Store), AttachmentOps.Loaded));
        cmd.SetFullViewport();
        cmd.SetShader(frame.PresentProgram);
        cmd.SetVertexSource(VertexSource.None);
        _properties.SetTexture("Graphite_Source", context.GetRenderTexture(_scene).ColorTextures[0], frame.LinearSampler);
        cmd.SetProperties(_properties);
        cmd.Draw(3);
    }
}

/// <summary>A world as the frame pipeline draws it: its buckets, its globals region and its frame table.</summary>
internal sealed class WorldFrame(RenderWorld world, BucketSet buckets, TableRing frameTable, int globalRegion) : IDisposable
{
    public RenderWorld World { get; } = world;
    public BucketSet Buckets { get; } = buckets;
    public TableRing FrameTable { get; } = frameTable;
    public int GlobalRegion { get; } = globalRegion;

    public void Dispose() => FrameTable.Dispose();
}

/// <summary>
/// The frame's pipelines: the FrameStart pipeline uploading every change, then one camera pipeline per view, all in
/// one execution. Graphs rebuild only when the set of persistent buffers changes.
/// </summary>
internal sealed class FramePipeline : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Push
    {
        public uint DrawRecordBase;
        public uint InstanceBase;
        public uint ViewBase;
        public uint Flags;
    }

    private static readonly List<RenderResourceID> s_bufferIds = new();

    private readonly GraphicsDevice _device;
    private readonly RenderPipeline _frameStart = new();
    private readonly UploadPass _upload;
    private readonly Dictionary<int, CameraFrame> _cameras = new();
    private readonly Dictionary<(int Page, PrimitiveTopology Topology), VertexSource> _vertexSources = new();
    private readonly List<GraphDispatch> _dispatches = new();
    private readonly List<DeviceBuffer> _buffers = new();
    private int _frame;

    private readonly RendererTextures _textures;

    private readonly Uploader _uploader;

    public FramePipeline(GraphicsDevice device, IShaderCompiler compiler, Uploader uploader, GeometryPool geometry, MaterialSystem materials, GlobalRegistry globals, DrawPrograms programs, RendererTextures textures, Action<string, string> report)
    {
        _device = device;
        _textures = textures;
        _uploader = uploader;
        Geometry = geometry;
        Materials = materials;
        Globals = globals;
        DrawPrograms = programs;
        Programs = new ProgramCache(device, compiler, globals, programs, report);
        _upload = new UploadPass(uploader);
        _frameStart.Name = "Graphite.FrameStart";
        _frameStart.SetPasses([_upload]);

        ShaderPass present = new() { State = new PassState(), Source = ShaderLibrary.Get(ShaderLibrary.Prefix + "Internal/Present.glsl")!, SourceName = "Graphite/Internal/Present.glsl" };
        ShaderDescription description = compiler.Compile(present, [], device.BackendType);
        description.BlendState = BlendStateDescription.SingleOverrideBlend;
        description.DepthStencilState = DepthStencilStateDescription.Disabled;
        description.RasterizerState = RasterizerStateDescription.CullNone;
        PresentProgram = device.ResourceFactory.CreateGraphicsProgram(description);
        LinearSampler = device.ResourceFactory.CreateSampler(SamplerDescription.Linear);
    }

    public GeometryPool Geometry { get; }
    public MaterialSystem Materials { get; }
    public GlobalRegistry Globals { get; }
    public DrawPrograms DrawPrograms { get; }
    public ProgramCache Programs { get; }
    public GraphicsProgram PresentProgram { get; }
    public Sampler LinearSampler { get; }

    /// <summary>Every persistent buffer the frame writes and its cameras read, in a stable order.</summary>
    public DeviceBuffer[] Buffers { get; private set; } = [];

    public static RenderResourceID BufferId(int index)
    {
        lock (s_bufferIds)
        {
            while (s_bufferIds.Count <= index)
                s_bufferIds.Add(RenderResourceID.Intern("Graphite.Buffer" + s_bufferIds.Count));
            return s_bufferIds[index];
        }
    }

    public VertexSource VertexSource(int page, PrimitiveTopology topology)
    {
        DeviceBuffer indices = Geometry.Pages[page].Indices;
        if (!_vertexSources.TryGetValue((page, topology), out VertexSource? source))
        {
            source = new VertexSource(topology).SetIndexBuffer(indices, IndexFormat.UInt32, indices.SizeInBytes / 4);
            _vertexSources[(page, topology)] = source;
        }
        return source;
    }

    /// <summary>Culls and compacts every view, then dispatches the frame. Returns the execution. Render thread.</summary>
    public ExecutionTask Run(ReadOnlySpan<ViewDesc> views, IReadOnlyDictionary<RenderWorld, WorldFrame> worlds, IEnumerable<WorldFrame> allWorlds)
    {
        _frame++;
        _dispatches.Clear();
        _dispatches.Add(new GraphDispatch(_frameStart, [new CameraView(null, 1, 1, -1)]));
        foreach (ViewDesc view in views)
        {
            if (!worlds.TryGetValue(view.World, out WorldFrame? world))
                continue;

            if (!_cameras.TryGetValue(view.ViewId, out CameraFrame? camera))
            {
                camera = new CameraFrame(_device, this, view.ViewId);
                _cameras[view.ViewId] = camera;
                _textures.TrackView(camera.ViewTable);
            }
            else if (camera.LastFrame == _frame)
            {
                throw new InvalidOperationException($"Two views share the ViewId {view.ViewId} in one frame.");
            }

            camera.LastFrame = _frame;
            camera.Desc = view;
            camera.World = world;
            Prepare(camera, view, world);
            _dispatches.Add(new GraphDispatch(camera.Pipeline, [new CameraView(view.Target, view.Target.Width, view.Target.Height, view.ViewId)]));
        }

        CollectBuffers(allWorlds);
        ExecutionTask task = _device.DispatchGraph(CollectionsMarshal.AsSpan(_dispatches));
        ReleaseStaleCameras();
        return task;
    }

    private void Prepare(CameraFrame camera, in ViewDesc view, WorldFrame world)
    {
        ViewLayer layer = view.Layers[0];
        Float4x4 viewProjection = layer.Projection * layer.View;
        CullFrustum frustum = new(viewProjection, layer.Position);
        world.Buckets.Build(0, frustum, view.CullingMask, camera.Prepass);
        world.Buckets.Build(1, frustum, view.CullingMask, camera.Forward);
        camera.Upload(_uploader, ViewBlock.Create(layer.View, layer.Projection, layer.Position, view.Target.Width, view.Target.Height, view.Near, view.FarDistance));
    }

    private void CollectBuffers(IEnumerable<WorldFrame> worlds)
    {
        _buffers.Clear();
        _buffers.Add(Geometry.Chunks.Buffer);
        _buffers.Add(Geometry.Meshes.Buffer);
        foreach (GeometryPage page in Geometry.Pages)
        {
            _buffers.Add(page.Vertices);
            _buffers.Add(page.Indices);
        }
        foreach (MaterialTable table in Materials.Tables.Values)
            _buffers.Add(table.Records.Buffer);
        _buffers.Add(Globals.Buffer);
        foreach (WorldFrame world in worlds)
        {
            RenderWorld.RenderSide render = world.World.Render;
            _buffers.AddRange([render.Transforms.Buffer, render.PreviousTransforms.Buffer, render.Bounds.Buffer, render.Attributes.Buffer, render.MaterialSlots.Buffer]);
            world.FrameTable.Set("Graphite_Chunks", 0, Geometry.Chunks.Buffer);
            world.FrameTable.Set("Graphite_Transforms", 0, render.Transforms.Buffer);
            world.FrameTable.Set("Graphite_PreviousTransforms", 0, render.PreviousTransforms.Buffer);
            world.FrameTable.Set("Graphite_Objects", 0, render.Attributes.Buffer);
            world.FrameTable.Set("Graphite_MaterialSlots", 0, render.MaterialSlots.Buffer);
        }
        foreach (CameraFrame camera in _cameras.Values)
        {
            if (camera.LastFrame == _frame)
                _buffers.AddRange(camera.Buffers);
        }

        if (Same(Buffers, _buffers))
            return;

        Buffers = [.. _buffers];
        _upload.Buffers = Buffers;
        _frameStart.SetPasses([_upload]);
        foreach (CameraFrame camera in _cameras.Values)
            camera.Pipeline.SetPasses([camera.Scene, camera.Present]);
    }

    private static bool Same(DeviceBuffer[] a, List<DeviceBuffer> b)
    {
        if (a.Length != b.Count)
            return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }

    // Per view state is released after a view has not been submitted for a few frames.
    private void ReleaseStaleCameras()
    {
        List<int>? stale = null;
        foreach ((int id, CameraFrame camera) in _cameras)
        {
            if (_frame - camera.LastFrame > 8)
                (stale ??= new()).Add(id);
        }
        if (stale == null)
            return;
        foreach (int id in stale)
        {
            _textures.Untrack(_cameras[id].ViewTable);
            _cameras[id].Dispose();
            _cameras.Remove(id);
        }
    }

    public void Dispose()
    {
        foreach (CameraFrame camera in _cameras.Values)
            camera.Dispose();
        _cameras.Clear();
        _frameStart.Dispose();
        Programs.Dispose();
        PresentProgram.Dispose();
        LinearSampler.Dispose();
    }
}
