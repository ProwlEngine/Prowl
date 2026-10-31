using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>A world as the frame pipeline draws it: its buckets, its globals region and its frame table.</summary>
internal sealed class WorldFrame(RenderWorld world, BucketSet buckets, int globalRegion, DrawPrograms programs) : IDisposable
{
    /// <summary>The renderer's draw program ids, which the world's providers hold theirs in.</summary>
    public DrawPrograms Programs { get; } = programs;

    private static int s_nextCapture;

    public RenderWorld World { get; } = world;
    public BucketSet Buckets { get; } = buckets;
    public int GlobalRegion { get; } = globalRegion;
    public BufferSet Buffers { get; } = new();

    /// <summary>The draw program of the sky material's Sky pass, held while the world draws it.</summary>
    public ushort? SkyProgram;

    /// <summary>The world's local light shadows, made at its first frame.</summary>
    public WorldShadows? Shadows;

    /// <summary>The views capturing the sky and the reflection probes: six faces at once or one, reused across frames.</summary>
    public CameraFrame? SkyCamera;
    public readonly List<CameraFrame> ProbeCameras = new();
    public readonly List<CameraFrame> ProbeFaceCameras = new();
    public int ProbeCamerasUsed, ProbeFaceCamerasUsed;

    private string? _shadowsName, _shadowsGroup;

    /// <summary>The debug group the world's shadow passes record in, made again when the world is renamed.</summary>
    public string ShadowsGroup(string name)
    {
        if (_shadowsGroup == null || !ReferenceEquals(_shadowsName, name))
        {
            _shadowsName = name;
            _shadowsGroup = $"{name} shadows";
        }
        return _shadowsGroup;
    }

    /// <summary>A new capture view id, below any a host may use.</summary>
    public static int NextCaptureViewId() => int.MinValue + Interlocked.Increment(ref s_nextCapture);

    public IEnumerable<CameraFrame> Captures()
    {
        if (SkyCamera != null)
            yield return SkyCamera;
        foreach (CameraFrame camera in ProbeCameras)
            yield return camera;
        foreach (CameraFrame camera in ProbeFaceCameras)
            yield return camera;
    }

    public void Dispose()
    {
        foreach (CameraFrame camera in Captures())
            camera.Dispose();
    }
}

internal enum PlannedKind
{
    Host,
    Capture,
    Fill,
}

internal readonly record struct PlannedInstance(PlannedKind Kind, WorldFrame World, object Data, string Name);
