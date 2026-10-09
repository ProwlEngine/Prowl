using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;
using Prowl.Graphite.RenderGraph;

namespace Prowl.Graphite.Debugger;

internal sealed record GraphTexture(RenderTexture Texture, TraceResourceId[] Backings);

internal sealed record GraphBuffer(TraceResourceId Id, GraphBufferDesc Desc);

internal sealed record BufferRestore(int PassIndex, string Name, byte[] Data);

internal sealed unsafe class ReplayScope : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly DeepRecording _deep;
    private readonly List<IDisposable> _owned = new();
    private readonly Dictionary<TraceResourceId, RecordedResource> _described = new();
    private readonly Dictionary<BlobRef, EquatableArray<byte>> _blobs = new();
    private readonly Dictionary<TraceResourceId, Texture> _textures = new();
    private readonly Dictionary<TraceResourceId, DeviceBuffer> _buffers = new();
    private readonly Dictionary<ProgramKey, ShaderProgram> _programs = new();
    private readonly Dictionary<int, Sampler> _samplers = new();
    private readonly Dictionary<(Texture, ResourceRange, PixelFormat?), TextureView> _views = new();
    private readonly Dictionary<TraceVersion, RecordedCopy> _copies = new();
    private readonly HashSet<TraceVersion> _planned = new();
    private readonly HashSet<TraceVersion> _produced = new();
    private readonly List<(int PassIndex, TraceResourceId Id, Texture Texture, RecordedCopy Copy)> _textureRestores = new();
    private readonly List<(int PassIndex, DeviceBuffer Buffer, RecordedCopy Copy)> _externalBufferRestores = new();

    public ReplayScope(GraphicsDevice device, DeepRecording deep)
    {
        _device = device;
        _deep = deep;
        foreach (RecordedResource resource in deep.Resources)
            _described[resource.Id] = resource;

        foreach (RecordedBlob blob in deep.Blobs)
            _blobs[blob.Ref] = blob.Data;

        foreach (RecordedCopy copy in deep.Views.SelectMany(v => v.Passes).SelectMany(p => p.Copies))
            _copies.TryAdd(copy.Version, copy);
    }

    public ResourceFactory Factory => _device.ResourceFactory;

    public Dictionary<string, GraphTexture> GraphTextures { get; } = new();

    public Dictionary<string, GraphBuffer> GraphBuffers { get; } = new();

    public Dictionary<ResourceId, TraceResourceId> ReplayIds { get; } = new();

    public List<BufferRestore> BufferRestores { get; } = new();

    public Dictionary<TraceResourceId, RenderTexture> Owners { get; } = new();

    public DeviceBuffer BindBuffer(TraceResourceId id, DeviceBuffer buffer)
    {
        _buffers[id] = buffer;
        ReplayIds[buffer.ResourceId] = id;
        return buffer;
    }

    public Texture Texture(TraceResourceId id) => _textures[id];

    public DeviceBuffer Buffer(TraceResourceId id) => _buffers[id];

    public ShaderProgram Program(ProgramKey key) => _programs[key];

    public Sampler? Sampler(int index) => index < 0 ? null : _samplers[index];

    public TextureView View(Texture texture, ResourceRange range, PixelFormat? format)
    {
        if (_views.TryGetValue((texture, range, format), out TextureView? view))
            return view;

        view = Own(_device.ResourceFactory.CreateTextureView(new TextureViewDescription(
            texture,
            range.BaseMipLevel,
            range.IsTexture ? range.MipLevels : texture.MipLevels,
            range.BaseArrayLayer,
            range.IsTexture ? range.ArrayLayers : texture.ArrayLayers,
            format)));
        _views[(texture, range, format)] = view;
        return view;
    }

    public ReadOnlySpan<byte> Blob(BlobRef blob) => _blobs[blob].AsSpan();

    public int Start(IReadOnlyList<DeepPass> passes)
    {
        for (int start = passes.Count - 1; start > 0; start--)
        {
            if (CanStartAt(passes, start))
                return start;
        }

        return 0;
    }

    private bool CanStartAt(IReadOnlyList<DeepPass> passes, int start)
    {
        HashSet<TraceVersion> produced = new();
        for (int i = start; i < passes.Count; i++)
        {
            foreach (RecordedReference reference in passes[i].References)
            {
                TraceVersion version = new(reference.Resource, reference.First);
                if (reference.NeedsContents && !produced.Contains(version)
                    && (!_copies.TryGetValue(version, out RecordedCopy? copy) || Unrestorable(reference.Resource, copy) != null))
                    return false;
            }

            AddProduced(produced, passes[i]);
        }

        return true;
    }

    private static void AddProduced(HashSet<TraceVersion> produced, DeepPass pass)
    {
        foreach (RecordedReference reference in pass.References)
        {
            if (reference.Last != reference.First)
                produced.Add(new TraceVersion(reference.Resource, reference.Last));
        }
    }

    public string? Prepare(DeepView view, IReadOnlyList<DeepPass> passes)
    {
        Dictionary<string, RecordedGraphResource> byName = new();
        foreach (RecordedGraphResource resource in view.Resources)
            byName.TryAdd(resource.Name, resource);

        foreach (DeepPass pass in passes)
        {
            string? error = PreparePass(pass, byName);
            if (error != null)
                return error;

            AddProduced(_produced, pass);
        }

        for (int i = 0; i < _deep.Samplers.Length; i++)
            _samplers[i] = Own(_device.ResourceFactory.CreateSampler(_deep.Samplers[i]));

        return null;
    }

    public static HashSet<TraceResourceId> Outputs(DeepView view, DeepPass pass)
    {
        HashSet<TraceResourceId> referenced = pass.References.Select(r => r.Resource).ToHashSet();
        HashSet<string> names = pass.Accesses.Where(a => a.IsOutput).Select(a => a.Resource).ToHashSet();
        HashSet<TraceResourceId> outputs = new();
        foreach (RecordedGraphResource resource in view.Resources)
        {
            if (names.Contains(resource.Name))
                outputs.UnionWith(resource.Backings.Select(b => b.Id));
        }

        outputs.IntersectWith(referenced);
        return outputs;
    }

    private string? PreparePass(DeepPass pass, Dictionary<string, RecordedGraphResource> byName)
    {
        foreach (RecordedAccess access in pass.Accesses)
        {
            if (!byName.TryGetValue(access.Resource, out RecordedGraphResource? graph))
                return $"Access to {access.Resource} has no recorded resource.";

            if (graph.Origin == GraphResourceOrigin.ViewTarget)
                return $"Pass {pass.Name} writes the view target, which is never copied.";

            string? error = graph.Kind == GraphResourceKind.Texture ? BuildTexture(graph) : BuildBuffer(graph);
            if (error != null)
                return error;
        }

        foreach (RecordedReference reference in pass.References)
        {
            if (_textures.ContainsKey(reference.Resource) || _buffers.ContainsKey(reference.Resource))
                continue;

            string? error = BuildStandalone(reference.Resource);
            if (error != null)
                return error;
        }

        foreach (RecordedReference reference in pass.References)
        {
            TraceVersion version = new(reference.Resource, reference.First);
            if (!reference.NeedsContents || _produced.Contains(version) || !_copies.TryGetValue(version, out RecordedCopy? restore) || !_planned.Add(version))
                continue;

            string? error = PlanRestore(pass.Index, reference.Resource, restore);
            if (error != null)
                return error;
        }

        foreach (SetPipelineCommand pipeline in pass.Commands.OfType<SetPipelineCommand>())
        {
            string? error = BuildProgram(pipeline.Program);
            if (error != null)
                return error;
        }

        return null;
    }

    public IEnumerable<int> RestoreSteps()
        => _textureRestores.Select(r => r.PassIndex)
            .Concat(_externalBufferRestores.Select(r => r.PassIndex))
            .Concat(BufferRestores.Select(r => r.PassIndex))
            .Distinct()
            .Order();

    public void RestoreStep(CommandBuffer cmd, int passIndex)
    {
        foreach ((int index, _, Texture texture, RecordedCopy copy) in _textureRestores)
        {
            if (index == passIndex)
                UpdateTexture(cmd, texture, copy);
        }

        foreach ((int index, DeviceBuffer buffer, RecordedCopy copy) in _externalBufferRestores)
        {
            if (index == passIndex)
                UpdateBuffer(cmd, buffer, copy);
        }
    }

    public IEnumerable<(string Name, GraphTexture Texture)> StepTextures(int passIndex)
    {
        foreach ((int index, TraceResourceId id, _, _) in _textureRestores)
        {
            if (index != passIndex)
                continue;

            foreach ((string name, GraphTexture texture) in GraphTextures)
            {
                if (texture.Backings.Contains(id))
                    yield return (name, texture);
            }
        }
    }

    private void UpdateTexture(CommandBuffer cmd, Texture texture, RecordedCopy copy)
    {
        byte[] data = ImmutableCollectionsMarshal.AsArray(_blobs[copy.Blob].Items)!;
        foreach (CopyRegion region in copy.Regions)
        {
            uint size = region.Width * region.Height * region.Depth * region.Format.GetSizeInBytes();
            TextureRegion target = new(0, 0, 0, region.Width, region.Height, region.Depth, region.MipLevel, region.ArrayLayer);
            fixed (byte* source = &data[region.Offset])
                cmd.UpdateTexture(texture, (IntPtr)source, size, target);
        }
    }

    private void UpdateBuffer(CommandBuffer cmd, DeviceBuffer buffer, RecordedCopy copy)
    {
        byte[] data = ImmutableCollectionsMarshal.AsArray(_blobs[copy.Blob].Items)!;
        fixed (byte* source = data)
            cmd.UpdateBuffer(buffer, 0, (IntPtr)source, Math.Min((uint)data.Length, buffer.SizeInBytes));
    }

    public Framebuffer Framebuffer(SetFramebufferCommand command)
    {
        if (command.Colors.Length > 0 && Owners.TryGetValue(command.Colors[0].Texture.Resource, out RenderTexture? owner) && WholeTexture(owner, command))
            return owner.Framebuffer;

        FramebufferAttachment[] colors = command.Colors
            .Select(a => new FramebufferAttachment(Texture(a.Texture.Resource), a.ArrayLayer, a.MipLevel))
            .ToArray();
        FramebufferAttachment? depth = command.Depth is { } d ? new FramebufferAttachment(Texture(d.Texture.Resource), d.ArrayLayer, d.MipLevel) : null;
        return Own(_device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(depth, colors)));
    }

    public void Dispose()
    {
        for (int i = _owned.Count - 1; i >= 0; i--)
            _owned[i].Dispose();

        _owned.Clear();
    }

    public T Own<T>(T resource) where T : IDisposable
    {
        _owned.Add(resource);
        return resource;
    }

    private bool WholeTexture(RenderTexture owner, SetFramebufferCommand command)
    {
        if (command.Colors.Length != owner.ColorTextures.Length || (command.Depth != null) != (owner.DepthTexture != null))
            return false;

        for (int i = 0; i < command.Colors.Length; i++)
        {
            RecordedAttachment color = command.Colors[i];
            if (color.MipLevel != 0 || color.ArrayLayer != 0 || _textures[color.Texture.Resource] != owner.ColorTextures[i])
                return false;
        }

        return command.Depth == null || (command.Depth.MipLevel == 0 && command.Depth.ArrayLayer == 0 && _textures[command.Depth.Texture.Resource] == owner.DepthTexture);
    }

    private string? BuildTexture(RecordedGraphResource graph)
    {
        if (GraphTextures.ContainsKey(graph.Name))
            return null;

        RecordedBacking[] colors = graph.Backings.Where(b => b.Role == BackingRole.Color).OrderBy(b => b.Index).ToArray();
        RecordedBacking? depth = graph.Backings.Where(b => b.Role == BackingRole.Depth).Cast<RecordedBacking?>().FirstOrDefault();
        TextureDescription[] colorDescriptions = new TextureDescription[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            if (!_described.TryGetValue(colors[i].Id, out RecordedResource? resource) || resource.Texture is not { } description)
                return $"Backing of {graph.Name} has no recorded description.";

            colorDescriptions[i] = description;
        }

        TextureDescription? depthDescription = null;
        if (depth is { } depthBacking)
        {
            if (!_described.TryGetValue(depthBacking.Id, out RecordedResource? resource) || resource.Texture is not { } description)
                return $"Depth backing of {graph.Name} has no recorded description.";

            depthDescription = description;
        }

        TextureDescription first = colorDescriptions.Length > 0 ? colorDescriptions[0] : depthDescription!.Value;
        foreach (TextureDescription description in colorDescriptions.Concat(depthDescription is { } d ? [d] : []))
        {
            string? error = CheckTexture(description, graph.Name);
            if (error != null)
                return error;
        }

        PixelFormat[] formats = colorDescriptions.Select(d => d.Format).ToArray();
        bool storage = colorDescriptions.Any(d => (d.Usage & TextureUsage.Storage) != 0);
        RenderTextureDescription renderDescription = depthDescription is { } depthValue
            ? new RenderTextureDescription(first.Width, first.Height, formats, depthValue.Format, first.SampleCount, storage)
            : new RenderTextureDescription(first.Width, first.Height, formats, false, first.SampleCount, storage);

        RenderTexture texture = Own(_device.ResourceFactory.CreateRenderTexture(renderDescription));
        List<TraceResourceId> ids = new();
        for (int i = 0; i < colors.Length; i++)
        {
            Register(colors[i].Id, texture.ColorTextures[i], texture);
            ids.Add(colors[i].Id);
        }

        if (depth is { } depthRegistered)
        {
            Register(depthRegistered.Id, texture.DepthTexture!, texture);
            ids.Add(depthRegistered.Id);
        }

        GraphTextures[graph.Name] = new GraphTexture(texture, ids.ToArray());
        return null;
    }

    private string? BuildBuffer(RecordedGraphResource graph)
    {
        if (GraphBuffers.ContainsKey(graph.Name))
            return null;

        if (graph.Buffer is not { } desc || graph.Backings.Length == 0)
            return $"Buffer {graph.Name} has no recorded description.";

        GraphBuffers[graph.Name] = new GraphBuffer(graph.Backings[0].Id, desc);
        return null;
    }

    private string? BuildStandalone(TraceResourceId id)
    {
        if (!_described.TryGetValue(id, out RecordedResource? resource))
            return $"Resource {id.Value} has no recorded description.";

        if (resource.Texture is { } texture)
        {
            string? error = CheckTexture(texture, resource.Name);
            if (error != null)
                return error;

            Register(id, Own(_device.ResourceFactory.CreateTexture(texture)), null);
            return null;
        }

        if (resource.Buffer is { } buffer)
        {
            BindBuffer(id, Own(_device.ResourceFactory.CreateBuffer(buffer)));
            return null;
        }

        return $"Resource {resource.Name} was referenced but never described.";
    }

    private string? CheckTexture(TextureDescription description, string name)
        => _device.GetPixelFormatSupport(description.Format, description.Type, description.Usage)
            ? null
            : $"{name} uses {description.Format} as {description.Usage}, which the replay device does not support.";

    private void Register(TraceResourceId id, Texture texture, RenderTexture? owner)
    {
        _textures[id] = texture;
        ReplayIds[texture.ResourceId] = id;
        if (owner != null)
            Owners[id] = owner;
    }

    private string? Unrestorable(TraceResourceId id, RecordedCopy copy)
    {
        if (!_described.TryGetValue(id, out RecordedResource? resource))
            return $"Resource {id.Value} has no recorded description.";

        if (resource.Texture is not { } texture)
            return null;

        if ((texture.Usage & TextureUsage.DepthStencil) != 0)
            return $"{resource.Name} is a depth texture, which cannot be restored yet.";

        if (texture.SampleCount != TextureSampleCount.Count1)
            return $"{resource.Name} is multisampled, which cannot be restored yet.";

        int length = _blobs[copy.Blob].Length;
        foreach (CopyRegion region in copy.Regions)
        {
            if ((ulong)region.Offset + region.Width * region.Height * region.Depth * region.Format.GetSizeInBytes() > (ulong)length)
                return $"{resource.Name} has a copy layout that cannot be restored yet.";
        }

        return null;
    }

    private string? PlanRestore(int passIndex, TraceResourceId id, RecordedCopy copy)
    {
        string? error = Unrestorable(id, copy);
        if (error != null)
            return error;

        if (_textures.TryGetValue(id, out Texture? texture))
        {
            _textureRestores.Add((passIndex, id, texture, copy));
            return null;
        }

        string? name = GraphBuffers.FirstOrDefault(pair => pair.Value.Id == id).Key;
        if (name != null)
        {
            BufferRestores.Add(new BufferRestore(passIndex, name, ImmutableCollectionsMarshal.AsArray(_blobs[copy.Blob].Items)!));
            return null;
        }

        _externalBufferRestores.Add((passIndex, _buffers[id], copy));
        return null;
    }

    private string? BuildProgram(ProgramKey key)
    {
        if (_programs.ContainsKey(key))
            return null;

        RecordedProgram? program = _deep.Programs.FirstOrDefault(p => p.Key == key);
        if (program == null)
            return "A pipeline references a program that is missing from the recording.";

        ShaderStageDescription[] stages = program.Stages
            .Select(s => new ShaderStageDescription(s.Stage, _blobs[s.Code].Items.ToArray(), s.EntryPoint))
            .ToArray();
        ResourceLayoutDescription[] layouts = program.Layouts.ToArray();
        ShaderProgram built;
        if (program.IsCompute)
        {
            built = _device.ResourceFactory.CreateComputeProgram(new ComputeDescription(stages[0], layouts, program.ThreadGroupX, program.ThreadGroupY, program.ThreadGroupZ));
        }
        else
        {
            built = _device.ResourceFactory.CreateGraphicsProgram(new ShaderDescription(
                stages,
                program.Blend ?? default,
                program.DepthStencil ?? default,
                program.Rasterizer ?? default,
                program.VertexLayouts.ToArray(),
                layouts));
        }

        _programs[key] = Own(built);
        return built.Key == key ? null : "A rebuilt program does not match its recorded key.";
    }
}
