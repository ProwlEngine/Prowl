using System;
using Prowl.Graphite.Debugger.Trace;

namespace Prowl.Graphite.Debugger;

/// <summary>Everything needed to replay the executions of one recording. Reading data before it is done throws.</summary>
public sealed class DeepRecording : RecordingHandle
{
    private readonly GraphicsDevice _device;
    private readonly Recording _recording;
    private DeepSink? _sink;
    private DeepResult? _result;

    internal DeepRecording(GraphicsDevice device, DeepSink sink, RecordingHandle? previous)
        : base(previous)
    {
        _device = device;
        _sink = sink;
        Mode = sink.Mode;
        Backend = device.BackendType;
        Features = device.Features;
        _recording = new Recording(device, sink.Light, null);
    }

    /// <summary>The mode it was recorded with.</summary>
    public DeepMode Mode { get; }

    /// <summary>Backend of the recording device.</summary>
    public GraphicsBackend Backend { get; }

    /// <summary>Features of the recording device.</summary>
    public GraphicsDeviceFeatures Features { get; }

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

    /// <summary>The recorded executions in start order.</summary>
    public EquatableArray<DeepExecution> Executions => Result.Executions;

    private DeepResult Result
    {
        get
        {
            RequireDone();
            return _result!;
        }
    }

    internal override bool PollReady() => _recording.IsDone;

    internal override bool WaitForExecutions() => _recording.WaitForExecutions();

    internal override void Finish()
    {
        _result = _sink!.Build(_device);
        _sink = null;
    }
}
