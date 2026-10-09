using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;
using Prowl.Graphite.RenderGraph;

namespace Prowl.Graphite.Debugger;

internal sealed record DeepResult(
    EquatableArray<RecordedResource> Resources,
    EquatableArray<RecordedProgram> Programs,
    EquatableArray<SamplerDescription> Samplers,
    EquatableArray<RecordedBlob> Blobs,
    EquatableArray<DeepView> Views);

public sealed partial class DeepRecording
{
    private readonly SortedDictionary<int, ViewState> _views = new();
    private readonly HashSet<(ResourceId, uint)> _copied = new();

    void ICaptureProfiler.DescribeView(in ViewCaptureInfo view)
    {
        if (!_views.TryGetValue(view.ViewIndex, out ViewState? state))
            return;

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

        foreach (PassInfo passInfo in view.Passes.Span)
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

                    if (recorded.ReadsContents)
                        pass.Reads.Add(backing.Id);
                }
            }

            state.Passes[passInfo.Index] = pass;
        }
    }

    void ICaptureProfiler.OnPassEnd(in PassInfo pass, ReadOnlySpan<PassReference> references, ICaptureContext capture)
    {
        if (!_views.TryGetValue(pass.ViewIndex, out ViewState? view))
            return;

        PassState state = view.Pass(pass.Index);
        RecordedReference[] recorded = new RecordedReference[references.Length];
        for (int i = 0; i < references.Length; i++)
        {
            PassReference reference = references[i];
            ResourceId id = reference.First.Resource;
            ResourceBuilder builder = Builder(id);
            Describe(builder, in reference, view.Origins.TryGetValue(id, out GraphResourceOrigin origin) ? Origin(origin) : ResourceOrigin.External);
            recorded[i] = new RecordedReference(builder.Id, reference.First.Version, reference.LastVersion, ReadsContents(view, state, id));

            if (reference.LastVersion != reference.First.Version && !state.Outputs.Contains(id) && !state.Written.Contains(id))
                state.NotReplayable ??= $"Undeclared GPU write to {reference.Name}.";
        }

        state.References = recorded;
        for (int i = 0; i < references.Length; i++)
        {
            PassReference reference = references[i];
            ResourceId id = reference.First.Resource;
            if (IsViewTarget(view, id) || view.Known.Contains((id, reference.First.Version)) || !recorded[i].Reads)
                continue;

            Copy(state, capture, in reference, CopyPlacement.BeforePass, reference.First.Version);
            view.Known.Add((id, reference.First.Version));
        }

        foreach (PassReference reference in references)
        {
            ResourceId id = reference.First.Resource;
            if (state.Outputs.Contains(id) || state.Written.Contains(id))
                view.Known.Add((id, reference.LastVersion));
        }

        if (Mode != DeepMode.Full)
            return;

        for (int i = 0; i < references.Length; i++)
        {
            PassReference reference = references[i];
            ResourceId id = reference.First.Resource;
            if (!state.Outputs.Contains(id) || IsViewTarget(view, id) || _copied.Contains((id, reference.LastVersion)))
                continue;

            Copy(state, capture, in reference, CopyPlacement.AfterPass, reference.LastVersion);
            view.Known.Add((id, reference.LastVersion));
        }
    }

    private static bool IsViewTarget(ViewState view, ResourceId id)
        => view.Origins.TryGetValue(id, out GraphResourceOrigin origin) && origin == GraphResourceOrigin.ViewTarget;

    private static bool ReadsContents(ViewState view, PassState state, ResourceId id)
    {
        if (state.Loaded.Contains(id) || state.Reads.Contains(id))
            return true;

        if (state.Attachments.Contains(id))
            return false;

        return !view.Origins.TryGetValue(id, out GraphResourceOrigin origin) || origin != GraphResourceOrigin.Transient;
    }

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
        foreach (PassState pass in _views.Values.SelectMany(v => v.Passes.Values))
        {
            foreach (PendingCopy pending in pass.Copies)
                pending.Copy.Staging.Dispose();

            pass.Copies.Clear();
        }
    }

    private DeepResult Build(GraphicsDevice device)
    {
        foreach (ViewState view in _views.Values)
        {
            foreach (PassState pass in view.Passes.Values)
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
            _views.Values.Select(BuildView).ToEquatableArray());
    }

    private static DeepView BuildView(ViewState view)
        => new(
            view.Name,
            view.Index,
            view.PixelWidth,
            view.PixelHeight,
            view.Resources.ToEquatableArray(),
            view.Passes.Values.Select(BuildPass).ToEquatableArray());

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
            _ => ResourceOrigin.ViewTarget,
        };
}
