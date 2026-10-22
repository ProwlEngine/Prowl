using System.Diagnostics;

using Prowl.Echo;
using Prowl.Graphite;
using Prowl.Graphite.Debugger;
using Prowl.Graphite.Debugger.Serialization;
using Prowl.Graphite.Debugger.Viewer;
using Prowl.Vector;

namespace DebuggerViewer.Scene;

/// <summary>Records every frame of the PBR scenes and serves captures and replays of them to the viewer.</summary>
internal sealed class SceneProfilerSource : IRenderProfilerSource
{
    private const int Window = 256;

    private sealed record Pending(Recording Recording, double DispatchMilliseconds, double FrameMilliseconds);

    private readonly GraphicsDevice _device;
    private readonly PbrScenes _scene;
    private readonly List<Recording> _frames = new();
    private readonly List<Pending> _pending = new();
    private readonly Dictionary<Recording, FrameTiming> _timings = new();
    private Replayer? _replayer;

    public SceneProfilerSource(GraphicsDevice device, PbrScenes scene)
    {
        _device = device;
        _scene = scene;
        DebuggerSerialization.Register();
    }

    public bool IsRecording { get; set; } = true;

    public IReadOnlyList<Recording> Frames => _frames;

    public int FrameCapacity => Window;

    public Recording? Latest { get; private set; }

    public double TotalMilliseconds { get; private set; }

    public long TotalFrames { get; private set; }

    public DeepRecording? Snapshot { get; private set; }

    public FrameTiming? SnapshotTiming { get; private set; }

    /// <summary>Collects finished recordings, then renders the next scene frame, recording it while recording.</summary>
    public void Update(float deltaSeconds)
    {
        Collect();
        _scene.Advance(deltaSeconds);
        if (!IsRecording)
        {
            _scene.Render(deltaSeconds);
            return;
        }

        Recording recording = new(_device);
        long start = Stopwatch.GetTimestamp();
        _scene.Render(deltaSeconds, recording);
        Latest = recording;
        _pending.Add(new Pending(recording, Stopwatch.GetElapsedTime(start).TotalMilliseconds, deltaSeconds * 1000.0));
    }

    private void Collect()
    {
        while (_pending.Count > 0 && _pending[0].Recording.IsDone)
        {
            Pending pending = _pending[0];
            _pending.RemoveAt(0);
            _timings[pending.Recording] = Measure(pending.Recording, pending.DispatchMilliseconds, pending.FrameMilliseconds);
            _frames.Add(pending.Recording);
            TotalMilliseconds += pending.FrameMilliseconds;
            TotalFrames++;

            if (_frames.Count > Window)
            {
                _timings.Remove(_frames[0]);
                _frames.RemoveAt(0);
            }
        }
    }

    public FrameTiming Timing(int frame) => _timings[_frames[frame]];

    private FrameTiming Measure(Recording recording, double dispatchMilliseconds, double frameMilliseconds)
    {
        List<CpuStage> stages = [new CpuStage("Execute", dispatchMilliseconds, "Main")];
        foreach (RecordedGroup group in recording.Groups)
            foreach (RecordedPass pass in group.Passes)
                stages.Add(new CpuStage($"{group.Name}/{pass.Name}", pass.Stats.CpuMilliseconds, "Main"));


        return new FrameTiming(
            dispatchMilliseconds,
            recording.CommandBuffers.Sum(b => b.Milliseconds ?? 0),
            frameMilliseconds,
            0,
            recording.CommandBuffers.Length,
            stages,
            []);
    }

    public void Capture(int frame, DeepMode mode)
    {
        DeepRecording recording = new(_device, mode);
        long start = Stopwatch.GetTimestamp();
        _scene.Render(0f, recording);
        double dispatch = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        recording.Wait();
        Open(recording, Measure(recording.Recording, dispatch, Timing(frame).FrameMilliseconds));
    }

    private void Open(DeepRecording recording, FrameTiming timing)
    {
        Snapshot?.Dispose();
        Snapshot = recording;
        SnapshotTiming = timing;
        _replayer = null;
    }

    public ReplayResult Replay(ReplayRequest request)
    {
        if (Snapshot == null)
            throw new InvalidOperationException("There is no snapshot to replay.");
        _replayer ??= new Replayer(_device, Snapshot);
        return _replayer.Replay(request);
    }

    public void SaveSnapshot(string path)
    {
        if (Snapshot == null)
            throw new InvalidOperationException("There is no snapshot to save.");
        using BinaryWriter writer = new(File.Create(path));
        Serializer.Serialize(Snapshot).WriteToBinary(writer);
    }

    public void LoadSnapshot(string path)
    {
        DeepRecording recording;
        using (BinaryReader reader = new(File.OpenRead(path)))
            recording = Serializer.Deserialize<DeepRecording>(EchoObject.ReadFromBinary(reader))
                ?? throw new InvalidDataException($"{path} is not a Graphite debugger recording.");
        Open(recording, Measure(recording.Recording, recording.Recording.Groups.Sum(g => g.Passes.Sum(p => p.Stats.CpuMilliseconds)), 0));
    }
}
