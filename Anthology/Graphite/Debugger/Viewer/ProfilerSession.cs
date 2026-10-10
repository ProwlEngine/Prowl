using System.Collections.Generic;

namespace Prowl.Graphite.Debugger.Viewer;

internal sealed class Snapshot
{
    public required int Id;
    public required DeepRecording Recording;
    public required FrameModel Model;
    public string Source = "";
}

internal sealed class ProfilerSession
{
    private int _nextId = 1;

    public ProfilerSession(IRenderProfilerSource source)
    {
        Source = source;
    }

    public IRenderProfilerSource Source { get; }

    public List<Snapshot> Snapshots { get; } = new();

    public Snapshot? Selected { get; set; }

    public Snapshot Add(DeepRecording recording, string origin)
    {
        Snapshot snapshot = new()
        {
            Id = _nextId++,
            Recording = recording,
            Model = FrameModel.Build(recording.Recording, Source.Timing(recording.Recording), recording),
            Source = origin,
        };
        Snapshots.Add(snapshot);
        Selected = snapshot;
        return snapshot;
    }
}
