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
    public readonly List<CameraFrame> ProbeCameras = [];
    public readonly List<CameraFrame> ProbeFaceCameras = [];
    public int ProbeCamerasUsed, ProbeFaceCamerasUsed;

    /// <summary>The view drawing the world's voxel volume and the three layer target it rasterizes into, while it has a box.</summary>
    public CameraFrame? VoxelCamera;
    public RenderTexture? VoxelTarget { get; private set; }

    /// <summary>The views drawing this frame's regions of the voxel box, each culled to its region, reused across frames.</summary>
    public readonly List<CameraFrame> VoxelDrawCameras = [];
    public int VoxelDrawCamerasUsed;

    public void EnsureVoxelTarget(GraphicsDevice device, RenderStore store, uint size)
    {
        if (VoxelTarget is { } current && current.Desc.Width == size)
            return;
        if (VoxelTarget != null)
            store.Retire(VoxelTarget);
        VoxelTarget = new RenderTexture(device, PersistentTexture.Describe(GraphTextureDesc.Sized((int)size, (int)size, PixelFormat.R8_UNorm) with { ArrayLayers = 3 }))
        {
            Name = $"{World.Name} voxel target",
        };
    }

    /// <summary>
    /// The sun's shadow over the voxel box, drawn again as often as the box is, at once when the sun or the box's shape
    /// changes, and cleared when the sun casts none.
    /// </summary>
    public CameraFrame? VoxelShadowCamera;
    public RenderTexture? VoxelShadow { get; private set; }
    public readonly List<ShadowDraw> VoxelShadowDraws = [];

    /// <summary>Whether the shadow's pass runs this frame. Off, the map keeps what it last drew.</summary>
    public bool VoxelShadowRedraw;

    /// <summary>The map's clip space and the point it was drawn around, kept for the frames it is read undrawn.</summary>
    public Float4x4 VoxelShadowShape;
    public Double3 VoxelShadowCenter;

    /// <summary>Frames since the map was drawn, -1 while it holds nothing to keep.</summary>
    public int VoxelShadowAge = -1;

    public void EnsureVoxelShadow(GraphicsDevice device, RenderStore store, int size, PixelFormat format)
    {
        if (VoxelShadow is { } current && current.Desc.Width == size && current.DepthTexture!.Format == format)
            return;
        if (VoxelShadow != null)
            store.Retire(VoxelShadow);
        VoxelShadow = new RenderTexture(device, PersistentTexture.Describe(ShadowRecorder.Desc(size, format)))
        {
            Name = $"{World.Name} voxel sun shadow",
        };
        VoxelShadowAge = -1;
    }

    public void ReleaseVoxelView(RenderStore store)
    {
        if (VoxelShadow != null)
            store.Retire(VoxelShadow);
        VoxelShadow = null;
        VoxelShadowDraws.Clear();
        if (VoxelTarget != null)
            store.Retire(VoxelTarget);
        VoxelTarget = null;
        if (VoxelCamera != null)
            VoxelCamera.VoxelTarget = null;
    }

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
        if (VoxelCamera != null)
            yield return VoxelCamera;
        foreach (CameraFrame camera in VoxelDrawCameras)
            yield return camera;
        if (VoxelShadowCamera != null)
            yield return VoxelShadowCamera;
    }

    public void Dispose()
    {
        foreach (CameraFrame camera in Captures())
            camera.Dispose();
        VoxelTarget?.Dispose();
        VoxelShadow?.Dispose();
    }
}

internal enum PlannedKind
{
    Host,
    Capture,
    Fill,
}

internal readonly record struct PlannedInstance(PlannedKind Kind, WorldFrame World, object Data, string Name);
