namespace Prowl.Graphite.Debugging;

/// <summary>Capture taps and limits. Any subset of the taps may be null.</summary>
public sealed class CaptureSetup
{
    public ICaptureHook? Hook { get; init; }

    public ICommandStreamSink? Commands { get; init; }

    public IResourceWriteSink? Writes { get; init; }

    public ulong ByteBudget { get; init; }
}
