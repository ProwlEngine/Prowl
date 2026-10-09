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
    private uint _nextTraceId;

    IPassCommandSink? ICommandStreamProfiler.BeginPassCommands(in PassInfo pass)
        => _views.TryGetValue(pass.ViewIndex, out ViewState? view) ? new PassRecorder(this, view.Pass(pass.Index)) : null;

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
                program.Name,
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
                program.Name,
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

    private sealed class PassRecorder(DeepRecording owner, PassState pass) : IPassCommandSink
    {
        public void End() { }

        public void SetFramebuffer(in FramebufferInfo framebuffer, in TargetLoadStoreOps ops)
        {
            RecordedAttachment[] colors = new RecordedAttachment[framebuffer.Colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                AttachmentUse use = framebuffer.Colors[i];
                colors[i] = owner.Attachment(use);
                pass.Written.Add(use.Texture.Resource);
            }

            RecordedAttachment? depth = null;
            if (framebuffer.Depth is { } depthUse)
            {
                depth = owner.Attachment(depthUse);
                pass.Written.Add(depthUse.Texture.Resource);
            }

            pass.Commands.Add(new SetFramebufferCommand(colors.ToEquatableArray(), depth, framebuffer.Outputs, framebuffer.Width, framebuffer.Height, ops));
        }

        public void ClearColorTarget(uint index, Color color) => pass.Commands.Add(new ClearColorTargetCommand(index, color));

        public void ClearDepthStencil(float depth, byte stencil) => pass.Commands.Add(new ClearDepthStencilCommand(depth, stencil));

        public void SetPipeline(in PipelineBindInfo pipeline)
            => pass.Commands.Add(new SetPipelineCommand(owner.EnsureProgram(pipeline.Program), pipeline.PipelineId, pipeline.IsCompute, pipeline.Outputs, pipeline.Topology));

        public void SetViewport(in Viewport viewport) => pass.Commands.Add(new SetViewportCommand(viewport));

        public void SetScissor(uint x, uint y, uint width, uint height) => pass.Commands.Add(new SetScissorCommand(x, y, width, height));

        public void SetStencilReference(uint reference) => pass.Commands.Add(new SetStencilReferenceCommand(reference));

        public void SetBlendConstants(Color constants) => pass.Commands.Add(new SetBlendConstantsCommand(constants));

        public void BindVertexBuffers(ReadOnlySpan<VertexBindingUse> bindings)
        {
            List<RecordedVertexBinding> changed = new();
            foreach (VertexBindingUse use in bindings)
            {
                RecordedVertexBinding binding = new(use.Slot, owner.Trace(use.Buffer), use.Offset, use.Stride);
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
            BindIndexBufferCommand command = new(owner.Trace(binding.Buffer), binding.Format, binding.IndexCount);
            if (pass.Index != command)
                pass.Commands.Add(command);

            pass.Index = command;
        }

        public void SetProperties(ReadOnlySpan<PropertyState> properties)
        {
            List<RecordedProperty> changed = new();
            HashSet<string> present = new();
            foreach (PropertyState state in properties)
            {
                RecordedProperty property = owner.Property(state);
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
            => pass.Commands.Add(new DrawCommand(vertexCount, instanceCount, firstVertex, firstInstance));

        public void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
            => pass.Commands.Add(new DrawIndexedCommand(instanceCount, firstIndex, vertexOffset, firstInstance));

        public void DrawIndirect(in ResourceVersion buffer, uint offset, uint drawCount, uint stride)
            => pass.Commands.Add(new DrawIndirectCommand(owner.Trace(buffer), offset, drawCount, stride));

        public void DrawIndexedIndirect(in ResourceVersion buffer, uint offset, uint drawCount, uint stride)
            => pass.Commands.Add(new DrawIndexedIndirectCommand(owner.Trace(buffer), offset, drawCount, stride));

        public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
            => pass.Commands.Add(new DispatchCommand(groupCountX, groupCountY, groupCountZ));

        public void DispatchIndirect(in ResourceVersion buffer, uint offset)
            => pass.Commands.Add(new DispatchIndirectCommand(owner.Trace(buffer), offset));

        public void UpdateBuffer(in ResourceVersion after, uint offset, ReadOnlySpan<byte> data)
        {
            pass.Written.Add(after.Resource);
            pass.Commands.Add(new UpdateBufferCommand(owner.Trace(after), offset, owner._store.Put(data)));
        }

        public void UpdateTexture(in ResourceVersion after, in TextureRegion region, ReadOnlySpan<byte> data)
        {
            pass.Written.Add(after.Resource);
            pass.Commands.Add(new UpdateTextureCommand(owner.Trace(after), region, owner._store.Put(data)));
        }

        public void CopyBuffer(in ResourceVersion source, uint sourceOffset, in ResourceVersion destinationAfter, uint destinationOffset, uint sizeInBytes)
        {
            pass.Written.Add(destinationAfter.Resource);
            pass.Commands.Add(new CopyBufferCommand(owner.Trace(source), sourceOffset, owner.Trace(destinationAfter), destinationOffset, sizeInBytes));
        }

        public void CopyTexture(in ResourceVersion source, in TextureRegion sourceRegion, in ResourceVersion destinationAfter, in TextureRegion destinationRegion, uint layerCount)
        {
            pass.Written.Add(destinationAfter.Resource);
            pass.Commands.Add(new CopyTextureCommand(owner.Trace(source), sourceRegion, owner.Trace(destinationAfter), destinationRegion, layerCount));
        }

        public void CopyTextureToBuffer(in ResourceVersion source, in TextureRegion region, in ResourceVersion destinationAfter, uint destinationOffset)
        {
            pass.Written.Add(destinationAfter.Resource);
            pass.Commands.Add(new CopyTextureToBufferCommand(owner.Trace(source), region, owner.Trace(destinationAfter), destinationOffset));
        }

        public void ResolveTexture(in ResourceVersion source, in ResourceVersion destinationAfter)
        {
            pass.Written.Add(destinationAfter.Resource);
            pass.Commands.Add(new ResolveTextureCommand(owner.Trace(source), owner.Trace(destinationAfter)));
        }

        public void GenerateMips(in ResourceVersion textureAfter)
        {
            pass.Written.Add(textureAfter.Resource);
            pass.Commands.Add(new GenerateMipsCommand(owner.Trace(textureAfter)));
        }
    }

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
