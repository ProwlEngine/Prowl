using System;
using Prowl.Graphite.Debugger.Data;
using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;

namespace Prowl.Graphite.Debugger;

/// <summary>Everything needed to replay one execution. Pass it to the execution, then read it once done.</summary>
public sealed partial class DeepRecording : IGraphProfiler, IGpuStatsProfiler, ICaptureProfiler, ICommandStreamProfiler, IDisposable
{
    private readonly GraphicsDevice? _device;
    private readonly Recording _recording;
    private readonly ContentStore _store = new();
    private readonly object _gate = new();
    private DeepResult? _result;
    private volatile bool _done;
    private bool _disposed;

    /// <summary>Creates a deep recording for one execution on the device.</summary>
    /// <param name="device">Device the execution runs on.</param>
    /// <param name="mode">What to copy.</param>
    public DeepRecording(GraphicsDevice device, DeepMode mode)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _recording = new Recording(device);
        Mode = mode;
        Backend = device.BackendType;
        Features = RecordedFeatures.From(device.Features);
    }

    internal DeepRecording(DeepMode mode, GraphicsBackend backend, RecordedFeatures features, Recording recording, DeepResult result)
    {
        Mode = mode;
        Backend = backend;
        Features = features;
        _recording = recording;
        _result = result;
        _done = true;
    }

    /// <summary>Id of the recorded execution, or 0 before it is passed to one.</summary>
    public ulong ExecutionId => _recording.ExecutionId;

    /// <summary>The mode it was recorded with.</summary>
    public DeepMode Mode { get; }

    /// <summary>Backend of the recording device.</summary>
    public GraphicsBackend Backend { get; }

    /// <summary>Features of the recording device.</summary>
    public RecordedFeatures Features { get; }

    /// <summary>The light data collected alongside. Its timings include the capture copies.</summary>
    public Recording Recording
    {
        get
        {
            RequireDone();
            return _recording;
        }
    }

    /// <summary>Every buffer and texture referenced, under trace local ids.</summary>
    public EquatableArray<RecordedResource> Resources => Result.Resources;

    /// <summary>Programs, one per key.</summary>
    public EquatableArray<RecordedProgram> Programs => Result.Programs;

    /// <summary>Sampler descriptions that properties index into.</summary>
    public EquatableArray<SamplerDescription> Samplers => Result.Samplers;

    /// <summary>Content keyed by hash.</summary>
    public EquatableArray<RecordedBlob> Blobs => Result.Blobs;

    /// <summary>The views of the execution in order.</summary>
    public EquatableArray<DeepView> Views => Result.Views;

    /// <summary>True once the execution has resolved on the GPU and its copies are read.</summary>
    public bool IsDone
    {
        get
        {
            if (_done)
                return true;

            if (!_recording.IsDone)
                return false;

            lock (_gate)
            {
                if (!_done)
                {
                    _result = Build(_device!);
                    _done = true;
                }

                return true;
            }
        }
    }

    internal DeepResult Result
    {
        get
        {
            RequireDone();
            ObjectDisposedException.ThrowIf(_result == null, this);
            return _result;
        }
    }

    /// <summary>Blocks until the execution has resolved. Throws if the recording was never passed to an execution.</summary>
    public void Wait()
    {
        _recording.Wait();
        _ = IsDone;
    }

    /// <summary>Frees the capture buffers of a recording that was never read. Waits for its execution first. Its data is gone afterwards.</summary>
    public void Dispose()
    {
        if (_device == null)
            return;

        if (_recording.ExecutionId != 0)
            _recording.Wait();

        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_done)
                return;

            Release();
            _done = true;
        }
    }

    private void RequireDone()
    {
        if (!IsDone)
            throw new InvalidOperationException("The recording is not done. Check IsDone or call Wait() first.");
    }

    void IProfiler.BeginExecution(ulong executionId, string graphName) => ((IProfiler)_recording).BeginExecution(executionId, graphName);

    void IProfiler.EndExecution() => ((IProfiler)_recording).EndExecution();

    void IGraphProfiler.BeginView(in ViewInfo view)
    {
        _views[view.Index] = new ViewState(view.Name, view.Index, view.PixelWidth, view.PixelHeight);
        ((IGraphProfiler)_recording).BeginView(in view);
    }

    void IGraphProfiler.EndView(in ViewInfo view) => ((IGraphProfiler)_recording).EndView(in view);

    void IGraphProfiler.BeginPass(in PassInfo pass) => ((IGraphProfiler)_recording).BeginPass(in pass);

    void IGraphProfiler.EndPass(in PassInfo pass, in PassStats stats) => ((IGraphProfiler)_recording).EndPass(in pass, in stats);

    void IGpuStatsProfiler.RecordExecutionTime(in CommandBufferInfo commandBuffer, double milliseconds)
        => ((IGpuStatsProfiler)_recording).RecordExecutionTime(in commandBuffer, milliseconds);

    void IGpuStatsProfiler.RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats)
        => ((IGpuStatsProfiler)_recording).RecordGpuVertexStats(in commandBuffer, in stats);
}
