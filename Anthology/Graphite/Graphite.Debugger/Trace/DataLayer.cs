namespace Prowl.Graphite.Debugger.Trace;

public sealed record TraceProgram(
    TraceProgramId Id,
    string Name,
    BlobRef SpirV,
    BlobRef Reflection);

public sealed record ResourceWriteEvent(
    int PrecedesExecution,
    TraceVersion After,
    TraceRange Range,
    TextureRegion? Region,
    BlobRef? Data);
