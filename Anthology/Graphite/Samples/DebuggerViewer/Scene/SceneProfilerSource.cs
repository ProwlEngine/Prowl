using System.Diagnostics;

using Prowl.Echo;
using Prowl.Graphite;
using Prowl.Graphite.Debugger;
using Prowl.Graphite.Debugger.Serialization;
using Prowl.Graphite.Debugger.Viewer;
using Prowl.Vector;

namespace DebuggerViewer.Scene;

/// <summary>Records every execution of the offscreen scene and serves captures and replays of it to the viewer.</summary>
internal sealed class SceneProfilerSource : IRenderProfilerSource
{
    private const int Window = 256;

    private sealed record Pending(Recording Recording, double DispatchMilliseconds, double FrameMilliseconds);

    private readonly GraphicsDevice _device;
    private readonly OffscreenScene _scene;
    private readonly List<Recording> _frames = new();
    private readonly List<Pending> _pending = new();
    private readonly Dictionary<Recording, FrameTiming> _timings = new();
    private Replayer? _replayer;
    private float _captureTime;

    public SceneProfilerSource(GraphicsDevice device, OffscreenScene scene)
    {
        _device = device;
        _scene = scene;
        DebuggerSerialization.Register();
    }

    public bool IsRecording { get; set; } = true;

    public IReadOnlyList<Recording> Frames => _frames;

    public Recording? Latest { get; private set; }

    public double TotalMilliseconds { get; private set; }

    public long TotalFrames { get; private set; }

    public DeepRecording? Snapshot { get; private set; }

    public FrameTiming? SnapshotTiming { get; private set; }

    /// <summary>Collects finished recordings, then renders and records the next scene frame while recording.</summary>
    public void Update(float deltaSeconds)
    {
        Collect();
        if (!IsRecording)
            return;

        _scene.Advance(deltaSeconds);
        Recording recording = new(_device);
        long start = Stopwatch.GetTimestamp();
        _scene.Dispatch(recording);
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
        List<CpuStage> stages = [new CpuStage("DispatchGraph", dispatchMilliseconds, "Main")];
        foreach (RecordedView view in recording.Views)
            foreach (RecordedPass pass in view.Passes)
                stages.Add(new CpuStage($"{view.Name}/{pass.Name}", pass.Stats.CpuMilliseconds, "Main"));

        List<ViewVisibility> visibility = recording.Views
            .Select(v => new ViewVisibility(v.Name, _scene.Cubes.Count, 0, 0, 1, 0, 0))
            .ToList();

        return new FrameTiming(
            dispatchMilliseconds,
            recording.CommandBuffers.Sum(b => b.Milliseconds ?? 0),
            frameMilliseconds,
            0,
            recording.CommandBuffers.Length,
            stages,
            visibility);
    }

    public void Capture(int frame, DeepMode mode)
    {
        DeepRecording recording = new(_device, mode);
        long start = Stopwatch.GetTimestamp();
        _scene.Dispatch(recording);
        double dispatch = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        recording.Wait();
        Open(recording, Measure(recording.Recording, dispatch, Timing(frame).FrameMilliseconds), _scene.Time);
    }

    private void Open(DeepRecording recording, FrameTiming timing, float time)
    {
        Snapshot?.Dispose();
        Snapshot = recording;
        SnapshotTiming = timing;
        _replayer = null;
        _captureTime = time;
    }

    public ReplayResult Replay(ReplayRequest request)
    {
        if (Snapshot == null)
            throw new InvalidOperationException("There is no snapshot to replay.");
        _replayer ??= new Replayer(_device, Snapshot);
        return _replayer.Replay(request);
    }

    public RendererInfo? Renderer(string marker)
    {
        SceneCube? cube = _scene.Cubes.FirstOrDefault(c => c.Name == marker);
        if (Snapshot == null || cube == null)
            return null;

        Quaternion rotation = Quaternion.AngleAxis(cube.Phase + _captureTime * cube.Speed, cube.Axis);
        Float3 eye = OffscreenScene.Eye(_captureTime);
        float distance = (float)Float3.Length(cube.Position - eye);
        float radius = (float)Float3.Length(cube.Scale) * 0.5f;
        float projected = radius / (distance * MathF.Tan(OffscreenScene.FieldOfView * 0.5f)) * OffscreenScene.Height * 0.5f;
        float coverage = MathF.Min(100f, MathF.PI * projected * projected / (OffscreenScene.Width * OffscreenScene.Height) * 100f);

        MaterialInfo material = new(
            "Lit",
            "Geometry",
            false,
            true,
            OffscreenScene.ConstantBytes,
            [
                ("Color", "Float4", Format(cube.Color)),
                ("LightDirection", "Float4", Format(new Float4(OffscreenScene.LightDirection, 0f))),
                ("LightColor", "Float4", Format(OffscreenScene.LightColor)),
                ("Ambient", "Float4", Format(OffscreenScene.Ambient)),
            ],
            []);

        return new RendererInfo(cube.Name, "MeshRenderer", _scene.Cubes.ToList().IndexOf(cube), "Default", cube.Speed == 0 ? "Static" : "None",
            false, false, "Off", -1, 0, cube.Position, rotation.EulerAngles, cube.Scale, coverage, distance, material);
    }

    private static string Format(Float4 v) => $"({v.X:0.##}, {v.Y:0.##}, {v.Z:0.##}, {v.W:0.##})";

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
        Open(recording, Measure(recording.Recording, recording.Recording.Views.Sum(v => v.Passes.Sum(p => p.Stats.CpuMilliseconds)), 0), _scene.Time);
    }
}
