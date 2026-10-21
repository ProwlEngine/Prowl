using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;

namespace Prowl.Graphite.Debugger;

internal sealed record DeepResult(
    EquatableArray<RecordedResource> Resources,
    EquatableArray<RecordedProgram> Programs,
    EquatableArray<SamplerDescription> Samplers,
    EquatableArray<RecordedBlob> Blobs,
    EquatableArray<DeepInstance> Instances);

public sealed partial class DeepRecording
{
    private readonly SortedDictionary<int, InstanceState> _instances = new();
    private readonly HashSet<(ResourceId, uint)> _copied = new();

    void ICaptureProfiler.DescribeInstance(in InstanceCaptureInfo instance)
    {
        if (!_instances.TryGetValue(instance.InstanceIndex, out InstanceState? state))
            return;

        Dictionary<RenderResourceID, GraphResourceInfo> byName = new();
        foreach (GraphResourceInfo info in instance.Resources.Span)
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
                info.Buffer,
                info.HistoryDepth,
                info.HistorySlot,
                info.HistoryValid));
        }

        foreach (PassInfo passInfo in instance.Passes.Span)
        {
            PassState pass = new(passInfo.Name, passInfo.Index);
            foreach (PassResourceAccess access in passInfo.Accesses.Span)
            {
                RecordedAccess recorded = new(
                    RenderResourceID.ToString(access.Id) ?? access.Id.ToString(),
                    access.Kind,
                    access.IsOutput,
                    access.TextureUsage,
                    access.DepthUsage,
                    access.BufferUsage);
                pass.Accesses.Add(recorded);

                if (!byName.TryGetValue(access.Id, out GraphResourceInfo resource))
                    continue;

                foreach (GraphBacking backing in resource.Backings.Span)
                {
                    pass.Declared.Add(backing.Id);
                    if (access.IsOutput)
                        pass.Outputs.Add(backing.Id);
                }
            }

            state.Passes[passInfo.Index] = pass;
        }
    }

    void ICaptureProfiler.OnPassEnd(in PassInfo pass, ReadOnlySpan<PassReference> references, ICaptureContext capture)
    {
        if (!_instances.TryGetValue(pass.InstanceIndex, out InstanceState? instance))
            return;

        PassState state = instance.Pass(pass.Index);
        RecordedReference[] recorded = new RecordedReference[references.Length];
        for (int i = 0; i < references.Length; i++)
        {
            PassReference reference = references[i];
            ResourceId id = reference.First.Resource;
            ResourceBuilder builder = Builder(id);
            Describe(builder, in reference, instance.Origins.TryGetValue(id, out GraphResourceOrigin origin) ? Origin(origin) : ResourceOrigin.External);
            recorded[i] = new RecordedReference(builder.Id, reference.First.Version, reference.LastVersion, reference.NeedsContents);

            if (reference.LastVersion != reference.First.Version && !state.Outputs.Contains(id) && !state.Written.Contains(id))
                state.NotReplayable ??= $"Undeclared GPU write to {reference.Name}.";
        }

        state.References = recorded;
        for (int i = 0; i < references.Length; i++)
        {
            PassReference reference = references[i];
            ResourceId id = reference.First.Resource;
            if (IsInstanceTarget(instance, id) || instance.Known.Contains((id, reference.First.Version)) || !reference.NeedsContents)
                continue;

            Copy(state, capture, in reference, CopyPlacement.BeforePass, reference.First.Version);
            instance.Known.Add((id, reference.First.Version));
        }

        foreach (PassReference reference in references)
        {
            ResourceId id = reference.First.Resource;
            if (state.Outputs.Contains(id) || state.Written.Contains(id))
                instance.Known.Add((id, reference.LastVersion));
        }

        if (Mode != DeepMode.Full)
            return;

        for (int i = 0; i < references.Length; i++)
        {
            PassReference reference = references[i];
            ResourceId id = reference.First.Resource;
            if (!state.Outputs.Contains(id) || IsInstanceTarget(instance, id) || _copied.Contains((id, reference.LastVersion)))
                continue;

            Copy(state, capture, in reference, CopyPlacement.AfterPass, reference.LastVersion);
            instance.Known.Add((id, reference.LastVersion));
        }
    }

    private static bool IsInstanceTarget(InstanceState instance, ResourceId id)
        => instance.Origins.TryGetValue(id, out GraphResourceOrigin origin) && origin == GraphResourceOrigin.InstanceTarget;

    private void Copy(PassState state, ICaptureContext capture, in PassReference reference, CopyPlacement placement, uint version)
    {
        ResourceId id = reference.First.Resource;
        if (!_copied.Add((id, version)))
            return;

        try
        {
            state.Copies.Add(new PendingCopy(new TraceVersion(Builder(id).Id, version), placement, capture.Copy(in reference, placement)));
        }
        catch (NotSupportedException ex)
        {
            state.NotReplayable ??= $"{reference.Name} cannot be copied: {ex.Message}";
        }
    }

    private void Release()
    {
        foreach (PassState pass in _instances.Values.SelectMany(i => i.Passes.Values))
        {
            foreach (PendingCopy pending in pass.Copies)
                pending.Copy.Staging.Dispose();

            pass.Copies.Clear();
        }
    }

    private DeepResult Build(GraphicsDevice device)
    {
        foreach (InstanceState instance in _instances.Values)
        {
            foreach (PassState pass in instance.Passes.Values)
                pass.Recorded = ReadCopies(device, pass);
        }

        ImmutableArray<RecordedBlob> blobs = _store.Snapshot()
            .Select(pair => new RecordedBlob(pair.Key, ImmutableCollectionsMarshal.AsImmutableArray(pair.Value)))
            .ToImmutableArray();

        return new DeepResult(
            _resources.Values
                .OrderBy(b => b.Id.Value)
                .Select(b => new RecordedResource(b.Id, b.Name, b.Kind, b.Origin, b.Texture, b.Buffer))
                .ToEquatableArray(),
            _programs.Values.ToEquatableArray(),
            _samplers.ToEquatableArray(),
            blobs,
            _instances.Values.Select(BuildInstance).ToEquatableArray());
    }

    private static DeepInstance BuildInstance(InstanceState instance)
        => new(
            instance.Info.Name,
            instance.Info.Index,
            instance.Info.ParentIndex,
            instance.Info.Key,
            instance.Info.PixelWidth,
            instance.Info.PixelHeight,
            instance.Resources.ToEquatableArray(),
            instance.Passes.Values.Select(BuildPass).ToEquatableArray());

    private static DeepPass BuildPass(PassState pass)
        => new(
            pass.Name,
            pass.PassIndex,
            pass.Accesses.ToEquatableArray(),
            pass.References.ToEquatableArray(),
            pass.Commands.ToEquatableArray(),
            pass.Recorded,
            pass.NotReplayable);

    private EquatableArray<RecordedCopy> ReadCopies(GraphicsDevice device, PassState pass)
    {
        RecordedCopy[] recorded = new RecordedCopy[pass.Copies.Count];
        for (int i = 0; i < recorded.Length; i++)
        {
            PendingCopy pending = pass.Copies[i];
            DeviceBuffer staging = pending.Copy.Staging;
            BlobRef blob;
            try
            {
                blob = _store.Put(device.Map(staging));
                device.Unmap(staging);
            }
            finally
            {
                staging.Dispose();
            }

            recorded[i] = new RecordedCopy(pending.Version, pending.Placement, pending.Copy.Regions.ToArray().ToEquatableArray(), blob);
        }

        pass.Copies.Clear();
        return recorded.ToEquatableArray();
    }

    private static void Describe(ResourceBuilder builder, in PassReference reference, ResourceOrigin origin)
    {
        if (builder.Described)
            return;

        builder.Described = true;
        builder.Name = reference.Name;
        builder.Origin = origin;
        builder.Kind = reference.Texture != null ? GraphResourceKind.Texture : GraphResourceKind.Buffer;
        builder.Texture = reference.Texture;
        builder.Buffer = reference.Buffer;
    }

    private static ResourceOrigin Origin(GraphResourceOrigin origin)
        => origin switch
        {
            GraphResourceOrigin.Transient => ResourceOrigin.Transient,
            GraphResourceOrigin.Imported => ResourceOrigin.Imported,
            GraphResourceOrigin.Persistent => ResourceOrigin.Persistent,
            _ => ResourceOrigin.InstanceTarget,
        };
}
