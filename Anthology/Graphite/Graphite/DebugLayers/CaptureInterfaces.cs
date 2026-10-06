namespace Prowl.Graphite.Debugging;

/// <summary>Graph level capture tap. Members arrive with the graph hook step.</summary>
public interface ICaptureHook
{
}

/// <summary>Per command tap for pass command buffers. Members arrive with the command stream sink step.</summary>
public interface ICommandStreamSink
{
}

/// <summary>Tap for content writes outside a pass. Members arrive with the resource write sink step.</summary>
public interface IResourceWriteSink
{
}
