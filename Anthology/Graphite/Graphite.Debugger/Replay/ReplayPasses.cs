using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;
using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

namespace Prowl.Graphite.Debugger;

internal sealed class ReplayRestorePass(ReplayScope scope, IReadOnlyList<DeepPass> passes, int step) : IPass
{
    private readonly Dictionary<string, BufferHandle> _handles = new();

    public string Name => $"Replay restore {step}";

    private IEnumerable<BufferRestore> Restores => scope.BufferRestores.Where(r => r.PassIndex == step);

    public void Setup(RenderContextBuilder builder)
    {
        _handles.Clear();
        HashSet<string> declared = new();
        if (step == passes[0].Index)
        {
            HashSet<string> produced = new();
            foreach (DeepPass pass in passes)
            {
                foreach (RecordedAccess access in pass.Accesses.Where(a => !a.IsOutput && !produced.Contains(a.Resource)))
                    Import(builder, access.Resource, declared);

                produced.UnionWith(pass.Accesses.Where(a => a.IsOutput).Select(a => a.Resource));
            }
        }

        foreach ((string name, _) in scope.StepTextures(step))
            Import(builder, name, declared);

        foreach (BufferRestore restore in Restores)
            Import(builder, restore.Name, declared);
    }

    public unsafe void Render(RenderContext context, CommandBuffer cmd)
    {
        foreach (BufferRestore restore in Restores)
        {
            DeviceBuffer buffer = scope.BindBuffer(scope.GraphBuffers[restore.Name].Id, context.GetRenderBuffer(_handles[restore.Name]));
            fixed (byte* source = restore.Data)
                cmd.UpdateBuffer(buffer, 0, (IntPtr)source, Math.Min((uint)restore.Data.Length, buffer.SizeInBytes));
        }

        scope.RestoreStep(cmd, step);
    }

    private void Import(RenderContextBuilder builder, string name, HashSet<string> declared)
    {
        if (!declared.Add(name))
            return;

        if (scope.GraphTextures.TryGetValue(name, out GraphTexture? texture))
            builder.DeclareImportedTexture(RenderResourceID.Intern(name), texture.Texture, TextureState.TransferDst);
        else if (scope.GraphBuffers.TryGetValue(name, out GraphBuffer? buffer))
            _handles[name] = builder.DeclareOutputBuffer(RenderResourceID.Intern(name), buffer.Desc, 0, BufferAccess.TransferWrite);
    }
}

internal sealed unsafe class ReplayPass(string name, ReplayScope scope, DeepRecording recording, DeepPass pass, int? lastEvent = null) : IPass
{
    private readonly Dictionary<string, BufferHandle> _handles = new();
    private readonly Dictionary<string, RecordedProperty> _properties = new();
    private readonly Dictionary<uint, RecordedVertexBinding> _vertex = new();
    private int _vertexCount;
    private BindIndexBufferCommand? _index;
    private SetPipelineCommand? _pipeline;
    private bool _sourceDirty;

    public string Name => name;

    public void Setup(RenderContextBuilder builder)
    {
        _handles.Clear();
        foreach (RecordedAccess access in pass.Accesses)
        {
            RenderResourceID id = RenderResourceID.Intern(access.Resource);
            if (access.Kind == GraphResourceKind.Texture)
            {
                RenderTexture texture = scope.GraphTextures[access.Resource].Texture;
                if (access.IsOutput)
                    builder.DeclareImportedTexture(id, texture, access.TextureUsage, access.DepthUsage);
                else
                    builder.DeclareInputTexture(id, access.TextureUsage, access.DepthUsage);
            }
            else
            {
                _handles[access.Resource] = access.IsOutput
                    ? builder.DeclareOutputBuffer(id, scope.GraphBuffers[access.Resource].Desc, 0, access.BufferUsage)
                    : builder.DeclareInputBuffer(id, access.BufferUsage);
            }
        }
    }

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        foreach ((string resource, BufferHandle handle) in _handles)
            scope.BindBuffer(scope.GraphBuffers[resource].Id, context.GetRenderBuffer(handle));

        _properties.Clear();
        _vertex.Clear();
        _vertexCount = 0;
        _index = null;
        _pipeline = null;
        _sourceDirty = false;
        int events = -1;
        foreach (RecordedCommand command in pass.Commands)
        {
            Execute(cmd, command);
            if (ReplayEvents.IsEvent(command) && ++events == lastEvent)
                break;
        }
    }

    private void Execute(CommandBuffer cmd, RecordedCommand command)
    {
        switch (command)
        {
            case SetFramebufferCommand framebuffer:
                cmd.SetFramebuffer(scope.Framebuffer(framebuffer), framebuffer.Ops);
                break;
            case ClearColorTargetCommand clear:
                cmd.ClearColorTarget(clear.Index, clear.Color);
                break;
            case ClearDepthStencilCommand clear:
                cmd.ClearDepthStencil(clear.Depth, clear.Stencil);
                break;
            case SetPipelineCommand pipeline:
                _pipeline = pipeline;
                _sourceDirty = !pipeline.IsCompute;
                if (pipeline.IsCompute)
                    cmd.SetComputeShader((ComputeProgram)scope.Program(pipeline.Program));
                else
                    cmd.SetShader((GraphicsProgram)scope.Program(pipeline.Program));
                break;
            case SetViewportCommand viewport:
                cmd.SetViewport(viewport.Viewport);
                break;
            case SetScissorCommand scissor:
                cmd.SetScissor(scissor.X, scissor.Y, scissor.Width, scissor.Height);
                break;
            case SetStencilReferenceCommand stencil:
                cmd.SetStencilReference(stencil.Reference);
                break;
            case SetBlendConstantsCommand blend:
                cmd.SetBlendConstants(blend.Constants);
                break;
            case BindVertexBuffersCommand bind:
                foreach (RecordedVertexBinding binding in bind.Changed)
                    _vertex[binding.Slot] = binding;

                foreach (uint slot in _vertex.Keys.Where(s => s >= bind.Count).ToArray())
                    _vertex.Remove(slot);

                _vertexCount = bind.Count;
                _sourceDirty = true;
                break;
            case BindIndexBufferCommand bind:
                _index = bind;
                _sourceDirty = true;
                break;
            case SetPropertiesCommand properties:
                ApplyProperties(cmd, properties);
                break;
            case DrawCommand draw:
                BindSource(cmd);
                cmd.Draw(draw.VertexCount, draw.InstanceCount, draw.FirstVertex, draw.FirstInstance);
                break;
            case DrawIndexedCommand draw:
                BindSource(cmd);
                cmd.DrawIndexed(draw.InstanceCount, draw.FirstIndex, draw.VertexOffset, draw.FirstInstance);
                break;
            case DrawIndirectCommand draw:
                BindSource(cmd);
                cmd.DrawIndirect(scope.Buffer(draw.Buffer.Resource), draw.Offset, draw.DrawCount, draw.Stride);
                break;
            case DrawIndexedIndirectCommand draw:
                BindSource(cmd);
                cmd.DrawIndexedIndirect(scope.Buffer(draw.Buffer.Resource), draw.Offset, draw.DrawCount, draw.Stride);
                break;
            case DispatchCommand dispatch:
                cmd.Dispatch(dispatch.GroupCountX, dispatch.GroupCountY, dispatch.GroupCountZ);
                break;
            case DispatchIndirectCommand dispatch:
                cmd.DispatchIndirect(scope.Buffer(dispatch.Buffer.Resource), dispatch.Offset);
                break;
            case UpdateBufferCommand update:
                fixed (byte* source = scope.Blob(update.Data))
                    cmd.UpdateBuffer(scope.Buffer(update.After.Resource), update.Offset, (IntPtr)source, (uint)update.Data.Length);
                break;
            case UpdateTextureCommand update:
                fixed (byte* source = scope.Blob(update.Data))
                    cmd.UpdateTexture(scope.Texture(update.After.Resource), (IntPtr)source, (uint)update.Data.Length, update.Region);
                break;
            case CopyBufferCommand copy:
                cmd.CopyBuffer(scope.Buffer(copy.Source.Resource), copy.SourceOffset, scope.Buffer(copy.DestinationAfter.Resource), copy.DestinationOffset, copy.SizeInBytes);
                break;
            case CopyTextureCommand copy:
                cmd.CopyTexture(
                    scope.Texture(copy.Source.Resource),
                    copy.SourceRegion.X, copy.SourceRegion.Y, copy.SourceRegion.Z,
                    copy.SourceRegion.MipLevel, copy.SourceRegion.ArrayLayer,
                    scope.Texture(copy.DestinationAfter.Resource),
                    copy.DestinationRegion.X, copy.DestinationRegion.Y, copy.DestinationRegion.Z,
                    copy.DestinationRegion.MipLevel, copy.DestinationRegion.ArrayLayer,
                    copy.SourceRegion.Width, copy.SourceRegion.Height, copy.SourceRegion.Depth,
                    copy.LayerCount);
                break;
            case CopyTextureToBufferCommand copy:
                cmd.CopyTextureToBuffer(scope.Texture(copy.Source.Resource), scope.Buffer(copy.DestinationAfter.Resource), copy.DestinationOffset, copy.Region);
                break;
            case ResolveTextureCommand resolve:
                cmd.ResolveTexture(scope.Texture(resolve.Source.Resource), scope.Texture(resolve.DestinationAfter.Resource));
                break;
            case GenerateMipsCommand mips:
                cmd.GenerateMipmaps(scope.Texture(mips.TextureAfter.Resource));
                break;
            default:
                throw new NotSupportedException($"Unknown command {command.GetType().Name}.");
        }
    }

    private void BindSource(CommandBuffer cmd)
    {
        if (!_sourceDirty || _pipeline is not { IsCompute: false } pipeline)
            return;

        GraphicsProgram program = (GraphicsProgram)scope.Program(pipeline.Program);
        VertexSource source = new(pipeline.Topology ?? PrimitiveTopology.TriangleList);
        foreach (RecordedVertexBinding binding in _vertex.Values)
            source.SetBuffer(program.VertexLayouts[(int)binding.Slot].Elements[0].Name, scope.Buffer(binding.Buffer.Resource), binding.Offset);

        if (_index is { } index)
            source.SetIndexBuffer(scope.Buffer(index.Buffer.Resource), index.Format, index.IndexCount);

        cmd.SetVertexSource(source);
        _sourceDirty = false;
    }

    private void ApplyProperties(CommandBuffer cmd, SetPropertiesCommand command)
    {
        foreach (string removed in command.Removed)
            _properties.Remove(removed);

        foreach (RecordedProperty property in command.Changed)
            _properties[property.Name] = property;

        PropertySet set = new();
        foreach (RecordedProperty property in _properties.Values)
            Apply(set, property);

        cmd.ClearProperties();
        cmd.SetProperties(set);
    }

    private void Apply(PropertySet set, RecordedProperty property)
    {
        PropertyID name = PropertyID.Intern(property.Name);
        ReadOnlySpan<byte> bytes = property.Uniform.AsSpan();
        switch (property.Kind)
        {
            case PropertyKind.Uniform:
                switch (property.UniformType)
                {
                    case UniformScalarType.Float1: set.SetFloat(name, MemoryMarshal.Read<float>(bytes)); break;
                    case UniformScalarType.Float2: set.SetFloat2(name, MemoryMarshal.Read<Float2>(bytes)); break;
                    case UniformScalarType.Float3: set.SetFloat3(name, MemoryMarshal.Read<Float3>(bytes)); break;
                    case UniformScalarType.Float4: set.SetFloat4(name, MemoryMarshal.Read<Float4>(bytes)); break;
                    case UniformScalarType.Int1: set.SetInt(name, MemoryMarshal.Read<int>(bytes)); break;
                    case UniformScalarType.Int2: set.SetInt2(name, MemoryMarshal.Read<Int2>(bytes)); break;
                    case UniformScalarType.Int3: set.SetInt3(name, MemoryMarshal.Read<Int3>(bytes)); break;
                    case UniformScalarType.Int4: set.SetInt4(name, MemoryMarshal.Read<Int4>(bytes)); break;
                    case UniformScalarType.Float4x4: set.SetMatrix(name, MemoryMarshal.Read<Float4x4>(bytes)); break;
                    default: throw new NotSupportedException($"Uniform type {property.UniformType} cannot be replayed.");
                }

                break;
            case PropertyKind.Buffer:
                set.SetBuffer(name, BufferRange(property));
                break;
            case PropertyKind.UniformBuffer:
                set.SetUniformBuffer(name, BufferRange(property));
                break;
            case PropertyKind.Texture:
                Texture texture = scope.Texture(property.Resource.Resource);
                Sampler? sampler = scope.Sampler(property.Sampler);
                if (NeedsView(texture, property))
                {
                    TextureView view = scope.Own(scope.Factory.CreateTextureView(new TextureViewDescription(
                        texture,
                        property.Range.BaseMipLevel,
                        property.Range.IsTexture ? property.Range.MipLevels : texture.MipLevels,
                        property.Range.BaseArrayLayer,
                        property.Range.IsTexture ? property.Range.ArrayLayers : texture.ArrayLayers,
                        property.ViewFormat)));
                    set.SetTexture(name, view, sampler);
                }
                else
                {
                    set.SetTexture(name, texture, sampler);
                }

                break;
            case PropertyKind.Sampler:
                set.SetSampler(name, scope.Sampler(property.Sampler)!);
                break;
        }
    }

    private DeviceBufferRange BufferRange(RecordedProperty property)
    {
        DeviceBuffer buffer = scope.Buffer(property.Resource.Resource);
        return property.Range.Size == 0
            ? new DeviceBufferRange(buffer, property.Range.Offset, buffer.SizeInBytes - property.Range.Offset)
            : new DeviceBufferRange(buffer, property.Range.Offset, property.Range.Size);
    }

    private static bool NeedsView(Texture texture, RecordedProperty property)
        => property.ViewFormat != null
            || (property.Range.IsTexture
                && (property.Range.BaseMipLevel != 0
                    || property.Range.MipLevels != texture.MipLevels
                    || property.Range.BaseArrayLayer != 0
                    || property.Range.ArrayLayers != texture.ArrayLayers));
}

internal static class ReplayEvents
{
    public static bool IsEvent(RecordedCommand command)
        => command is DrawCommand or DrawIndexedCommand or DrawIndirectCommand or DrawIndexedIndirectCommand
            or DispatchCommand or DispatchIndirectCommand
            or ClearColorTargetCommand or ClearDepthStencilCommand
            or UpdateBufferCommand or UpdateTextureCommand
            or CopyBufferCommand or CopyTextureCommand or CopyTextureToBufferCommand
            or ResolveTextureCommand or GenerateMipsCommand;
}
