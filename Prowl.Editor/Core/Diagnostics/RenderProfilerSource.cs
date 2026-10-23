// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Graphite;
using Prowl.Graphite.Debugger;
using Prowl.Graphite.Debugger.Serialization;
using Prowl.Graphite.Debugger.Viewer;
using Prowl.Graphite.Debugging;
using Prowl.OrigamiUI;
using Prowl.Runtime;

namespace Prowl.Editor.Core.Diagnostics;

/// <summary>
/// Feeds Graphite's render profiler from the editor's frames. A frame is recorded only while one of the profiler panels
/// is open, and a snapshot is the next frame executed with a deep recording.
/// </summary>
internal sealed class RenderProfilerSource : IRenderProfilerSource
{
    private const int Window = 256;

    private sealed record Pending(Recording Recording, double FrameMilliseconds);

    private readonly List<Recording> _frames = [];
    private readonly List<Pending> _pending = [];
    private readonly Dictionary<Recording, FrameTiming> _timings = [];
    private Replayer? _replayer;
    private DeepMode? _captureRequest;
    private DeepRecording? _capturing;
    private double _captureFrameMilliseconds;

    public RenderProfilerSource()
    {
        DebuggerSerialization.Register();
    }

    /// <summary>The panels and the dock they live in, set by <see cref="Attach"/>.</summary>
    public RenderProfilerWindows? Windows { get; private set; }

    public bool IsRecording { get; set; } = true;

    public IReadOnlyList<Recording> Frames => _frames;

    public int FrameCapacity => Window;

    public Recording? Latest { get; private set; }

    public double TotalMilliseconds { get; private set; }

    public long TotalFrames { get; private set; }

    public DeepRecording? Snapshot { get; private set; }

    public FrameTiming? SnapshotTiming { get; private set; }

    /// <summary>Creates the panels in <paramref name="dock"/> and starts profiling the editor's frames.</summary>
    public void Attach(DockSpace dock)
    {
        Windows = new RenderProfilerWindows(this, dock);
        Graphics.FrameProfilers = FrameProfilers;
    }

    private bool PanelsOpen
    {
        get
        {
            EditorApplication? editor = EditorApplication.Instance;
            return editor != null && (editor.IsPanelOpen(typeof(LiveProfilerPanel)) || editor.IsPanelOpen(typeof(SnapshotViewerPanel)));
        }
    }

    private IProfiler[]? FrameProfilers()
    {
        Collect();
        GraphicsDevice? device = Graphics.Device;
        if (device == null)
            return null;

        double frameMilliseconds = Time.CurrentTime.UnscaledDeltaTime * 1000.0;
        if (_captureRequest is { } mode && _capturing == null)
        {
            _captureRequest = null;
            _captureFrameMilliseconds = frameMilliseconds;
            _capturing = new DeepRecording(device, mode);
            return [_capturing];
        }

        if (!IsRecording || !PanelsOpen)
            return null;

        Recording recording = new(device);
        Latest = recording;
        _pending.Add(new Pending(recording, frameMilliseconds));
        return [recording];
    }

    private void Collect()
    {
        if (_capturing is { IsDone: true } done)
        {
            _capturing = null;
            Open(done, Measure(done.Recording, _captureFrameMilliseconds));
        }

        while (_pending.Count > 0 && _pending[0].Recording.IsDone)
        {
            Pending pending = _pending[0];
            _pending.RemoveAt(0);
            _timings[pending.Recording] = Measure(pending.Recording, pending.FrameMilliseconds);
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

    private static FrameTiming Measure(Recording recording, double frameMilliseconds)
    {
        List<CpuStage> stages = [];
        foreach (RecordedGroup group in recording.Groups)
            foreach (RecordedPass pass in group.Passes)
                stages.Add(new CpuStage($"{group.Name}/{pass.Name}", pass.Stats.CpuMilliseconds, "Render"));

        return new FrameTiming(
            stages.Sum(s => s.Milliseconds),
            recording.CommandBuffers.Where(b => !b.Name.StartsWith(CaptureCopy.BufferPrefix, StringComparison.Ordinal)).Sum(b => b.Milliseconds ?? 0),
            frameMilliseconds,
            0,
            recording.CommandBuffers.Length,
            stages,
            []);
    }

    /// <summary>Executes the next frame with a deep recording, which opens as the snapshot once its frame is done.</summary>
    public void Capture(int frame, DeepMode mode) => _captureRequest = mode;

    private void Open(DeepRecording recording, FrameTiming timing)
    {
        Snapshot?.Dispose();
        Snapshot = recording;
        SnapshotTiming = timing;
        _replayer = null;
    }

    public ReplayResult Replay(ReplayRequest request)
    {
        if (Snapshot == null || Graphics.Device == null)
            throw new InvalidOperationException("There is no snapshot to replay.");
        _replayer ??= new Replayer(Graphics.Device, Snapshot);
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
        Open(recording, Measure(recording.Recording, 0));
    }
}
