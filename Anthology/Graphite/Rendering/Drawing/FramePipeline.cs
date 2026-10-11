using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using Prowl.Graphite.RenderGraph;
using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// The Level 0 programs of draw programs: the variant compiled with the view's global keywords, put through the
/// material rewrite, with its pass state, the material's state override and the winding of mirrored objects.
/// A variant compiles on a worker the first time it is asked for, and until it is ready draws use the nearest
/// compiled variant of the same pass: the one differing in the fewest axes, ties going to axis defaults. Only the
/// compile and rewrite run on the worker, programs are created on the render thread. In deterministic mode compiles
/// block instead. Programs that fail to compile are reported once and draw nothing.
/// </summary>
internal sealed class ProgramCache(GraphicsDevice device, IShaderCompiler compiler, GlobalRegistry globals, DrawPrograms programs, bool deterministic, Action<string, string> report) : IDisposable
{
    private sealed class Entry
    {
        public required DrawProgram Program;
        public required bool Mirrored;
        public required int[] Values;
        public Task<ShaderDescription>? Pending;
        public GraphicsProgram? Built;
        public bool Failed;
    }

    private readonly record struct Family(Shader Shader, int PassIndex, PassState? StateOverride, bool Mirrored);

    private readonly Dictionary<(ushort Program, bool Mirrored, ulong Globals), Entry> _entries = new();
    private readonly Dictionary<Family, List<Entry>> _families = new();

    public static readonly ResourceLayoutDescription FrameLayout = GraphiteBindings.FrameTable(RendererTextures.Textures2D, RendererTextures.OtherTextures);
    public static readonly ResourceLayoutDescription ViewLayout = GraphiteBindings.ViewTable(RendererTextures.Samplers);

    /// <summary>Variants still compiling on a worker.</summary>
    public int Pending
    {
        get
        {
            int pending = 0;
            foreach (Entry entry in _entries.Values)
            {
                if (entry.Pending != null)
                    pending++;
            }
            return pending;
        }
    }

    /// <summary>The program to draw with now: the wanted variant when it is ready, else the nearest ready one, else null. Render thread.</summary>
    public GraphicsProgram? Get(ushort id, bool mirrored, IReadOnlyDictionary<string, string> keywords)
    {
        DrawProgram program = programs[id];
        IReadOnlyList<VariantSpace> axes = program.Shader.GlobalAxes;
        ulong key = 0, stride = 1;
        Span<int> globalValues = stackalloc int[axes.Count];
        for (int i = 0; i < axes.Count; i++)
        {
            globalValues[i] = ValueIndex(axes[i], keywords.GetValueOrDefault(axes[i].Name));
            key += (ulong)globalValues[i] * stride;
            stride *= (ulong)axes[i].Values.Count;
        }

        if (!_entries.TryGetValue((id, mirrored, key), out Entry? entry))
        {
            entry = Create(program, mirrored, globalValues);
            _entries[(id, mirrored, key)] = entry;
        }

        Complete(entry);
        return entry.Built ?? (entry.Failed ? null : Nearest(entry));
    }

    public void Dispose()
    {
        foreach (Entry entry in _entries.Values)
        {
            try
            {
                entry.Pending?.Wait();
            }
            catch (AggregateException)
            {
            }
            entry.Built?.Dispose();
        }
        _entries.Clear();
        _families.Clear();
    }

    private Entry Create(DrawProgram program, bool mirrored, ReadOnlySpan<int> globalValues)
    {
        IReadOnlyList<VariantSpace> materialAxes = program.Shader.MaterialAxes;
        IReadOnlyList<VariantSpace> globalAxes = program.Shader.GlobalAxes;
        int[] values = new int[materialAxes.Count + globalAxes.Count];
        Keyword[] combo = new Keyword[values.Length];
        for (int i = 0; i < materialAxes.Count; i++)
        {
            values[i] = ValueIndex(materialAxes[i], program.Keywords[i].Value);
            combo[i] = program.Keywords[i];
        }
        for (int i = 0; i < globalAxes.Count; i++)
        {
            values[materialAxes.Count + i] = globalValues[i];
            combo[materialAxes.Count + i] = new Keyword(globalAxes[i].Name, globalAxes[i].Values[globalValues[i]]);
        }

        Entry entry = new() { Program = program, Mirrored = mirrored, Values = values };
        Family family = new(program.Shader, program.PassIndex, program.StateOverride, mirrored);
        if (!_families.TryGetValue(family, out List<Entry>? members))
        {
            members = new List<Entry>();
            _families[family] = members;
        }
        members.Add(entry);

        entry.Pending = deterministic ? DescribeNow(program, combo) : Task.Run(() => Describe(program, combo));
        return entry;
    }

    private Task<ShaderDescription> DescribeNow(DrawProgram program, Keyword[] combo)
    {
        try
        {
            return Task.FromResult(Describe(program, combo));
        }
        catch (Exception e)
        {
            return Task.FromException<ShaderDescription>(e);
        }
    }

    // A variant that declared new globals waits until the next extraction has grown the regions to hold them.
    private void Complete(Entry entry)
    {
        if (entry.Pending is not { IsCompleted: true } pending || globals.NeedsGrowth)
            return;

        entry.Pending = null;
        DrawProgram program = entry.Program;
        try
        {
            ShaderDescription description = pending.GetAwaiter().GetResult();
            entry.Built = Build(program, description, entry.Mirrored);
        }
        catch (Exception e)
        {
            entry.Failed = true;
            report($"program.{program.Shader.Name}.{program.PassIndex}.{string.Join(",", entry.Values)}", $"{program.Shader.Name}: {e.Message}");
        }
    }

    // The ready variant of the same pass differing in the fewest axes, ties going to the one whose differences are defaults.
    private GraphicsProgram? Nearest(Entry wanted)
    {
        GraphicsProgram? best = null;
        (int Differences, int NonDefault) bestScore = (int.MaxValue, int.MaxValue);
        foreach (Entry candidate in _families[new Family(wanted.Program.Shader, wanted.Program.PassIndex, wanted.Program.StateOverride, wanted.Mirrored)])
        {
            Complete(candidate);
            if (candidate.Built == null)
                continue;

            int differences = 0, nonDefault = 0;
            for (int i = 0; i < wanted.Values.Length; i++)
            {
                if (candidate.Values[i] == wanted.Values[i])
                    continue;
                differences++;
                if (candidate.Values[i] != 0)
                    nonDefault++;
            }

            if ((differences, nonDefault).CompareTo(bestScore) < 0)
            {
                bestScore = (differences, nonDefault);
                best = candidate.Built;
            }
        }
        return best;
    }

    private static int ValueIndex(VariantSpace axis, string? value)
    {
        for (int v = 0; v < axis.Values.Count; v++)
        {
            if (axis.Values[v] == value)
                return v;
        }
        return 0;
    }

    // Worker thread: the compile and the material rewrite, nothing that touches the device.
    private ShaderDescription Describe(DrawProgram program, Keyword[] combo)
    {
        ShaderPass pass = program.Shader.Definition.Passes![program.PassIndex];
        ShaderDescription description = compiler.Compile(pass, combo, device.BackendType);
        return MaterialRewrite.Rewrite(program.Shader.Name, description, program.Shader.Schema, compiler.GetGlobals(pass), globals, FrameLayout, ViewLayout);
    }

    private GraphicsProgram Build(DrawProgram program, ShaderDescription description, bool mirrored)
    {
        ShaderPass pass = program.Shader.Definition.Passes![program.PassIndex];
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

/// <summary>The view a camera pipeline draws for: its target and size. Kept per camera so a frame allocates nothing.</summary>
internal sealed class CameraView : IRenderView
{
    public Framebuffer? Target { get; set; }
    public uint PixelWidth { get; set; } = 1;
    public uint PixelHeight { get; set; } = 1;
    public int ViewId { get; set; }
}

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
    public readonly DrawList Transparent = new();
    public readonly RenderPipeline Pipeline = new();
    public readonly ScenePass Scene;
    public readonly PresentPass Present;
    public readonly TableRing ViewTable;
    public readonly GpuArray Instances;
    public readonly GpuArray Records;
    public readonly GpuArray Commands;
    public readonly GpuArray ViewBlocks;
    public ViewDesc Desc;
    public WorldFrame World = null!;
    public int LastFrame;
    public readonly CameraView View = new();
    public readonly IRenderView[] Views;

    private IndirectDrawIndexedArguments[] _commands = new IndirectDrawIndexedArguments[64];
    private DrawRecord[] _records = new DrawRecord[64];
    private uint[] _instances = new uint[256];

    public CameraFrame(GraphicsDevice device, FramePipeline frame, int viewId)
    {
        Scene = new ScenePass(frame, this);
        Present = new PresentPass(frame);
        Views = [View];
        View.ViewId = viewId;
        Lists = [Prepass, Forward, Transparent];
        Pipeline.Name = $"Graphite.Camera {viewId}";
        Pipeline.SetPasses([Scene, Present]);
        ViewTable = new TableRing(device, ProgramCache.ViewLayout, $"View table {viewId}");
        Instances = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"Instances {viewId}");
        Records = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"Draw records {viewId}");
        Commands = new GpuArray(device, BufferUsage.IndirectBuffer, $"Indirect commands {viewId}");
        ViewBlocks = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"View blocks {viewId}");
    }

    public void AddBuffers(List<DeviceBuffer> buffers)
    {
        buffers.Add(Instances.Buffer);
        buffers.Add(Records.Buffer);
        buffers.Add(Commands.Buffer);
        buffers.Add(ViewBlocks.Buffer);
    }

    /// <summary>The frame's draw lists in drawing order: prepass, opaque, transparent.</summary>
    public DrawList[] Lists { get; }

    /// <summary>Where each list's commands start in the uploaded command buffer.</summary>
    public int[] CommandBases { get; } = new int[3];

    /// <summary>Queues the frame's commands, draw records, instances and view block, the lists one after another.</summary>
    public void Upload(Uploader uploader, in ViewBlock block)
    {
        int commands = 0, instances = 0;
        foreach (DrawList list in Lists)
        {
            commands += list.CommandCount;
            instances += list.InstanceCount;
        }
        if (_commands.Length < commands)
        {
            Array.Resize(ref _commands, commands * 2);
            Array.Resize(ref _records, commands * 2);
        }
        if (_instances.Length < instances)
            Array.Resize(ref _instances, instances * 2);

        int commandBase = 0, instanceBase = 0;
        for (int l = 0; l < Lists.Length; l++)
        {
            DrawList list = Lists[l];
            CommandBases[l] = commandBase;
            for (int i = 0; i < list.CommandCount; i++)
            {
                IndirectDrawIndexedArguments command = list.Commands[i];
                command.FirstInstance += (uint)instanceBase;
                _commands[commandBase + i] = command;
            }
            Array.Copy(list.Records, 0, _records, commandBase, list.CommandCount);
            Array.Copy(list.Instances, 0, _instances, instanceBase, list.InstanceCount);
            commandBase += list.CommandCount;
            instanceBase += list.InstanceCount;
        }

        Commands.Write<IndirectDrawIndexedArguments>(uploader, _commands.AsSpan(0, commands));
        Records.Write<DrawRecord>(uploader, _records.AsSpan(0, commands));
        Instances.Write<uint>(uploader, _instances.AsSpan(0, instances));
        ViewBlocks.Write<ViewBlock>(uploader, new ReadOnlySpan<ViewBlock>(in block));
    }

    public void Dispose()
    {
        Pipeline.Dispose();
        ViewTable.Dispose();
        Instances.Dispose();
        Records.Dispose();
        Commands.Dispose();
        ViewBlocks.Dispose();
    }
}

/// <summary>Draws a view's prepass, opaque and transparent lists into its HDR scene target.</summary>
internal sealed class ScenePass(FramePipeline frame, CameraFrame camera) : IPass
{
    public static readonly RenderResourceID SceneId = RenderResourceID.Intern("Graphite.Scene");

    private static readonly GraphTextureDesc s_scene = GraphTextureDesc.ViewSized(PixelFormat.R16_G16_B16_A16_Float, depth: true) with { DepthFormat = PixelFormat.D32_Float };

    private readonly PropertySet _properties = new();
    private (DeviceBuffer? Buffer, uint Offset, uint Size) _globals;
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
        (DeviceBuffer Buffer, uint Offset, uint Size) globals = (registry.Buffer, (uint)camera.World.GlobalRegion * registry.RegionWords * 4, registry.RegionWords * 4);
        if (globals != _globals)
        {
            _globals = globals;
            camera.ViewTable.Set("Graphite_Globals", 0, new DeviceBufferRange(globals.Buffer, globals.Offset, globals.Size));
        }
        camera.ViewTable.Set("Graphite_Views", 0, camera.ViewBlocks.Buffer);
        DescriptorTable viewTable = camera.ViewTable.Get(ring);
        DescriptorTable frameTable = camera.World.FrameTable.Get(ring);

        Float4 clear = camera.Desc.ClearColor;
        cmd.SetFramebuffer(context.GetRenderTexture(_target).Framebuffer, new TargetLoadStoreOps(AttachmentOps.Clear(new Color(clear.X, clear.Y, clear.Z, clear.W)), AttachmentOps.Clear(0f)));
        cmd.SetFullViewport();
        _properties.SetBuffer(GraphiteBindings.InstancesName, camera.Instances.Buffer);
        _properties.SetBuffer(GraphiteBindings.DrawRecordsName, camera.Records.Buffer);

        for (int l = 0; l < camera.Lists.Length; l++)
            Draw(cmd, camera.Lists[l], camera.CommandBases[l], frameTable, viewTable);
    }

    private void Draw(CommandBuffer cmd, DrawList list, int commandBase, DescriptorTable frameTable, DescriptorTable viewTable)
    {
        uint stride = (uint)Marshal.SizeOf<IndirectDrawIndexedArguments>();
        foreach (DrawCall call in list.Calls)
        {
            GraphicsProgram? program = frame.Programs.Get(call.Program, call.Mirrored, camera.World.World.Globals.KeywordSnapshot);
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
    private readonly IRenderView[] _frameStartViews = [new CameraView { ViewId = -1 }];
    private int _frame;

    private readonly RendererTextures _textures;
    private readonly bool _deterministic;

    private readonly Uploader _uploader;

    public FramePipeline(GraphicsDevice device, IShaderCompiler compiler, Uploader uploader, GeometryPool geometry, MaterialSystem materials, GlobalRegistry globals, DrawPrograms programs, RendererTextures textures, bool deterministic, Action<string, string> report)
    {
        _device = device;
        _textures = textures;
        _uploader = uploader;
        _deterministic = deterministic;
        Geometry = geometry;
        Materials = materials;
        Globals = globals;
        DrawPrograms = programs;
        Programs = new ProgramCache(device, compiler, globals, programs, deterministic, report);
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
    public ExecutionTask Run(ReadOnlySpan<ViewDesc> views, IReadOnlyDictionary<RenderWorld, WorldFrame> worlds, IEnumerable<WorldFrame> allWorlds, ulong lastDispatched)
    {
        _frame++;
        _dispatches.Clear();
        _dispatches.Add(new GraphDispatch(_frameStart, _frameStartViews));
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
            camera.View.Target = view.Target;
            camera.View.PixelWidth = view.Target.Width;
            camera.View.PixelHeight = view.Target.Height;
            _dispatches.Add(new GraphDispatch(camera.Pipeline, camera.Views));
        }

        ResolvePrograms();
        if (_deterministic && Globals.NeedsGrowth)
        {
            Globals.Extract();
            Globals.Flush(_uploader, lastDispatched);
            ResolvePrograms();
        }

        CollectBuffers(allWorlds);
        ExecutionTask task = _device.DispatchGraph(CollectionsMarshal.AsSpan(_dispatches));
        ReleaseStaleCameras();
        return task;
    }

    // Asks for every call's program before recording, so compiles start early and deterministic ones finish here.
    private void ResolvePrograms()
    {
        foreach (CameraFrame camera in _cameras.Values)
        {
            if (camera.LastFrame != _frame)
                continue;
            foreach (DrawList list in camera.Lists)
            {
                foreach (DrawCall call in list.Calls)
                    Programs.Get(call.Program, call.Mirrored, camera.World.World.Globals.KeywordSnapshot);
            }
        }
    }

    private void Prepare(CameraFrame camera, in ViewDesc view, WorldFrame world)
    {
        ViewLayer layer = view.Layers[0];
        Float4x4 viewProjection = layer.Projection * layer.View;
        CullFrustum frustum = new(viewProjection, layer.Position);
        world.Buckets.Cull(frustum, view.CullingMask);
        world.Buckets.Build(0, frustum, camera.Prepass);
        world.Buckets.Build(1, frustum, camera.Forward);
        world.Buckets.BuildTransparent(frustum, camera.Transparent);
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
            foreach (GpuTable table in render.Tables)
                _buffers.Add(table.Buffer);
            world.FrameTable.Set("Graphite_Chunks", 0, Geometry.Chunks.Buffer);
            world.FrameTable.Set("Graphite_Transforms", 0, render.Transforms.Buffer);
            world.FrameTable.Set("Graphite_PreviousTransforms", 0, render.PreviousTransforms.Buffer);
            world.FrameTable.Set("Graphite_Objects", 0, render.Attributes.Buffer);
            world.FrameTable.Set("Graphite_MaterialSlots", 0, render.MaterialSlots.Buffer);
        }
        foreach (CameraFrame camera in _cameras.Values)
        {
            if (camera.LastFrame == _frame)
                camera.AddBuffers(_buffers);
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
