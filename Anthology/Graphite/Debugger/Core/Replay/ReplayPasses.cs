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
                cmd.UpdateBuffer(buffer, restore.Offset, (IntPtr)source, Math.Min((uint)restore.Data.Length, buffer.SizeInBytes - restore.Offset));
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

internal sealed unsafe class ReplayPass(string name, ReplayScope scope, DeepPass pass, int? lastEvent = null, IndirectSlice? slice = null) : IPass
{
    private readonly Dictionary<string, BufferHandle> _handles = new();
    private readonly Dictionary<string, RecordedProperty> _properties = new();
    private readonly Dictionary<uint, RecordedVertexBinding> _vertex = new();
    private int _vertexCount;
    private BindIndexBufferCommand? _index;
    private SetPipelineCommand? _pipeline;
    private bool _sourceDirty;
    private IndirectSlice? _slice;

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
            bool isEvent = ReplayEvents.IsEvent(command);
            _slice = isEvent && events + 1 == lastEvent ? slice : null;
            Execute(cmd, command);
            if (isEvent && ++events == lastEvent)
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
            case SetDescriptorTableCommand table:
                cmd.SetDescriptorTable(table.Set, BuildTable(table));
                break;
            case SetPushConstantsCommand push:
                cmd.SetPushConstants(scope.Blob(push.Data), push.Offset);
                break;
            case DrawCommand draw:
                BindSource(cmd);
                cmd.Draw(draw.VertexCount, draw.InstanceCount, draw.FirstVertex, draw.FirstInstance);
                break;
            case DrawIndexedCommand draw:
                BindSource(cmd);
                cmd.DrawIndexed(draw.IndexCount, draw.InstanceCount, draw.FirstIndex, draw.VertexOffset, draw.FirstInstance);
                break;
            case DrawIndirectCommand draw:
                BindSource(cmd);
                if (Slice(draw.Offset, draw.DrawCount, draw.Stride) is var (offset, count) && count > 0)
                    cmd.DrawIndirect(scope.Buffer(draw.Buffer.Resource), offset, count, draw.Stride);
                break;
            case DrawIndexedIndirectCommand draw:
                BindSource(cmd);
                if (Slice(draw.Offset, draw.DrawCount, draw.Stride) is var (indexedOffset, indexedCount) && indexedCount > 0)
                    cmd.DrawIndexedIndirect(scope.Buffer(draw.Buffer.Resource), indexedOffset, indexedCount, draw.Stride);
                break;
            case DrawIndirectCountCommand draw:
                BindSource(cmd);
                cmd.DrawIndirectCount(scope.Buffer(draw.Buffer.Resource), draw.Offset, scope.Buffer(draw.CountBuffer.Resource), draw.CountOffset, draw.MaxDrawCount, draw.Stride);
                break;
            case DrawIndexedIndirectCountCommand draw:
                BindSource(cmd);
                cmd.DrawIndexedIndirectCount(scope.Buffer(draw.Buffer.Resource), draw.Offset, scope.Buffer(draw.CountBuffer.Resource), draw.CountOffset, draw.MaxDrawCount, draw.Stride);
                break;
            case DispatchCommand dispatch:
                cmd.Dispatch(dispatch.GroupCountX, dispatch.GroupCountY, dispatch.GroupCountZ);
                break;
            case DispatchIndirectCommand dispatch:
                cmd.DispatchIndirect(scope.Buffer(dispatch.Buffer.Resource), dispatch.Offset);
                break;
            case ComputeBarrierCommand:
                cmd.ComputeBarrier();
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
            case BlitTextureCommand blit:
                cmd.BlitTexture(scope.Texture(blit.Source.Resource), scope.Texture(blit.DestinationAfter.Resource), blit.Regions.AsSpan());
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
            case PushMarkerCommand or PopMarkerCommand:
                break;
            default:
                throw new NotSupportedException($"Unknown command {command.GetType().Name}.");
        }
    }

    private (uint Offset, uint Count) Slice(uint offset, uint count, uint stride)
    {
        if (_slice is not { } slice)
            return (offset, count);

        uint first = Math.Min(slice.First, count);
        return (offset + first * stride, Math.Min(slice.Count, count - first));
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
                set.SetUniform(name, property.UniformType, bytes);
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
                    set.SetTexture(name, scope.View(texture, property.Range, property.ViewFormat, property.Arrayed), sampler);
                else
                    set.SetTexture(name, texture, sampler);

                break;
            case PropertyKind.Sampler:
                set.SetSampler(name, scope.Sampler(property.Sampler)!);
                break;
        }
    }

    private DescriptorTable? BuildTable(SetDescriptorTableCommand command)
    {
        if (command.Layout.Length == 0)
            return null;

        DescriptorTable table = scope.Own(scope.Factory.CreateDescriptorTable(
            new ResourceLayoutDescription(command.Set, command.Layout.ToArray()) { Table = true }));

        foreach (RecordedTableEntry entry in command.Entries)
        {
            RecordedProperty property = entry.Property;
            PropertyID name = PropertyID.Intern(property.Name);
            switch (property.Kind)
            {
                case PropertyKind.Buffer:
                    table.SetBuffer(name, entry.Index, BufferRange(property));
                    break;
                case PropertyKind.Texture:
                    Texture texture = scope.Texture(property.Resource.Resource);
                    Sampler? sampler = scope.Sampler(property.Sampler);
                    if (NeedsView(texture, property))
                        table.SetTexture(name, entry.Index, scope.View(texture, property.Range, property.ViewFormat, property.Arrayed), sampler);
                    else
                        table.SetTexture(name, entry.Index, texture, sampler);
                    break;
                case PropertyKind.Sampler:
                    table.SetSampler(name, entry.Index, scope.Sampler(property.Sampler)!);
                    break;
            }
        }

        return table;
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
            || property.Arrayed
            || (property.Range.IsTexture
                && (property.Range.BaseMipLevel != 0
                    || property.Range.MipLevels != texture.MipLevels
                    || property.Range.BaseArrayLayer != 0
                    || property.Range.ArrayLayers != texture.ArrayLayers * (texture.Type == TextureType.TextureCube ? 6u : 1u)));
}

/// <summary>Which recorded commands count as events, the steps a replay can stop after.</summary>
public static class ReplayEvents
{
    /// <summary>Whether the command is a draw, dispatch, clear or copy.</summary>
    public static bool IsEvent(RecordedCommand command)
        => command is DrawCommand or DrawIndexedCommand or DrawIndirectCommand or DrawIndexedIndirectCommand
            or DrawIndirectCountCommand or DrawIndexedIndirectCountCommand
            or DispatchCommand or DispatchIndirectCommand
            or ClearColorTargetCommand or ClearDepthStencilCommand
            or UpdateBufferCommand or UpdateTextureCommand
            or CopyBufferCommand or CopyTextureCommand or BlitTextureCommand or CopyTextureToBufferCommand
            or ResolveTextureCommand or GenerateMipsCommand;
}
