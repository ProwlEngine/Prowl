using System;
using System.Collections.Generic;
using System.Linq;
using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;
using Prowl.Graphite.RenderGraph;

namespace Prowl.Graphite.Debugger;

/// <summary>Replays recorded passes on a user supplied device, which may differ from the recording device.</summary>
public sealed class Replayer
{
    private readonly GraphicsDevice _device;
    private readonly DeepRecording _recording;

    /// <summary>Creates a replayer. The recording must be done.</summary>
    /// <param name="device">Device to replay on.</param>
    /// <param name="recording">Recording to replay.</param>
    public Replayer(GraphicsDevice device, DeepRecording recording)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(recording);
        _device = device;
        _recording = recording;
    }

    /// <summary>Replays one pass and reads back its outputs. Never throws for an unreplayable pass.</summary>
    /// <param name="request">Pass to replay.</param>
    public ReplayResult Replay(ReplayRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        DeepExecution? execution = _recording.Executions.FirstOrDefault(e => e.ExecutionId == request.ExecutionId);
        DeepView? view = execution?.Views.FirstOrDefault(v => v.Index == request.ViewIndex);
        DeepPass? pass = view?.Passes.FirstOrDefault(p => p.Index == request.PassIndex);
        if (execution == null || view == null || pass == null)
            return NotReplayable("The request names an execution, view, or pass that is not in the recording.");

        if (pass.NotReplayable != null)
            return NotReplayable(pass.NotReplayable);

        if (request.EventIndex is { } eventIndex)
        {
            int events = pass.Commands.Count(ReplayEvents.IsEvent);
            if (eventIndex < 0 || eventIndex >= events)
                return NotReplayable($"Event {eventIndex} is out of range, pass {pass.Name} has {events} events.");
        }

        bool reexecute = _recording.Mode != DeepMode.Full;
        List<DeepPass> passes = reexecute ? view.Passes.Where(p => p.Index <= pass.Index).OrderBy(p => p.Index).ToList() : [pass];
        DeepPass? blocked = passes.FirstOrDefault(p => p.NotReplayable != null);
        if (blocked != null)
            return NotReplayable($"Pass {blocked.Name} is needed to reach {pass.Name}: {blocked.NotReplayable}");

        try
        {
            using ReplayScope scope = new(_device, _recording);
            string? error = scope.Prepare(execution, view, passes, reexecute);
            return error != null ? NotReplayable(error) : Run(scope, view, passes, pass, request.EventIndex);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return NotReplayable($"Replay failed: {ex.Message}");
        }
    }

    private ReplayResult Run(ReplayScope scope, DeepView view, List<DeepPass> passes, DeepPass pass, int? eventIndex)
    {
        scope.RestoreImmediate();
        string name = $"Replay {pass.Index} {pass.Name}";
        HashSet<TraceResourceId> wanted = _recording.Mode == DeepMode.Full
            ? pass.Copies.Where(c => c.Placement == CopyPlacement.AfterPass).Select(c => c.Version.Resource).ToHashSet()
            : scope.Outputs(pass);
        ReplayCapture capture = new(name, wanted, scope.ReplayIds);
        List<IPass> replay = [new ReplayRestorePass(scope, passes)];
        replay.AddRange(passes.Select(p => new ReplayPass($"Replay {p.Index} {p.Name}", scope, _recording, p, p == pass ? eventIndex : null)));
        using RenderPipeline pipeline = new(replay.ToArray());
        ReplayView[] views = [new ReplayView(view.Name, view.PixelWidth, view.PixelHeight)];

        _device.Debug.Attach(capture);
        try
        {
            ExecutionTask task = _device.DispatchGraph(pipeline, views);
            _device.WaitForExecution(task);
        }
        finally
        {
            _device.Debug.Detach(capture);
        }

        List<ReplayOutput> outputs = new();
        foreach ((TraceResourceId resource, CaptureCopy copy) in capture.Copies)
        {
            try
            {
                outputs.Add(new ReplayOutput(resource, copy.Regions.ToArray().ToEquatableArray(), EquatableArray.Create<byte>(_device.Map(copy.Staging))));
                _device.Unmap(copy.Staging);
            }
            finally
            {
                copy.Staging.Dispose();
            }
        }

        if (capture.Failure != null)
            return NotReplayable(capture.Failure);

        string device = _device.BackendType == _recording.Backend ? "the recording backend" : $"{_device.BackendType}, not the recording backend";
        string executed = passes.Count == 1 ? $"pass {pass.Name}" : $"{passes.Count} passes up to {pass.Name}";
        return new ReplayResult(ReplayStatus.Reexecuted, $"Executed {executed} on {device} with unreproducible inputs restored from copies.", outputs.ToEquatableArray());
    }

    private static ReplayResult NotReplayable(string reason)
        => new(ReplayStatus.NotReplayable, reason, EquatableArray<ReplayOutput>.Empty);

    private readonly struct ReplayView(string name, uint width, uint height) : IRenderView
    {
        public uint PixelWidth => width;

        public uint PixelHeight => height;

        public int ViewId => 0;

        public string Name => name;
    }
}
