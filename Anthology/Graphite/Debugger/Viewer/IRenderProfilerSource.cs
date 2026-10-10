using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Graphite.Debugger.Viewer;

/// <summary>Feeds the render profiler with recordings, captures and replays.</summary>
/// <remarks>Frames is the live window, oldest first. Engine data is optional and only fills extra inspector rows.</remarks>
public interface IRenderProfilerSource
{
    bool IsRecording { get; set; }

    IReadOnlyList<Recording> Frames { get; }

    double TotalMilliseconds { get; }

    long TotalFrames { get; }

    FrameTiming Timing(Recording frame);

    DeepRecording Capture(Recording frame, DeepMode mode);

    ReplayResult Replay(DeepRecording recording, ReplayRequest request);

    RendererInfo? Renderer(DeepRecording recording, string marker);

    void Save(DeepRecording recording, string path);

    DeepRecording Load(string path);
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

/// <summary>Engine data for the renderer behind a debug marker.</summary>
public sealed record RendererInfo(
    string Name,
    string RendererType,
    int InstanceId,
    string Layer,
    string StaticFlags,
    bool CastShadows,
    bool ReceiveShadows,
    string LightProbes,
    int LightmapIndex,
    int SortingOrder,
    Float3 Position,
    Float3 Rotation,
    Float3 Scale,
    float ScreenCoverage,
    float CameraDistance,
    MaterialInfo Material);

/// <summary>Engine data for a material.</summary>
public sealed record MaterialInfo(
    string Name,
    string RenderQueue,
    bool Instancing,
    bool DoubleSided,
    int ConstantBufferBytes,
    IReadOnlyList<(string Name, string Type, string Value)> Properties,
    IReadOnlyList<(string Slot, string Texture, int Size, string Format, int Mips, long Bytes)> Textures);
