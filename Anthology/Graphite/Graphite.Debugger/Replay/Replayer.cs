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

        if (_recording.Mode != DeepMode.Full)
            return NotReplayable("ReplayOnly recordings need the view re-executed, which is not supported yet.");

        try
        {
            using ReplayScope scope = new(_device, _recording);
            string? error = scope.Prepare(execution, view, pass);
            return error != null ? NotReplayable(error) : Run(scope, view, pass);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return NotReplayable($"Replay failed: {ex.Message}");
        }
    }

    private ReplayResult Run(ReplayScope scope, DeepView view, DeepPass pass)
    {
        scope.RestoreImmediate();
        string name = $"Replay {pass.Name}";
        HashSet<TraceResourceId> wanted = pass.Copies.Where(c => c.Placement == CopyPlacement.AfterPass).Select(c => c.Version.Resource).ToHashSet();
        ReplayCapture capture = new(name, wanted, scope.ReplayIds);
        using RenderPipeline pipeline = new(new IPass[] { new ReplayRestorePass(scope, pass), new ReplayPass(name, scope, _recording, pass) });
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
        return new ReplayResult(ReplayStatus.Reexecuted, $"Executed pass {pass.Name} on {device} with inputs restored from copies.", outputs.ToEquatableArray());
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
