using System.Collections.Generic;

namespace Prowl.Graphite.Debugger.Viewer;

/// <summary>Feeds the render profiler with recordings, captures and replays.</summary>
/// <remarks>Frames is the live window, oldest first. Timing is engine data that only fills extra inspector rows.</remarks>
public interface IRenderProfilerSource
{
    bool IsRecording { get; set; }

    IReadOnlyList<Recording> Frames { get; }

    /// <summary>How many frames Frames holds when full, or 0 when unknown.</summary>
    int FrameCapacity => 0;

    Recording? Latest { get; }

    double TotalMilliseconds { get; }

    long TotalFrames { get; }

    DeepRecording? Snapshot { get; }

    FrameTiming? SnapshotTiming { get; }

    FrameTiming Timing(int frame);

    void Capture(int frame, DeepMode mode);

    ReplayResult Replay(ReplayRequest request);

    void SaveSnapshot(string path);

    void LoadSnapshot(string path);
}

/// <summary>Engine side timing and visibility for one execution.</summary>
public sealed record FrameTiming(
    double CpuMilliseconds,
    double GpuMilliseconds,
    double FrameMilliseconds,
    double SubmitLatencyMilliseconds,
    int QueueSubmits,
    IReadOnlyList<CpuStage> CpuStages,
    IReadOnlyList<ViewVisibility> Visibility);

/// <summary>A slice of the CPU side of a frame.</summary>
public sealed record CpuStage(string Name, double Milliseconds, string Thread);

/// <summary>Culling results for one view.</summary>
public sealed record ViewVisibility(string View, int Visible, int CulledFrustum, int CulledOcclusion, int Lights, int ShadowCasters, double CullingMilliseconds);
