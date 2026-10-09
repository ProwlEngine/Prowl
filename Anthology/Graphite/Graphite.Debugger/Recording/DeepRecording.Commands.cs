using System;
using System.Collections.Generic;
using System.Linq;
using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;
using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

namespace Prowl.Graphite.Debugger;

public sealed partial class DeepRecording
{
    private readonly Dictionary<ResourceId, ResourceBuilder> _resources = new();
    private readonly Dictionary<ProgramKey, RecordedProgram> _programs = new();
    private readonly List<SamplerDescription> _samplers = new();
    private PassState? _pass;
    private uint _nextTraceId;

    void ICommandStreamProfiler.BeginPassCommands(in PassInfo pass)
        => _pass = _views.TryGetValue(pass.ViewIndex, out ViewState? view) ? view.Pass(pass.Index) : null;

    void ICommandStreamProfiler.EndPassCommands(in PassInfo pass) => _pass = null;

    void ICommandStreamProfiler.SetFramebuffer(in FramebufferInfo framebuffer, in TargetLoadStoreOps ops)
    {
        if (_pass is not { } pass)
            return;

        RecordedAttachment[] colors = new RecordedAttachment[framebuffer.Colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            AttachmentUse use = framebuffer.Colors[i];
            colors[i] = Attachment(use);
            pass.Written.Add(use.Texture.Resource);
        }

        RecordedAttachment? depth = null;
        if (framebuffer.Depth is { } depthUse)
        {
            depth = Attachment(depthUse);
            pass.Written.Add(depthUse.Texture.Resource);
        }

        pass.Commands.Add(new SetFramebufferCommand(colors.ToEquatableArray(), depth, framebuffer.Outputs, framebuffer.Width, framebuffer.Height, ops));
    }

    void ICommandStreamProfiler.ClearColorTarget(uint index, Color color) => Add(new ClearColorTargetCommand(index, color));

    void ICommandStreamProfiler.ClearDepthStencil(float depth, byte stencil) => Add(new ClearDepthStencilCommand(depth, stencil));

    void ICommandStreamProfiler.SetPipeline(in PipelineBindInfo pipeline)
        => Add(new SetPipelineCommand(EnsureProgram(pipeline.Program), pipeline.IsCompute, pipeline.Outputs, pipeline.Topology));

    void ICommandStreamProfiler.SetViewport(in Viewport viewport) => Add(new SetViewportCommand(viewport));

    void ICommandStreamProfiler.SetScissor(uint x, uint y, uint width, uint height) => Add(new SetScissorCommand(x, y, width, height));

    void ICommandStreamProfiler.SetStencilReference(uint reference) => Add(new SetStencilReferenceCommand(reference));

    void ICommandStreamProfiler.SetBlendConstants(Color constants) => Add(new SetBlendConstantsCommand(constants));

    void ICommandStreamProfiler.BindVertexBuffers(ReadOnlySpan<VertexBindingUse> bindings)
    {
        if (_pass is not { } pass)
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

    void ICommandStreamProfiler.BindIndexBuffer(in IndexBindingUse binding)
    {
        if (_pass is not { } pass)
            return;

        BindIndexBufferCommand command = new(Trace(binding.Buffer), binding.Format, binding.IndexCount);
        if (pass.Index != command)
            pass.Commands.Add(command);

        pass.Index = command;
    }

    void ICommandStreamProfiler.SetProperties(ReadOnlySpan<PropertyState> properties)
    {
        if (_pass is not { } pass)
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

    void ICommandStreamProfiler.Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
        => Add(new DrawCommand(vertexCount, instanceCount, firstVertex, firstInstance));

    void ICommandStreamProfiler.DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
        => Add(new DrawIndexedCommand(instanceCount, firstIndex, vertexOffset, firstInstance));

    void ICommandStreamProfiler.DrawIndirect(in ResourceVersion buffer, uint offset, uint drawCount, uint stride)
        => Add(new DrawIndirectCommand(Trace(buffer), offset, drawCount, stride));

    void ICommandStreamProfiler.DrawIndexedIndirect(in ResourceVersion buffer, uint offset, uint drawCount, uint stride)
        => Add(new DrawIndexedIndirectCommand(Trace(buffer), offset, drawCount, stride));

    void ICommandStreamProfiler.Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
        => Add(new DispatchCommand(groupCountX, groupCountY, groupCountZ));

    void ICommandStreamProfiler.DispatchIndirect(in ResourceVersion buffer, uint offset)
        => Add(new DispatchIndirectCommand(Trace(buffer), offset));

    void ICommandStreamProfiler.UpdateBuffer(in ResourceVersion after, uint offset, ReadOnlySpan<byte> data)
    {
        Written(after);
        Add(new UpdateBufferCommand(Trace(after), offset, _store.Put(data)));
    }

    void ICommandStreamProfiler.UpdateTexture(in ResourceVersion after, in TextureRegion region, ReadOnlySpan<byte> data)
    {
        Written(after);
        Add(new UpdateTextureCommand(Trace(after), region, _store.Put(data)));
    }

    void ICommandStreamProfiler.CopyBuffer(in ResourceVersion source, uint sourceOffset, in ResourceVersion destinationAfter, uint destinationOffset, uint sizeInBytes)
    {
        Written(destinationAfter);
        Add(new CopyBufferCommand(Trace(source), sourceOffset, Trace(destinationAfter), destinationOffset, sizeInBytes));
    }

    void ICommandStreamProfiler.CopyTexture(in ResourceVersion source, in TextureRegion sourceRegion, in ResourceVersion destinationAfter, in TextureRegion destinationRegion, uint layerCount)
    {
        Written(destinationAfter);
        Add(new CopyTextureCommand(Trace(source), sourceRegion, Trace(destinationAfter), destinationRegion, layerCount));
    }

    void ICommandStreamProfiler.CopyTextureToBuffer(in ResourceVersion source, in TextureRegion region, in ResourceVersion destinationAfter, uint destinationOffset)
    {
        Written(destinationAfter);
        Add(new CopyTextureToBufferCommand(Trace(source), region, Trace(destinationAfter), destinationOffset));
    }

    void ICommandStreamProfiler.ResolveTexture(in ResourceVersion source, in ResourceVersion destinationAfter)
    {
        Written(destinationAfter);
        Add(new ResolveTextureCommand(Trace(source), Trace(destinationAfter)));
    }

    void ICommandStreamProfiler.GenerateMips(in ResourceVersion textureAfter)
    {
        Written(textureAfter);
        Add(new GenerateMipsCommand(Trace(textureAfter)));
    }

    private void Add(RecordedCommand command) => _pass?.Commands.Add(command);

    private void Written(in ResourceVersion version) => _pass?.Written.Add(version.Resource);

    private TraceResourceId Trace(ResourceId id)
        => id.Value == 0 ? default : Builder(id).Id;

    private TraceVersion Trace(in ResourceVersion version) => new(Trace(version.Resource), version.Version);

    private RecordedAttachment Attachment(in AttachmentUse use) => new(Trace(use.Texture), use.MipLevel, use.ArrayLayer);

    private ResourceBuilder Builder(ResourceId id)
    {
        if (!_resources.TryGetValue(id, out ResourceBuilder? builder))
            _resources[id] = builder = new ResourceBuilder(new TraceResourceId(++_nextTraceId));
        return builder;
    }

    private RecordedProperty Property(in PropertyState state)
    {
        int sampler = -1;
        if (state.Sampler is { } description)
        {
            sampler = _samplers.IndexOf(description);
            if (sampler < 0)
            {
                sampler = _samplers.Count;
                _samplers.Add(description);
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

        return key;
    }

    private RecordedStage Stage(ShaderStageDescription stage) => new(stage.Stage, stage.EntryPoint, _store.Put(stage.ShaderBytes));

    private sealed record PendingCopy(TraceVersion Version, CopyPlacement Placement, CaptureCopy Copy);

    private sealed class ViewState(string name, int index, uint pixelWidth, uint pixelHeight)
    {
        public readonly string Name = name;
        public readonly int Index = index;
        public readonly uint PixelWidth = pixelWidth;
        public readonly uint PixelHeight = pixelHeight;
        public readonly List<RecordedGraphResource> Resources = new();
        public readonly Dictionary<ResourceId, GraphResourceOrigin> Origins = new();
        public readonly HashSet<(ResourceId, uint)> Known = new();
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
        public readonly HashSet<ResourceId> Written = new();
        public readonly List<RecordedCommand> Commands = new();
        public readonly Dictionary<string, RecordedProperty> Properties = new();
        public readonly Dictionary<uint, RecordedVertexBinding> Vertex = new();
        public int VertexCount;
        public BindIndexBufferCommand? Index;
        public RecordedReference[] References = [];
        public readonly List<PendingCopy> Copies = new();
        public string? NotReplayable;
        public EquatableArray<RecordedCopy> Recorded = EquatableArray<RecordedCopy>.Empty;
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
