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
    EquatableArray<DeepExecution> Executions);

internal sealed partial class DeepSink
{
    public void OnPassEnd(in PassInfo pass, ReadOnlySpan<PassReference> references, ICaptureContext capture)
    {
        if (_thread.Value!.View is not { } view)
            return;

        PassState state = view.Pass(pass.Index);
        RecordedReference[] recorded = new RecordedReference[references.Length];
        for (int i = 0; i < references.Length; i++)
        {
            PassReference reference = references[i];
            ResourceId id = reference.First.Resource;
            ResourceBuilder builder = Builder(id);
            Describe(builder, in reference, view.Origins.TryGetValue(id, out GraphResourceOrigin origin) ? Origin(origin) : ResourceOrigin.External);
            recorded[i] = new RecordedReference(builder.Id, reference.First.Version, reference.LastVersion);

            if (reference.LastVersion != reference.First.Version && !state.Outputs.Contains(id) && !state.Written.Contains(id))
                state.NotReplayable ??= $"Undeclared GPU write to {reference.Name}.";
        }

        state.References = recorded;
        foreach (PassReference reference in references)
        {
            ResourceId id = reference.First.Resource;
            if (state.Outputs.Contains(id) || state.Written.Contains(id))
                view.Execution.Known.Add((id, reference.LastVersion));
        }
    }

    public DeepResult Build()
    {
        ImmutableArray<RecordedBlob> blobs = _store.Snapshot()
            .Select(pair => new RecordedBlob(pair.Key, ImmutableCollectionsMarshal.AsImmutableArray(pair.Value)))
            .ToImmutableArray();

        lock (_gate)
        {
            return new DeepResult(
                _resources.Values
                    .OrderBy(b => b.Id.Value)
                    .Select(b => new RecordedResource(b.Id, b.Name, b.Kind, b.Origin, b.Texture, b.Buffer))
                    .ToEquatableArray(),
                _programs.Values.ToEquatableArray(),
                _samplers.ToEquatableArray(),
                blobs,
                _executions.Select(e => new DeepExecution(e.Key, e.Value.Views.Values.Select(BuildView).ToEquatableArray())).ToEquatableArray());
        }
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
            EquatableArray<RecordedCopy>.Empty,
            pass.NotReplayable);

    private static void Describe(ResourceBuilder builder, in PassReference reference, ResourceOrigin origin)
    {
        lock (builder)
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
    }

    private static ResourceOrigin Origin(GraphResourceOrigin origin)
        => origin switch
        {
            GraphResourceOrigin.Transient => ResourceOrigin.Transient,
            GraphResourceOrigin.Imported => ResourceOrigin.Imported,
            _ => ResourceOrigin.ViewTarget,
        };
}
