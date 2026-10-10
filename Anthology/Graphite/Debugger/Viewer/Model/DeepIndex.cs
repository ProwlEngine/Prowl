using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;

namespace Prowl.Graphite.Debugger.Viewer;

internal sealed class DeepIndex
{
    private readonly DeepRecording _recording;
    private readonly Dictionary<uint, RecordedResource> _resources = new();
    private readonly Dictionary<string, RecordedResource> _resourcesByName = new();
    private readonly Dictionary<ProgramKey, RecordedProgram> _programs = new();
    private readonly Dictionary<BlobRef, byte[]> _blobs = new();

    public DeepIndex(DeepRecording recording)
    {
        _recording = recording;
        Dictionary<TraceResourceId, string> graphNames = new();
        foreach (DeepView view in recording.Views)
            foreach (RecordedGraphResource graph in view.Resources)
                foreach (RecordedBacking backing in graph.Backings)
                    graphNames.TryAdd(backing.Id, backing.Role == BackingRole.Depth ? $"{graph.Name} (depth)" : graph.Name);

        foreach (RecordedResource recorded in recording.Resources)
        {
            RecordedResource resource = recorded.Name.Length == 0 && graphNames.TryGetValue(recorded.Id, out string? name) ? recorded with { Name = name } : recorded;
            _resources[resource.Id.Value] = resource;
            _resourcesByName.TryAdd(resource.Name, resource);
        }
        foreach (RecordedProgram program in recording.Programs)
            _programs[program.Key] = program;
        foreach (RecordedBlob blob in recording.Blobs)
            _blobs[blob.Ref] = blob.Data.Items.ToArray();
    }

    public int ProgramCount => _programs.Count;

    public RecordedResource? Resource(TraceResourceId id) => _resources.GetValueOrDefault(id.Value);

    public RecordedResource? Resource(string name) => _resourcesByName.GetValueOrDefault(name);

    public RecordedProgram? Program(ProgramKey key) => _programs.GetValueOrDefault(key);

    public byte[]? Blob(BlobRef blob) => _blobs.GetValueOrDefault(blob);

    public (RecordedCopy Copy, byte[] Data)? Contents(TraceResourceId id, DeepPass? prefer)
    {
        if (prefer != null && Find(prefer, id) is { } own)
            return own;

        foreach (DeepView view in _recording.Views)
            foreach (DeepPass pass in view.Passes)
                if (Find(pass, id) is { } found)
                    return found;
        return null;
    }

    private (RecordedCopy, byte[])? Find(DeepPass pass, TraceResourceId id)
    {
        foreach (RecordedCopy copy in pass.Copies)
            if (copy.Version.Resource == id && Blob(copy.Blob) is { } data)
                return (copy, data);
        return null;
    }
}
