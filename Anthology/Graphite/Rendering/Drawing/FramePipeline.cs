using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
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

    /// <summary>Drops every variant of a freed draw program id, before the id is reused for another program.</summary>
    public void Drop(ushort id)
    {
        List<(ushort, bool, ulong)>? keys = null;
        foreach (((ushort Program, bool Mirrored, ulong Globals) key, Entry entry) in _entries)
        {
            if (key.Program != id)
                continue;
            (keys ??= new()).Add(key);
            entry.Built?.Dispose();
            Family family = new(entry.Program.Shader, entry.Program.PassIndex, entry.Program.StateOverride, entry.Mirrored);
            if (_families.TryGetValue(family, out List<Entry>? members))
            {
                members.Remove(entry);
                if (members.Count == 0)
                    _families.Remove(family);
            }
        }
        if (keys == null)
            return;
        foreach ((ushort, bool, ulong) key in keys)
            _entries.Remove(key);
    }

    /// <summary>Variants known to the cache, ready, compiling or failed.</summary>
    internal int EntryCount => _entries.Count;

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

    private readonly Dictionary<GraphicsProgram, PropertyID[]> _userBuffers = new();

    /// <summary>The buffers a program reads that the host sets through Globals.SetBuffer.</summary>
    public PropertyID[] UserBuffers(GraphicsProgram program)
    {
        lock (_userBuffers)
            return _userBuffers.TryGetValue(program, out PropertyID[]? buffers) ? buffers : [];
    }

    /// <summary>Whether the wanted variant itself is built, rather than a nearby one standing in. Render thread.</summary>
    public bool Ready(ushort id, bool mirrored, IReadOnlyDictionary<string, string> keywords)
    {
        Get(id, mirrored, keywords);
        DrawProgram program = programs[id];
        IReadOnlyList<VariantSpace> axes = program.Shader.GlobalAxes;
        ulong key = 0, stride = 1;
        for (int i = 0; i < axes.Count; i++)
        {
            key += (ulong)ValueIndex(axes[i], keywords.GetValueOrDefault(axes[i].Name)) * stride;
            stride *= (ulong)axes[i].Values.Count;
        }
        return _entries.TryGetValue((id, mirrored, key), out Entry? entry) && entry.Built != null;
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

    // Colour takes the pass's blend. The G-buffer targets are written only by an opaque pass whose fragment stage writes
    // them, so transparent and other passes leave what the opaque surfaces wrote. The prepass writes no colour at all.
    internal static BlendStateDescription SceneBlend(in ShaderDescription description, PassState state, bool prepass)
    {
        BlendAttachmentDescription color = state.ToBlendState(BlendStateDescription.SingleOverrideBlend).AttachmentStates[0];
        bool blended = color.BlendEnabled && !(color.SourceColorFactor == BlendFactor.One && color.DestinationColorFactor == BlendFactor.Zero);
        bool gbuffer = !prepass && !blended && WritesGBuffer(description);
        BlendAttachmentDescription target = BlendAttachmentDescription.OverrideBlend;
        BlendAttachmentDescription masked = target with { ColorWriteMask = ColorWriteMask.None };
        if (prepass)
            color.ColorWriteMask = ColorWriteMask.None;

        BlendStateDescription blend = state.ToBlendState(BlendStateDescription.SingleOverrideBlend);
        blend.AttachmentStates = [color, gbuffer ? target : masked, gbuffer ? target : masked];
        return blend;
    }

    private static bool WritesGBuffer(in ShaderDescription description)
    {
        foreach (ShaderStageDescription stage in description.Stages)
        {
            if (stage.Stage != ShaderStages.Fragment)
                continue;
            SpirvModule module = new(stage.ShaderBytes);
            uint normal = module.FindVariableAtLocation(SpirvModule.Output, 1);
            uint material = module.FindVariableAtLocation(SpirvModule.Output, 2);
            return normal != 0 && material != 0 && module.Stores(normal) && module.Stores(material);
        }
        return false;
    }

    private GraphicsProgram Build(DrawProgram program, ShaderDescription description, bool mirrored)
    {
        ShaderPass pass = program.Shader.Definition.Passes![program.PassIndex];
        PassState state = program.StateOverride is { } over ? over.Apply(pass.State) : pass.State;
        description.BlendState = SceneBlend(description, state, ShaderDefinition.PassHasTag(pass, PassType.Tag, PassType.Prepass.Name));
        description.DepthStencilState = state.ToDepthStencilState(DepthStencilStateDescription.DepthOnlyGreaterEqual);
        RasterizerStateDescription raster = state.ToRasterizerState(RasterizerStateDescription.Default);
        if (mirrored)
            raster.FrontFace = raster.FrontFace == FrontFace.Clockwise ? FrontFace.CounterClockwise : FrontFace.Clockwise;
        if (ShaderDefinition.PassHasTag(pass, PassType.Tag, PassType.Sky.Name))
        {
            description.DepthStencilState = DepthStencilStateDescription.DepthOnlyGreaterEqualRead;
            raster = RasterizerStateDescription.CullNone;
        }
        description.RasterizerState = raster;

        GraphicsProgram built = device.ResourceFactory.CreateGraphicsProgram(description);
        built.Name = $"{program.Shader.Name} {pass.Name}";
        List<PropertyID> buffers = new();
        foreach (ResourceLayoutDescription layout in description.ResourceLayouts ?? [])
        {
            if (layout.Set != GraphiteBindings.UserSet)
                continue;
            foreach (ResourceLayoutElementDescription element in layout.Elements)
                buffers.Add(element.Name);
        }
        lock (_userBuffers)
            _userBuffers[built] = [.. buffers];
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
    public readonly GpuArray ViewLights;
    public readonly GpuArray ViewLightNodes;
    public readonly GpuArray ViewDecals;
    public readonly GpuArray ViewDecalNodes;
    public readonly GpuArray LightRegions;
    public readonly GpuArray LightRegionNodes;
    public readonly GpuArray DecalRegions;
    public readonly GpuArray DecalRegionNodes;
    public readonly GpuArray ProbeRegions;
    public readonly GpuArray ProbeRegionNodes;
    public readonly LightTreeBuilder LightTree = new();
    public readonly LightTreeBuilder DecalTree = new();
    public readonly LightTreeBuilder LightRegionTree = new();
    public readonly LightTreeBuilder DecalRegionTree = new();
    public readonly LightTreeBuilder ProbeRegionTree = new();

    /// <summary>Whether the view's basis flips handedness, as cube captures do, so every draw's winding flips too.</summary>
    public bool Mirrored;
    public ViewDesc Desc;
    public WorldFrame World = null!;
    public int LastFrame;
    public readonly CameraView View = new();

    /// <summary>Last frame's view block per layer, for motion: its view projection and camera position.</summary>
    public readonly ViewBlock[] Previous = new ViewBlock[6];
    public readonly ViewBlock[] Blocks = new ViewBlock[6];
    public bool HasPrevious;
    public int LayerCount = 1;
    public readonly IRenderView[] Views;

    private IndirectDrawIndexedArguments[] _commands = new IndirectDrawIndexedArguments[64];
    private DrawRecord[] _records = new DrawRecord[64];
    private uint[] _instances = new uint[256];

    public CameraFrame(GraphicsDevice device, FramePipeline frame, int viewId)
    {
        Scene = new ScenePass(frame, this);
        Present = new PresentPass(frame, this);
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
        ViewLights = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"View lights {viewId}");
        ViewLightNodes = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"View light nodes {viewId}");
        ViewDecals = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"View decals {viewId}");
        ViewDecalNodes = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"View decal nodes {viewId}");
        LightRegions = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"Light regions {viewId}");
        LightRegionNodes = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"Light region nodes {viewId}");
        DecalRegions = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"Decal regions {viewId}");
        DecalRegionNodes = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"Decal region nodes {viewId}");
        ProbeRegions = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"Probe regions {viewId}");
        ProbeRegionNodes = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"Probe region nodes {viewId}");
    }

    public void AddBuffers(List<DeviceBuffer> buffers)
    {
        buffers.Add(Instances.Buffer);
        buffers.Add(Records.Buffer);
        buffers.Add(Commands.Buffer);
        buffers.Add(ViewBlocks.Buffer);
        buffers.Add(ViewLights.Buffer);
        buffers.Add(ViewLightNodes.Buffer);
        buffers.Add(ViewDecals.Buffer);
        buffers.Add(ViewDecalNodes.Buffer);
        buffers.Add(LightRegions.Buffer);
        buffers.Add(LightRegionNodes.Buffer);
        buffers.Add(DecalRegions.Buffer);
        buffers.Add(DecalRegionNodes.Buffer);
        buffers.Add(ProbeRegions.Buffer);
        buffers.Add(ProbeRegionNodes.Buffer);
    }

    /// <summary>The frame's draw lists in drawing order: prepass, opaque, transparent.</summary>
    public DrawList[] Lists { get; }

    /// <summary>Where each list's commands start in the uploaded command buffer.</summary>
    public int[] CommandBases { get; } = new int[3];

    /// <summary>Queues the frame's commands, draw records, instances and view blocks, the lists one after another.</summary>
    public void Upload(Uploader uploader, ReadOnlySpan<ViewBlock> blocks)
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
        ViewBlocks.Write<ViewBlock>(uploader, blocks);
    }

    public void Dispose()
    {
        Pipeline.Dispose();
        ViewTable.Dispose();
        Instances.Dispose();
        Records.Dispose();
        Commands.Dispose();
        ViewBlocks.Dispose();
        ViewLights.Dispose();
        ViewLightNodes.Dispose();
        ViewDecals.Dispose();
        ViewDecalNodes.Dispose();
        LightRegions.Dispose();
        LightRegionNodes.Dispose();
        DecalRegions.Dispose();
        DecalRegionNodes.Dispose();
        ProbeRegions.Dispose();
        ProbeRegionNodes.Dispose();
    }
}

/// <summary>Draws a view's prepass, opaque and transparent lists into its HDR scene target.</summary>
internal sealed class ScenePass(FramePipeline frame, CameraFrame camera) : IPass
{
    public static readonly RenderResourceID SceneId = RenderResourceID.Intern("Graphite.Scene");

    private static readonly GraphTextureDesc s_scene = GraphTextureDesc.ViewSized(PixelFormat.R16_G16_B16_A16_Float, depth: true) with
    {
        ColorFormats = [PixelFormat.R16_G16_B16_A16_Float, PixelFormat.R16_G16_SNorm, PixelFormat.R8_G8_UNorm],
        DepthFormat = PixelFormat.D32_Float,
    };

    /// <summary>The scene target, drawn through multiview into one array layer per view layer.</summary>
    public static GraphTextureDesc Target(int layers) => s_scene with { ArrayLayers = layers > 1 ? (uint)layers : 0 };

    private readonly PropertySet _properties = new();
    private (DeviceBuffer? Buffer, uint Offset, uint Size) _globals;
    private TextureHandle _target;

    public string Name => "Graphite.Scene";

    public void Setup(RenderContextBuilder builder)
    {
        _target = builder.DeclareOutputTexture(SceneId, Target(camera.LayerCount));
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
        camera.ViewTable.Set("Graphite_ViewLights", 0, camera.ViewLights.Buffer);
        camera.ViewTable.Set("Graphite_ViewLightNodes", 0, camera.ViewLightNodes.Buffer);
        camera.ViewTable.Set("Graphite_ViewDecals", 0, camera.ViewDecals.Buffer);
        camera.ViewTable.Set("Graphite_ViewDecalNodes", 0, camera.ViewDecalNodes.Buffer);
        camera.ViewTable.Set("Graphite_LightRegions", 0, camera.LightRegions.Buffer);
        camera.ViewTable.Set("Graphite_LightRegionNodes", 0, camera.LightRegionNodes.Buffer);
        camera.ViewTable.Set("Graphite_DecalRegions", 0, camera.DecalRegions.Buffer);
        camera.ViewTable.Set("Graphite_DecalRegionNodes", 0, camera.DecalRegionNodes.Buffer);
        camera.ViewTable.Set("Graphite_ProbeRegions", 0, camera.ProbeRegions.Buffer);
        camera.ViewTable.Set("Graphite_ProbeRegionNodes", 0, camera.ProbeRegionNodes.Buffer);
        SetWorldBuffers(camera.ViewTable, camera.World.World);
        DescriptorTable viewTable = camera.ViewTable.Get(ring);
        DescriptorTable frameTable = frame.TextureTable;

        Float4 clear = camera.Desc.ClearColor;
        cmd.SetFramebuffer(context.GetRenderTexture(_target).Framebuffer, new TargetLoadStoreOps(AttachmentOps.Clear(new Color(clear.X, clear.Y, clear.Z, clear.W)), AttachmentOps.Clear(0f)));
        cmd.SetFullViewport();
        cmd.ClearColorTarget(1, new Color(0, 0, 0, 0));
        cmd.ClearColorTarget(2, new Color(0, 0, 0, 0));
        _properties.SetBuffer(GraphiteBindings.InstancesName, camera.Instances.Buffer);
        _properties.SetBuffer(GraphiteBindings.DrawRecordsName, camera.Records.Buffer);

        for (int l = 0; l < camera.Lists.Length; l++)
        {
            if (camera.Lists[l] == camera.Transparent)
                DrawSky(cmd, frameTable, viewTable);
            Draw(cmd, camera.Lists[l], camera.CommandBases[l], frameTable, viewTable);
        }
    }

    // The host's buffers a program reads, from the world's globals over the renderer's. A missing one skips the draw.
    private bool BindUserBuffers(GraphicsProgram program, Shader shader)
    {
        IReadOnlyDictionary<PropertyID, DeviceBuffer> buffers = camera.World.World.Globals.BufferSnapshot;
        foreach (PropertyID name in frame.Programs.UserBuffers(program))
        {
            if (!buffers.TryGetValue(name, out DeviceBuffer? buffer) || buffer.IsDisposed)
            {
                frame.Report($"global.buffer.{name.Text()}", $"{shader.Name} reads the buffer {name.Text()}, which no Globals.SetBuffer provides, so it does not draw.");
                return false;
            }
            _properties.SetBuffer(name, buffer);
        }
        return true;
    }

    // The world's buffers sit in every view's table, which a frame in flight's table never sees change
    private void SetWorldBuffers(TableRing table, RenderWorld world)
    {
        RenderWorld.RenderSide render = world.Render;
        WorldLighting lighting = world.Lighting;
        table.Set("Graphite_Chunks", 0, frame.Geometry.Chunks.Buffer);
        table.Set("Graphite_Transforms", 0, render.Transforms.Buffer);
        table.Set("Graphite_PreviousTransforms", 0, render.PreviousTransforms.Buffer);
        table.Set("Graphite_Objects", 0, render.Attributes.Buffer);
        table.Set("Graphite_MaterialSlots", 0, render.MaterialSlots.Buffer);
        table.Set("Graphite_StaticLights", 0, lighting.StaticLights.Buffer);
        table.Set("Graphite_StaticLightNodes", 0, lighting.StaticLightNodes.Buffer);
        table.Set("Graphite_StaticDecals", 0, lighting.StaticDecals.Buffer);
        table.Set("Graphite_StaticDecalNodes", 0, lighting.StaticDecalNodes.Buffer);
        table.Set("Graphite_WorldLighting", 0, lighting.World.Buffer);
        table.Set("Graphite_ObjectProbes", 0, world.Probes.ObjectSh);
        table.Set("Graphite_ReflectionProbes", 0, world.Reflections.Regions.Records.Buffer);
        table.Set("Graphite_ReflectionProbeNodes", 0, world.Reflections.Regions.Nodes.Buffer);
        table.Set("Graphite_SkyAmbientSH", 0, world.Reflections.SkyAmbient);
    }

    // A fullscreen triangle at the far plane with the sky material's slot in the push constants
    private void DrawSky(CommandBuffer cmd, DescriptorTable frameTable, DescriptorTable viewTable)
    {
        WorldFrame world = camera.World;
        if (!camera.Desc.Sky || world.SkyProgram is not ushort id || world.World.Lighting.Environment.Sky is not { IsDisposed: false, Table: { } table } sky)
            return;
        GraphicsProgram? program = frame.Programs.Get(id, false, world.World.Globals.KeywordSnapshot);
        if (program == null || !BindUserBuffers(program, sky.Shader))
            return;

        cmd.SetShader(program);
        cmd.SetVertexSource(VertexSource.None);
        cmd.SetDescriptorTable(GraphiteBindings.FrameSet, frameTable);
        cmd.SetDescriptorTable(GraphiteBindings.ViewSet, viewTable);
        _properties.SetBuffer(GraphiteBindings.MaterialsName, table.Records.Buffer);
        cmd.SetProperties(_properties);
        cmd.SetPushConstants(new FramePipeline.Push { InstanceBase = sky.Slot });
        cmd.Draw(3);
    }

    private void Draw(CommandBuffer cmd, DrawList list, int commandBase, DescriptorTable frameTable, DescriptorTable viewTable)
    {
        uint stride = (uint)Marshal.SizeOf<IndirectDrawIndexedArguments>();
        foreach (DrawCall call in list.Calls)
        {
            GraphicsProgram? program = frame.Programs.Get(call.Program, call.Mirrored != camera.Mirrored, camera.World.World.Globals.KeywordSnapshot);
            if (program == null)
                continue;

            Shader shader = frame.DrawPrograms[call.Program].Shader;
            if (!frame.Materials.Tables.TryGetValue(shader, out MaterialTable? table) || !BindUserBuffers(program, shader))
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
internal sealed class PresentPass(FramePipeline frame, CameraFrame camera) : IPass
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
        cmd.SetShader(camera.LayerCount > 1 ? frame.PresentLayersProgram : frame.PresentProgram);
        cmd.SetVertexSource(VertexSource.None);
        _properties.SetTexture("Graphite_Source", context.GetRenderTexture(_scene).ColorTextures[0], frame.LinearSampler);
        cmd.SetProperties(_properties);
        cmd.Draw(3);
    }
}

/// <summary>A world as the frame pipeline draws it: its buckets, its globals region and its frame table.</summary>
internal sealed class WorldFrame(RenderWorld world, BucketSet buckets, int globalRegion) : IDisposable
{
    private static int s_nextCapture;

    public RenderWorld World { get; } = world;
    public BucketSet Buckets { get; } = buckets;
    public int GlobalRegion { get; } = globalRegion;

    /// <summary>The draw program of the sky material's Sky pass, held while the world draws it.</summary>
    public ushort? SkyProgram;

    /// <summary>The views capturing the sky and the reflection probes: six faces at once or one, reused across frames.</summary>
    public CameraFrame? SkyCamera;
    public readonly List<CameraFrame> ProbeCameras = new();
    public readonly List<CameraFrame> ProbeFaceCameras = new();
    public int ProbeCamerasUsed, ProbeFaceCamerasUsed;

    /// <summary>The pipeline projecting the sky onto harmonics for the sky ambient mode.</summary>
    public RenderPipeline? SkyAmbient;
    public SkyAmbientPass? SkyAmbientPass;

    /// <summary>Filter pipelines, one per filling of a probe layer this frame, reused across frames.</summary>
    public readonly List<(RenderPipeline Pipeline, PrefilterPass Pass)> Filters = new();
    public int FiltersUsed;

    /// <summary>A new capture view id, below any a host may use.</summary>
    public static int NextCaptureViewId() => int.MinValue + Interlocked.Increment(ref s_nextCapture);

    public IEnumerable<CameraFrame> Captures()
    {
        if (SkyCamera != null)
            yield return SkyCamera;
        foreach (CameraFrame camera in ProbeCameras)
            yield return camera;
        foreach (CameraFrame camera in ProbeFaceCameras)
            yield return camera;
    }

    public void Dispose()
    {
        foreach (CameraFrame camera in Captures())
            camera.Dispose();
        foreach ((RenderPipeline pipeline, _) in Filters)
            pipeline.Dispose();
        SkyAmbient?.Dispose();
    }
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
    private readonly ProbePass _probes;
    private readonly Dictionary<int, CameraFrame> _cameras = new();
    private readonly Dictionary<(int Page, PrimitiveTopology Topology), VertexSource> _vertexSources = new();
    private readonly List<GraphDispatch> _dispatches = new();
    private readonly List<DeviceBuffer> _buffers = new();
    private readonly List<CameraFrame> _captures = new();
    private readonly List<Texture> _disposeAfterDispatch = new();
    private readonly List<WorldFrame> _frameWorlds = new();
    private readonly IRenderView[] _frameStartViews = [new CameraView { ViewId = -1 }];
    private int _frame;

    private readonly RendererTextures _textures;
    private readonly bool _deterministic;

    private readonly Uploader _uploader;

    public FramePipeline(GraphicsDevice device, IShaderCompiler compiler, Uploader uploader, GeometryPool geometry, MaterialSystem materials, GlobalRegistry globals, DrawPrograms programs, RendererTextures textures, bool deterministic, Action<string, string> report)
    {
        Report = report;
        _device = device;
        _textures = textures;
        _uploader = uploader;
        _deterministic = deterministic;
        Geometry = geometry;
        Materials = materials;
        Globals = globals;
        DrawPrograms = programs;
        Programs = new ProgramCache(device, compiler, globals, programs, deterministic, report);
        programs.Freed += Programs.Drop;
        _upload = new UploadPass(uploader);
        _probes = new ProbePass(InternalShaders.Compute(device, compiler, "Internal/Probes.glsl"));
        _frameStart.Name = "Graphite.FrameStart";
        _frameStart.SetPasses([_upload, _probes]);

        PresentProgram = Fullscreen(device, compiler, "Internal/Present.glsl");
        PresentLayersProgram = Fullscreen(device, compiler, "Internal/PresentLayers.glsl");
        PrefilterProgram = Fullscreen(device, compiler, "Internal/Prefilter.glsl");
        SkyAmbientProgram = InternalShaders.Compute(device, compiler, "Internal/SkyAmbient.glsl");
        TrilinearClamp = device.ResourceFactory.CreateSampler(SamplerDescription.Linear with
        {
            AddressModeU = SamplerAddressMode.Clamp,
            AddressModeV = SamplerAddressMode.Clamp,
            AddressModeW = SamplerAddressMode.Clamp,
        });
        LinearSampler = device.ResourceFactory.CreateSampler(SamplerDescription.Linear);
    }

    public GeometryPool Geometry { get; }
    public MaterialSystem Materials { get; }
    public GlobalRegistry Globals { get; }
    public DrawPrograms DrawPrograms { get; }
    public ProgramCache Programs { get; }
    public GraphicsProgram PresentProgram { get; }

    /// <summary>Copies every layer of a multiview scene into the matching view of its target.</summary>
    public GraphicsProgram PresentLayersProgram { get; }

    public GraphicsProgram PrefilterProgram { get; }

    /// <summary>Reports a problem once per key.</summary>
    public Action<string, string> Report { get; }
    public ComputeProgram SkyAmbientProgram { get; }

    /// <summary>Frames a view keeps its per view state after it was last drawn.</summary>
    public int StaleViewFrames { get; init; } = 8;

    /// <summary>The renderer's bindless texture table, set 0 of every material program.</summary>
    public DescriptorTable TextureTable => _textures.Table;
    public Sampler TrilinearClamp { get; }

    private static GraphicsProgram Fullscreen(GraphicsDevice device, IShaderCompiler compiler, string file)
    {
        ShaderPass pass = new() { State = new PassState(), Source = ShaderLibrary.Get(ShaderLibrary.Prefix + file)!, SourceName = "Graphite/" + file };
        ShaderDescription description = compiler.Compile(pass, [], device.BackendType);
        description.BlendState = BlendStateDescription.SingleOverrideBlend;
        description.DepthStencilState = DepthStencilStateDescription.Disabled;
        description.RasterizerState = RasterizerStateDescription.CullNone;
        return device.ResourceFactory.CreateGraphicsProgram(description);
    }
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
    public ExecutionTask Run(ReadOnlySpan<ViewDesc> views, IReadOnlyDictionary<RenderWorld, WorldFrame> worlds, IEnumerable<WorldFrame> allWorlds, ulong lastDispatched, in FrameTime time)
    {
        _frame++;
        _dispatches.Clear();
        _dispatches.Add(new GraphDispatch(_frameStart, _frameStartViews));
        _frameWorlds.Clear();
        foreach (WorldFrame world in allWorlds)
        {
            _frameWorlds.Add(world);
            world.FiltersUsed = 0;
            world.ProbeCamerasUsed = 0;
            world.ProbeFaceCamerasUsed = 0;
            UpdateSkyProgram(world);
            WorldReflections reflections = world.World.Reflections;
            if (reflections.CaptureDue)
                CaptureSky(world, time);
            foreach (ProbeCapture capture in reflections.Captures)
                CaptureProbe(world, capture, time);
        }

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

            Draw(camera, view, world, time);
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
        foreach (Texture texture in _disposeAfterDispatch)
            texture.Dispose();
        _disposeAfterDispatch.Clear();
        ReleaseStaleCameras();
        return task;
    }

    private void Draw(CameraFrame camera, in ViewDesc view, WorldFrame world, in FrameTime time)
    {
        camera.LastFrame = _frame;
        camera.Desc = view;
        if (camera.LayerCount != view.LayerCount)
        {
            camera.LayerCount = view.LayerCount;
            camera.HasPrevious = false;
            camera.Pipeline.SetPasses([camera.Scene, camera.Present]);
        }
        camera.World = world;
        Prepare(camera, view, world, time);
        camera.View.Target = view.Target;
        camera.View.PixelWidth = view.Target.Width;
        camera.View.PixelHeight = view.Target.Height;
        _dispatches.Add(new GraphDispatch(camera.Pipeline, camera.Views));
    }

    // Holds the draw program of the environment's sky material while it has a Sky pass
    private void UpdateSkyProgram(WorldFrame world)
    {
        ushort? program = null;
        if (world.World.Lighting.Environment.Sky is { IsDisposed: false, Table: not null } sky && DrawPrograms.TryGet(sky.Draw, PassType.Sky, out ushort id))
            program = id;
        if (program == world.SkyProgram)
            return;
        if (program is ushort acquired)
            DrawPrograms.Acquire(acquired);
        if (world.SkyProgram is ushort released)
            DrawPrograms.Release(released);
        world.SkyProgram = program;
    }

    // Draws only the sky into the six faces of the world's capture cube, then filters it into the sky probe's mips
    private void CaptureSky(WorldFrame world, in FrameTime time)
    {
        WorldReflections reflections = world.World.Reflections;
        world.SkyCamera ??= Capturing();
        WorldReflections.Scratch scratch = reflections.SkyScratch!;
        if (world.SkyProgram is ushort sky && !Programs.Ready(sky, false, world.World.Globals.KeywordSnapshot))
            reflections.SkyIncomplete();

        ViewDesc capture = new()
        {
            World = world.World,
            ViewId = world.SkyCamera.View.ViewId,
            LayerCount = 6,
            Target = scratch.All,
            CullingMask = 0,
            ClearColor = world.World.Lighting.Environment.SkyColor,
            Near = 0.05f,
        };
        for (int face = 0; face < 6; face++)
            capture.Layers[face] = new ViewLayer { View = WorldReflections.FaceView(face), Projection = WorldReflections.FaceProjection };
        Draw(world.SkyCamera, capture, world, time);
        Fill(world, reflections.Sky!, scratch.Cube, null);

        if (world.SkyAmbient == null)
        {
            world.SkyAmbientPass = new SkyAmbientPass(SkyAmbientProgram, TrilinearClamp);
            world.SkyAmbient = new RenderPipeline { Name = "Graphite.SkyAmbient" };
            world.SkyAmbient.SetPasses([world.SkyAmbientPass]);
        }
        world.SkyAmbientPass!.Source = scratch.Cube;
        world.SkyAmbientPass.Output = reflections.SkyAmbient;
        _dispatches.Add(new GraphDispatch(world.SkyAmbient, _frameStartViews));
    }

    // Filters a captured cube into every mip of a prefiltered one
    private void Fill(WorldFrame world, WorldReflections.Target target, Texture source, Action<BakedReflectionProbe>? bake)
    {
        if (world.FiltersUsed == world.Filters.Count)
        {
            PrefilterPass created = new(PrefilterProgram, TrilinearClamp, world.World.Reflections.PrefilterSamples);
            RenderPipeline pipeline = new() { Name = "Graphite.Prefilter" };
            pipeline.SetPasses([created]);
            world.Filters.Add((pipeline, created));
        }
        (RenderPipeline filter, PrefilterPass pass) = world.Filters[world.FiltersUsed++];
        WorldReflections reflections = world.World.Reflections;
        pass.Source = source;
        pass.Destination = target.Cube;
        pass.Readback = bake != null ? reflections.BakeBuffer(target, bake) : null;
        _dispatches.Add(new GraphDispatch(filter, target.Views));
    }

    // Draws the world around a probe into its capture cube, all faces or the next one, then fills its layer
    private void CaptureProbe(WorldFrame world, ProbeCapture work, in FrameTime time)
    {
        WorldReflections reflections = world.World.Reflections;
        ReflectionProbeDesc probe = reflections.Probe(work.Index);
        WorldReflections.Target target = reflections.ProbeTarget(work.Index);
        WorldReflections.Scratch scratch = work.Scratch;
        Float4x4 projection = WorldReflections.FaceProjectionBetween(probe.NearClip, probe.FarClip);
        int faces = work.EndFace - work.FirstFace;
        if (faces == 6)
        {
            CameraFrame camera = Pooled(world.ProbeCameras, ref world.ProbeCamerasUsed);
            ViewDesc capture = new()
            {
                World = world.World,
                ViewId = camera.View.ViewId,
                LayerCount = 6,
                Target = scratch.All,
                CullingMask = probe.CullingMask,
                ClearColor = world.World.Lighting.Environment.SkyColor,
                Near = probe.NearClip,
                FarDistance = probe.FarClip,
            };
            for (int face = 0; face < 6; face++)
                capture.Layers[face] = new ViewLayer { View = WorldReflections.FaceView(face), Projection = projection, Position = probe.Position };
            Draw(camera, capture, world, time);
        }
        else if (faces == 1)
        {
            CameraFrame camera = Pooled(world.ProbeFaceCameras, ref world.ProbeFaceCamerasUsed);
            ViewDesc capture = new()
            {
                World = world.World,
                ViewId = camera.View.ViewId,
                Target = scratch.Faces[work.FirstFace],
                CullingMask = probe.CullingMask,
                ClearColor = world.World.Lighting.Environment.SkyColor,
                Near = probe.NearClip,
                FarDistance = probe.FarClip,
            };
            capture.Layers[0] = new ViewLayer { View = WorldReflections.FaceView(work.FirstFace), Projection = projection, Position = probe.Position };
            Draw(camera, capture, world, time);
        }

        if (work.Prefilter)
            Fill(world, target, scratch.Cube, work.Bake);
    }

    private CameraFrame Capturing()
    {
        CameraFrame camera = new(_device, this, WorldFrame.NextCaptureViewId());
        _textures.TrackView(camera.ViewTable);
        return camera;
    }

    // The next capture camera of a pool this frame, a new one when every one is in use
    private CameraFrame Pooled(List<CameraFrame> pool, ref int used)
    {
        if (used == pool.Count)
            pool.Add(Capturing());
        return pool[used++];
    }

    /// <summary>Releases what the frame pipeline keeps for a removed world. Render thread idle.</summary>
    public void ForgetWorld(WorldFrame world)
    {
        if (world.SkyProgram is ushort program)
            DrawPrograms.Release(program);
        world.SkyProgram = null;
        foreach (CameraFrame camera in world.Captures())
            _textures.Untrack(camera.ViewTable);
    }

    // Asks for every call's program before recording, so compiles start early and deterministic ones finish here.
    private void ResolvePrograms()
    {
        foreach (WorldFrame world in _frameWorlds)
        {
            if (world.SkyProgram is ushort sky)
                Programs.Get(sky, false, world.World.Globals.KeywordSnapshot);
        }
        foreach (CameraFrame camera in _cameras.Values)
        {
            if (camera.LastFrame != _frame)
                continue;
            foreach (DrawList list in camera.Lists)
            {
                foreach (DrawCall call in list.Calls)
                    Programs.Get(call.Program, call.Mirrored != camera.Mirrored, camera.World.World.Globals.KeywordSnapshot);
            }
        }
    }

    private void Prepare(CameraFrame camera, in ViewDesc view, WorldFrame world, in FrameTime time)
    {
        int layers = view.LayerCount;
        CullFrustum frustum = new(view.Layers, layers);
        uint lightRegions = LightTreeBuilder.Invalid, decalRegions = LightTreeBuilder.Invalid, viewLights = LightTreeBuilder.Invalid;
        uint viewDecals = LightTreeBuilder.Invalid, probeRegions = LightTreeBuilder.Invalid;
        if (view.CullingMask == 0)
        {
            camera.Prepass.Clear();
            camera.Forward.Clear();
            camera.Transparent.Clear();
        }
        else
        {
            world.Buckets.Cull(frustum, view.CullingMask);
            world.Buckets.Build(0, frustum, camera.Prepass);
            world.Buckets.Build(1, frustum, camera.Forward);
            world.Buckets.BuildTransparent(frustum, camera.Transparent);

            WorldLighting lighting = world.World.Lighting;
            lightRegions = lighting.BuildViewLightRegions(frustum, camera.LightRegionTree, _uploader, camera.LightRegions, camera.LightRegionNodes);
            decalRegions = lighting.BuildViewDecalRegions(frustum, camera.DecalRegionTree, _uploader, camera.DecalRegions, camera.DecalRegionNodes);
            viewLights = lighting.BuildViewLights(frustum, camera.LightTree, _uploader, camera.ViewLights, camera.ViewLightNodes);
            viewDecals = lighting.BuildViewDecals(frustum, camera.DecalTree, _uploader, camera.ViewDecals, camera.ViewDecalNodes);
            probeRegions = world.World.Reflections.Regions.BuildView(frustum, camera.ProbeRegionTree, _uploader, camera.ProbeRegions, camera.ProbeRegionNodes);
        }
        ViewLayer first = view.Layers[0];
        float handedness = Determinant3(first.View) * first.Projection.c0.X * first.Projection.c1.Y;
        camera.Mirrored = handedness < 0;

        for (int l = 0; l < layers; l++)
        {
            ViewLayer layer = view.Layers[l];
            ViewBlock block = ViewBlock.Create(layer.View, layer.Projection, layer.Position, view.Target.Width, view.Target.Height, view.Near, view.FarDistance);
            float timeHigh = (float)time.Time;
            block.Time = new Float4(timeHigh, time.DeltaTime, time.FrameIndex, (float)(time.Time - timeHigh));
            Float3 offset = frustum.Relative(layer.Position);
            block.TreeOffset = new Float4(offset.X, offset.Y, offset.Z, 0);
            block.LightRegionRoot = lightRegions;
            block.DecalRegionRoot = decalRegions;
            block.ViewLightRoot = viewLights;
            block.ViewDecalRoot = viewDecals;
            block.ProbeRegionRoot = probeRegions;
            if (camera.HasPrevious)
            {
                block.PreviousViewProjection = camera.Previous[l].ViewProjection;
                block.PreviousCameraHigh = camera.Previous[l].CameraHigh;
                block.PreviousCameraLow = camera.Previous[l].CameraLow;
            }
            camera.Previous[l] = block;
            camera.Blocks[l] = block;
        }
        camera.HasPrevious = true;
        camera.Upload(_uploader, camera.Blocks.AsSpan(0, layers));
    }

    // The hosts' views drawn this frame and every capture view, drawn this frame or not
    private IEnumerable<CameraFrame> Active()
    {
        foreach (CameraFrame camera in _cameras.Values)
        {
            if (camera.LastFrame == _frame)
                yield return camera;
        }
        foreach (CameraFrame camera in _captures)
            yield return camera;
    }

    private static float Determinant3(in Float4x4 m)
        => m.c0.X * (m.c1.Y * m.c2.Z - m.c2.Y * m.c1.Z)
         - m.c1.X * (m.c0.Y * m.c2.Z - m.c2.Y * m.c0.Z)
         + m.c2.X * (m.c0.Y * m.c1.Z - m.c1.Y * m.c0.Z);

    private void CollectBuffers(IEnumerable<WorldFrame> worlds)
    {
        _captures.Clear();
        foreach (WorldFrame world in worlds)
            _captures.AddRange(world.Captures());
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
        _probes.Worlds.Clear();
        foreach (WorldFrame world in worlds)
        {
            RenderWorld.RenderSide render = world.World.Render;
            WorldProbes probes = world.World.Probes;
            _probes.Worlds.Add(probes);
            _buffers.Add(probes.ProbeRecords.Buffer);
            _buffers.Add(probes.TetRecords.Buffer);
            _buffers.Add(probes.Requests.Buffer);
            _buffers.Add(probes.ObjectSh);
            StaticRegions<ReflectionProbeRecord> reflections = world.World.Reflections.Regions;
            _buffers.Add(reflections.Records.Buffer);
            _buffers.Add(reflections.Nodes.Buffer);
            _buffers.Add(world.World.Reflections.SkyAmbient);

            foreach (GpuTable table in render.Tables)
                _buffers.Add(table.Buffer);
            WorldLighting lighting = world.World.Lighting;
            foreach (GpuTable table in lighting.Tables)
                _buffers.Add(table.Buffer);

        }
        foreach (CameraFrame camera in Active())
            camera.AddBuffers(_buffers);

        if (Same(Buffers, _buffers))
            return;

        Buffers = [.. _buffers];
        _upload.Buffers = Buffers;
        _probes.Buffers = Buffers;
        _frameStart.SetPasses([_upload, _probes]);
        foreach (CameraFrame camera in _cameras.Values)
            camera.Pipeline.SetPasses([camera.Scene, camera.Present]);
        foreach (CameraFrame camera in _captures)
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
            if (_frame - camera.LastFrame > StaleViewFrames)
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
        _probes.Dispose();
        Programs.Dispose();
        PresentProgram.Dispose();
        PresentLayersProgram.Dispose();
        PrefilterProgram.Dispose();
        SkyAmbientProgram.Dispose();
        TrilinearClamp.Dispose();
        LinearSampler.Dispose();
    }
}
