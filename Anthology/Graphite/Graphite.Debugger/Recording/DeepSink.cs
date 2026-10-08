using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Prowl.Graphite.Debugger.Data;
using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;
using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

namespace Prowl.Graphite.Debugger;

internal sealed partial class DeepSink : IGraphProfiler, IGpuStatsProfiler, ICaptureProfiler, ICommandStreamProfiler
{
    private readonly RecordingSink _light = new();
    private readonly ContentStore _store = new();
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<ResourceId, ResourceBuilder> _resources = new();
    private readonly Dictionary<ProgramKey, RecordedProgram> _programs = new();
    private readonly List<SamplerDescription> _samplers = new();
    private readonly SortedDictionary<ulong, ExecutionState> _executions = new();
    private readonly ThreadLocal<ThreadState> _thread = new(() => new ThreadState());
    private uint _nextTraceId;

    public DeepSink(DeepMode mode)
    {
        Mode = mode;
    }

    public DeepMode Mode { get; }

    public RecordingSink Light => _light;

    public void Close() => _light.Close();

    public void BeginView(in ViewInfo view) => _light.BeginView(in view);

    public void EndView(in ViewInfo view) => _light.EndView(in view);

    public void BeginPass(in PassInfo pass) => _light.BeginPass(in pass);

    public void EndPass(in PassInfo pass, in PassStats stats) => _light.EndPass(in pass, in stats);

    public void RecordPassRead(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer) { }

    public void RecordPassWrite(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer) { }

    public void RecordExecutionTime(in CommandBufferInfo commandBuffer, bool isTransfer, double milliseconds)
        => _light.RecordExecutionTime(in commandBuffer, isTransfer, milliseconds);

    public void RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats)
        => _light.RecordGpuVertexStats(in commandBuffer, in stats);

    public void RecordExecutionResolved(ulong executionId) => _light.RecordExecutionResolved(executionId);

    public void OnViewBegin(in ViewCaptureInfo view)
    {
        ThreadState thread = _thread.Value!;
        thread.View = null;
        thread.Pass = null;
        if (!_light.Contains(view.ExecutionId))
            return;

        ExecutionState execution;
        lock (_gate)
        {
            if (!_executions.TryGetValue(view.ExecutionId, out execution!))
                _executions[view.ExecutionId] = execution = new ExecutionState();
        }

        ViewState state = new(view.ViewName, view.ViewIndex, view.PixelWidth, view.PixelHeight, execution);
        Dictionary<RenderResourceID, GraphResourceInfo> byName = new();
        foreach (GraphResourceInfo info in view.Resources.Span)
        {
            byName[info.Id] = info;
            GraphBacking[] backings = info.Backings.ToArray();
            foreach (GraphBacking backing in backings)
                state.Origins[backing.Id] = info.Origin;

            state.Resources.Add(new RecordedGraphResource(
                info.Name,
                info.Kind,
                info.Origin,
                backings.Select(b => new RecordedBacking(Trace(b.Id), b.EntryVersion.Version, b.Role, b.Index)).ToEquatableArray(),
                info.Texture,
                info.Buffer));
        }

        foreach (PassCaptureInfo passInfo in view.Passes.Span)
        {
            PassState pass = new(passInfo.Pass.Name, passInfo.Pass.Index);
            foreach (PassResourceAccess access in passInfo.Accesses.Span)
            {
                pass.Accesses.Add(new RecordedAccess(
                    RenderResourceID.ToString(access.Id) ?? access.Id.ToString(),
                    access.Kind,
                    access.IsOutput,
                    access.TextureUsage,
                    access.DepthUsage,
                    access.BufferUsage));

                if (!byName.TryGetValue(access.Id, out GraphResourceInfo resource))
                    continue;

                foreach (GraphBacking backing in resource.Backings.Span)
                {
                    pass.Declared.Add(backing.Id);
                    if (access.IsOutput)
                        pass.Outputs.Add(backing.Id);
                    else
                        pass.Inputs.Add(backing.Id);
                }
            }

            state.Passes[passInfo.Pass.Index] = pass;
        }

        lock (_gate)
            execution.Views[view.ViewIndex] = state;

        thread.View = state;
    }

    public void OnViewEnd()
    {
        ThreadState thread = _thread.Value!;
        thread.View = null;
        thread.Pass = null;
    }

    public void OnExecutionSubmitted(ExecutionTask task) { }

    public void BeginPassCommands(in PassInfo pass)
    {
        ThreadState thread = _thread.Value!;
        thread.Pass = thread.View?.Pass(pass.Index);
    }

    public void EndPassCommands(in PassInfo pass) => _thread.Value!.Pass = null;

    public void SetFramebuffer(in FramebufferInfo framebuffer, in TargetLoadStoreOps ops)
    {
        if (_thread.Value!.Pass is not { } pass)
            return;

        RecordedAttachment[] colors = new RecordedAttachment[framebuffer.Colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            AttachmentUse use = framebuffer.Colors[i];
            colors[i] = Attachment(use);
            pass.Written.Add(use.Texture.Resource);
            pass.Attachments.Add(use.Texture.Resource);
            if (ops.Color.Load == LoadAction.Load)
                pass.Loaded.Add(use.Texture.Resource);
        }

        RecordedAttachment? depth = null;
        if (framebuffer.Depth is { } depthUse)
        {
            depth = Attachment(depthUse);
            pass.Written.Add(depthUse.Texture.Resource);
            pass.Attachments.Add(depthUse.Texture.Resource);
            if (ops.Depth.Load == LoadAction.Load)
                pass.Loaded.Add(depthUse.Texture.Resource);
        }

        pass.Commands.Add(new SetFramebufferCommand(colors.ToEquatableArray(), depth, framebuffer.Outputs, framebuffer.Width, framebuffer.Height, ops));
    }

    public void ClearColorTarget(uint index, Color color) => Add(new ClearColorTargetCommand(index, color));

    public void ClearDepthStencil(float depth, byte stencil) => Add(new ClearDepthStencilCommand(depth, stencil));

    public void SetPipeline(in PipelineBindInfo pipeline)
        => Add(new SetPipelineCommand(EnsureProgram(pipeline.Program), pipeline.IsCompute, pipeline.Outputs, pipeline.Topology));

    public void SetViewport(in Viewport viewport) => Add(new SetViewportCommand(viewport));

    public void SetScissor(uint x, uint y, uint width, uint height) => Add(new SetScissorCommand(x, y, width, height));

    public void SetStencilReference(uint reference) => Add(new SetStencilReferenceCommand(reference));

    public void SetBlendConstants(Color constants) => Add(new SetBlendConstantsCommand(constants));

    public void BindVertexBuffers(ReadOnlySpan<VertexBindingUse> bindings)
    {
        if (_thread.Value!.Pass is not { } pass)
            return;

        List<RecordedVertexBinding> changed = new();
        foreach (VertexBindingUse use in bindings)
        {
            RecordedVertexBinding binding = new(use.Slot, Trace(use.Buffer), use.Offset, use.Stride);
            if (!pass.Vertex.TryGetValue(use.Slot, out RecordedVertexBinding? previous) || previous != binding)
                changed.Add(binding);

            pass.Vertex[use.Slot] = binding;
        }

        if (changed.Count > 0 || pass.VertexCount != bindings.Length)
            pass.Commands.Add(new BindVertexBuffersCommand(changed.ToEquatableArray(), bindings.Length));

        pass.VertexCount = bindings.Length;
    }

    public void BindIndexBuffer(in IndexBindingUse binding)
    {
        if (_thread.Value!.Pass is not { } pass)
            return;

        BindIndexBufferCommand command = new(Trace(binding.Buffer), binding.Format, binding.IndexCount);
        if (pass.Index != command)
            pass.Commands.Add(command);

        pass.Index = command;
    }

    public void SetProperties(ReadOnlySpan<PropertyState> properties)
    {
        if (_thread.Value!.Pass is not { } pass)
            return;

        List<RecordedProperty> changed = new();
        HashSet<string> present = new();
        foreach (PropertyState state in properties)
        {
            RecordedProperty property = Property(state);
            present.Add(property.Name);
            if (!pass.Properties.TryGetValue(property.Name, out RecordedProperty? previous) || previous != property)
                changed.Add(property);

            pass.Properties[property.Name] = property;
        }

        string[] removed = pass.Properties.Keys.Where(name => !present.Contains(name)).ToArray();
        foreach (string name in removed)
            pass.Properties.Remove(name);

        if (changed.Count > 0 || removed.Length > 0)
            pass.Commands.Add(new SetPropertiesCommand(changed.ToEquatableArray(), removed.ToEquatableArray()));
    }

    public void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
        => Add(new DrawCommand(vertexCount, instanceCount, firstVertex, firstInstance));

    public void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
        => Add(new DrawIndexedCommand(indexCount, instanceCount, firstIndex, vertexOffset, firstInstance));

    public void DrawIndirect(in ResourceVersion buffer, uint offset, uint drawCount, uint stride)
        => Add(new DrawIndirectCommand(Trace(buffer), offset, drawCount, stride));

    public void DrawIndexedIndirect(in ResourceVersion buffer, uint offset, uint drawCount, uint stride)
        => Add(new DrawIndexedIndirectCommand(Trace(buffer), offset, drawCount, stride));

    public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
        => Add(new DispatchCommand(groupCountX, groupCountY, groupCountZ));

    public void DispatchIndirect(in ResourceVersion buffer, uint offset)
        => Add(new DispatchIndirectCommand(Trace(buffer), offset));

    public void UpdateBuffer(in ResourceVersion after, uint offset, ReadOnlySpan<byte> data)
    {
        Written(after);
        Add(new UpdateBufferCommand(Trace(after), offset, _store.Put(data)));
    }

    public void UpdateTexture(in ResourceVersion after, in TextureRegion region, ReadOnlySpan<byte> data)
    {
        Written(after);
        Add(new UpdateTextureCommand(Trace(after), region, _store.Put(data)));
    }

    public void CopyBuffer(in ResourceVersion source, uint sourceOffset, in ResourceVersion destinationAfter, uint destinationOffset, uint sizeInBytes)
    {
        Written(destinationAfter);
        Add(new CopyBufferCommand(Trace(source), sourceOffset, Trace(destinationAfter), destinationOffset, sizeInBytes));
    }

    public void CopyTexture(in ResourceVersion source, in TextureRegion sourceRegion, in ResourceVersion destinationAfter, in TextureRegion destinationRegion, uint layerCount)
    {
        Written(destinationAfter);
        Add(new CopyTextureCommand(Trace(source), sourceRegion, Trace(destinationAfter), destinationRegion, layerCount));
    }

    public void CopyTextureToBuffer(in ResourceVersion source, in TextureRegion region, in ResourceVersion destinationAfter, uint destinationOffset)
    {
        Written(destinationAfter);
        Add(new CopyTextureToBufferCommand(Trace(source), region, Trace(destinationAfter), destinationOffset));
    }

    public void ResolveTexture(in ResourceVersion source, in ResourceVersion destinationAfter)
    {
        Written(destinationAfter);
        Add(new ResolveTextureCommand(Trace(source), Trace(destinationAfter)));
    }

    public void GenerateMips(in ResourceVersion textureAfter)
    {
        Written(textureAfter);
        Add(new GenerateMipsCommand(Trace(textureAfter)));
    }

    private void Add(RecordedCommand command) => _thread.Value!.Pass?.Commands.Add(command);

    private void Written(in ResourceVersion version) => _thread.Value!.Pass?.Written.Add(version.Resource);

    private TraceResourceId Trace(ResourceId id)
        => id.Value == 0 ? default : Builder(id).Id;

    private TraceVersion Trace(in ResourceVersion version) => new(Trace(version.Resource), version.Version);

    private RecordedAttachment Attachment(in AttachmentUse use) => new(Trace(use.Texture), use.MipLevel, use.ArrayLayer);

    private ResourceBuilder Builder(ResourceId id)
        => _resources.GetOrAdd(id, _ => new ResourceBuilder(new TraceResourceId(Interlocked.Increment(ref _nextTraceId))));

    private RecordedProperty Property(in PropertyState state)
    {
        int sampler = -1;
        if (state.Sampler is { } description)
        {
            lock (_gate)
            {
                sampler = _samplers.IndexOf(description);
                if (sampler < 0)
                {
                    sampler = _samplers.Count;
                    _samplers.Add(description);
                }
            }
        }

        return new RecordedProperty(
            PropertyID.ToString(state.Name) ?? state.Name.ToString(),
            state.Kind,
            state.UniformType,
            state.Kind == PropertyKind.Uniform ? EquatableArray.Create<byte>(state.Uniform.AsBytes()) : EquatableArray<byte>.Empty,
            Trace(state.Resource),
            state.Range,
            state.ViewFormat,
            sampler);
    }

    private ProgramKey EnsureProgram(ShaderProgram program)
    {
        ProgramKey key = program.Key;
        lock (_gate)
        {
            if (_programs.ContainsKey(key))
                return key;

            _programs[key] = program switch
            {
                GraphicsProgram graphics => new RecordedProgram(
                    key,
                    false,
                    graphics.StageDescriptions.Select(Stage).ToEquatableArray(),
                    program.ResourceLayouts.ToEquatableArray(),
                    graphics.BlendState,
                    graphics.DepthStencilState,
                    graphics.RasterizerState,
                    graphics.VertexLayouts.ToEquatableArray(),
                    0,
                    0,
                    0),
                ComputeProgram compute => new RecordedProgram(
                    key,
                    true,
                    EquatableArray.Create(Stage(compute.StageDescription)),
                    program.ResourceLayouts.ToEquatableArray(),
                    null,
                    null,
                    null,
                    EquatableArray<VertexLayoutDescription>.Empty,
                    compute.ThreadGroupSizeX,
                    compute.ThreadGroupSizeY,
                    compute.ThreadGroupSizeZ),
                _ => throw new NotSupportedException($"Unknown program type {program.GetType().Name}."),
            };
        }

        return key;
    }

    private RecordedStage Stage(ShaderStageDescription stage) => new(stage.Stage, stage.EntryPoint, _store.Put(stage.ShaderBytes));

    private sealed class ThreadState
    {
        public ViewState? View;
        public PassState? Pass;
    }

    private sealed class ExecutionState
    {
        public readonly SortedDictionary<int, ViewState> Views = new();
        public readonly HashSet<(ResourceId, uint)> Known = new();
    }

    private sealed class ViewState(string name, int index, uint pixelWidth, uint pixelHeight, ExecutionState execution)
    {
        public readonly string Name = name;
        public readonly int Index = index;
        public readonly uint PixelWidth = pixelWidth;
        public readonly uint PixelHeight = pixelHeight;
        public readonly ExecutionState Execution = execution;
        public readonly List<RecordedGraphResource> Resources = new();
        public readonly Dictionary<ResourceId, GraphResourceOrigin> Origins = new();
        public readonly SortedDictionary<int, PassState> Passes = new();

        public PassState Pass(int index)
        {
            if (!Passes.TryGetValue(index, out PassState? pass))
                Passes[index] = pass = new PassState("", index);
            return pass;
        }
    }

    private sealed class PassState(string name, int index)
    {
        public readonly string Name = name;
        public readonly int PassIndex = index;
        public readonly List<RecordedAccess> Accesses = new();
        public readonly HashSet<ResourceId> Declared = new();
        public readonly HashSet<ResourceId> Outputs = new();
        public readonly HashSet<ResourceId> Inputs = new();
        public readonly HashSet<ResourceId> Attachments = new();
        public readonly HashSet<ResourceId> Loaded = new();
        public readonly HashSet<ResourceId> Written = new();
        public readonly List<RecordedCommand> Commands = new();
        public readonly Dictionary<string, RecordedProperty> Properties = new();
        public readonly Dictionary<uint, RecordedVertexBinding> Vertex = new();
        public int VertexCount;
        public BindIndexBufferCommand? Index;
        public RecordedReference[] References = [];
        public string? NotReplayable;
    }

    private sealed class ResourceBuilder(TraceResourceId id)
    {
        public readonly TraceResourceId Id = id;
        public string Name = "";
        public GraphResourceKind Kind;
        public ResourceOrigin Origin;
        public TextureDescription? Texture;
        public BufferDescription? Buffer;
        public bool Described;
    }
}
