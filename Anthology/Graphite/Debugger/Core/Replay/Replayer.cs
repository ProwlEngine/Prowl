using System;
using System.Collections.Generic;
using System.Linq;
using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;

namespace Prowl.Graphite.Debugger;

/// <summary>Replays recorded passes on a user supplied device, which may differ from the recording device.</summary>
public sealed class Replayer
{
    private readonly GraphicsDevice _device;
    private readonly DeepRecording _recording;
    private readonly Dictionary<BlobRef, EquatableArray<byte>> _blobs = new();
    private readonly Dictionary<ProgramKey, RecordedProgram> _programs = new();

    /// <summary>Creates a replayer. The recording must be done.</summary>
    /// <param name="device">Device to replay on.</param>
    /// <param name="recording">Recording to replay.</param>
    public Replayer(GraphicsDevice device, DeepRecording recording)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(recording);
        _device = device;
        _recording = recording;
        foreach (RecordedBlob blob in recording.Blobs)
            _blobs[blob.Ref] = blob.Data;

        foreach (RecordedProgram program in recording.Programs)
            _programs.TryAdd(program.Key, program);
    }

    /// <summary>Replays one pass and reads back its outputs. Never throws for an unreplayable pass.</summary>
    /// <param name="request">Pass to replay.</param>
    public ReplayResult Replay(ReplayRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        DeepGroup? group = _recording.Groups.FirstOrDefault(i => i.Index == request.GroupIndex);
        DeepPass? pass = group?.Passes.FirstOrDefault(p => p.Index == request.PassIndex);
        if (group == null || pass == null)
            return NotReplayable("The request names a group or pass that is not in the recording.");

        if (pass.NotReplayable != null)
            return NotReplayable(pass.NotReplayable);

        if (request.EventIndex is { } eventIndex)
        {
            int events = pass.Commands.Count(ReplayEvents.IsEvent);
            if (eventIndex < -1 || eventIndex >= events)
                return NotReplayable($"Event {eventIndex} is out of range, pass {pass.Name} has {events} events.");
        }

        if (request.EventIndex == null && _recording.Mode == DeepMode.Full && request.Patches is not { Count: > 0 })
            return Restore(group, pass);

        try
        {
            using ReplayScope scope = new(_device, _recording, _blobs, _programs);
            List<DeepPass> ordered = group.Passes.Where(p => p.Index <= pass.Index).OrderBy(p => p.Index).ToList();
            List<DeepPass> passes = ordered.Skip(scope.Start(ordered)).ToList();
            DeepPass? blocked = passes.FirstOrDefault(p => p.NotReplayable != null);
            if (blocked != null)
                return NotReplayable($"Pass {blocked.Name} is needed to reach {pass.Name}: {blocked.NotReplayable}");

            string? error = scope.Prepare(group, passes);
            if (error == null)
                scope.AddPatches(passes[0].Index, request.Patches);
            return error != null ? NotReplayable(error) : Run(scope, group, passes, pass, request.EventIndex, request.Indirect);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return NotReplayable($"Replay failed: {ex.Message}");
        }
    }

    private ReplayResult Run(ReplayScope scope, DeepGroup group, List<DeepPass> passes, DeepPass pass, int? eventIndex, IndirectSlice? slice)
    {
        string name = $"Replay {pass.Index} {pass.Name}";
        ReplayCapture capture = new(name, ReplayScope.Outputs(group, pass), scope.ReplayIds);
        HashSet<int> steps = scope.RestoreSteps().ToHashSet();
        using FrameGraph graph = new(_device, $"Replay {group.Name}");
        foreach (DeepPass p in passes)
        {
            if (p == passes[0] || steps.Contains(p.Index))
                new ReplayRestorePass(scope, passes, p.Index).Add(graph);

            new ReplayPass($"Replay {p.Index} {p.Name}", scope, p, p == pass ? eventIndex : null, p == pass ? slice : null).Add(graph);
        }

        List<ReplayOutput> outputs = new();
        try
        {
            ExecutionTask task = graph.Execute(capture);
            _device.WaitForExecution(task);

            foreach ((TraceResourceId resource, CaptureCopy copy) in capture.Copies)
            {
                outputs.Add(new ReplayOutput(resource, copy.Regions.ToArray().ToEquatableArray(), EquatableArray.Create<byte>(_device.Map(copy.Staging))));
                _device.Unmap(copy.Staging);
            }
        }
        finally
        {
            foreach ((_, CaptureCopy copy) in capture.Copies)
                copy.Staging.Dispose();
        }

        if (capture.Failure != null)
            return NotReplayable(capture.Failure);

        string device = _device.BackendType == _recording.Backend ? "the recording backend" : $"{_device.BackendType}, not the recording backend";
        string executed = passes.Count == 1 ? $"pass {pass.Name}" : $"{passes.Count} passes up to {pass.Name}";
        return new ReplayResult(ReplayStatus.Reexecuted, $"Executed {executed} on {device} with unreproducible inputs restored from copies.", outputs.ToEquatableArray());
    }

    private ReplayResult Restore(DeepGroup group, DeepPass pass)
    {
        Dictionary<TraceVersion, RecordedCopy> copies = new();
        foreach (RecordedCopy copy in _recording.Groups.SelectMany(i => i.Passes).SelectMany(p => p.Copies))
            copies.TryAdd(copy.Version, copy);

        HashSet<TraceResourceId> outputs = ReplayScope.Outputs(group, pass);
        List<ReplayOutput> results = new();
        foreach (RecordedReference reference in pass.References.Where(r => outputs.Contains(r.Resource)))
        {
            if (!copies.TryGetValue(new TraceVersion(reference.Resource, reference.Last), out RecordedCopy? copy) || !_blobs.TryGetValue(copy.Blob, out EquatableArray<byte> data))
                return NotReplayable($"The recorded output of pass {pass.Name} is missing its data.");

            results.Add(new ReplayOutput(reference.Resource, copy.Regions, data));
        }

        return new ReplayResult(ReplayStatus.Restored, $"Outputs of pass {pass.Name} taken from the copies recorded for their versions.", results.ToEquatableArray());
    }

    private static ReplayResult NotReplayable(string reason)
        => new(ReplayStatus.NotReplayable, reason, EquatableArray<ReplayOutput>.Empty);
}
