namespace Prowl.Graphite.Debugger.Trace;

public readonly record struct TraceResourceId(uint Value);

public readonly record struct TraceVersion(TraceResourceId Resource, uint Version);

public readonly record struct BlobRef(EquatableArray<byte> Hash, ulong Length);
