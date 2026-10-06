using System;
using Prowl.Graphite.RenderGraph;

namespace Prowl.Graphite.Debugger.Trace;

[Flags]
public enum PassMarks : byte
{
    None = 0,
    MissingRequiredSnapshot = 1 << 0,
    UndeclaredGpuWrite = 1 << 1,
}

public sealed record TraceResource(
    TraceResourceId Id,
    string Name,
    ResourceKind Kind,
    bool Imported,
    GraphTextureDesc? Texture,
    GraphBufferDesc? Buffer,
    TraceVersion EntryVersion,
    SnapshotRef? FirstRead)
{
    public bool Equals(TraceResource? other)
    {
        if (other is null)
        {
            return false;
        }

        return Id == other.Id
            && Name == other.Name
            && Kind == other.Kind
            && Imported == other.Imported
            && TextureEquals(Texture, other.Texture)
            && Buffer.Equals(other.Buffer)
            && EntryVersion == other.EntryVersion
            && FirstRead.Equals(other.FirstRead);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, Name, Kind, Imported, Buffer, EntryVersion);
    }

    private static bool TextureEquals(GraphTextureDesc? a, GraphTextureDesc? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        GraphTextureDesc x = a.Value;
        GraphTextureDesc y = b.Value;
        return x.SizeMode == y.SizeMode
            && x.Scale == y.Scale
            && x.Width == y.Width
            && x.Height == y.Height
            && x.EnableDepth == y.EnableDepth
            && x.DepthFormat == y.DepthFormat
            && new ReadOnlySpan<PixelFormat>(x.ColorFormats).SequenceEqual(y.ColorFormats);
    }
}

public sealed record TraceGhost(
    TraceResourceId Id,
    string Name,
    TraceVersion EntryVersion,
    TextureDescription? Texture,
    BufferDescription? Buffer,
    SnapshotRef Contents);

public sealed record TraceAccess(
    TraceResourceId Resource,
    ResourceKind Kind,
    bool IsOutput,
    TextureState TextureUsage,
    TextureState? DepthUsage,
    BufferAccess BufferUsage);

public sealed record TraceLoadedAttachment(TraceUse Use, SnapshotRef Contents);

public sealed record TracePass(
    string Name,
    int Index,
    EquatableArray<TraceAccess> Accesses,
    EquatableArray<TraceUse> Inputs,
    EquatableArray<TraceUse> Outputs,
    EquatableArray<TraceLoadedAttachment> LoadedAttachments,
    EquatableArray<TraceResourceId> NewExternals,
    bool IsCheckpoint,
    EquatableArray<SnapshotRef> OutputSnapshots,
    PassMarks Marks,
    EquatableArray<TraceCommand> Commands);

public sealed record TraceExecution(
    ulong ExecutionId,
    string ViewName,
    int ViewIndex,
    uint ViewPixelWidth,
    uint ViewPixelHeight,
    EquatableArray<TraceResource> Resources,
    EquatableArray<TraceGhost> Ghosts,
    EquatableArray<TracePass> Passes);

public sealed record TraceDocument(
    EquatableArray<TraceExecution> Executions,
    EquatableArray<TraceProgram> Programs,
    EquatableArray<ResourceWriteEvent> Writes);
