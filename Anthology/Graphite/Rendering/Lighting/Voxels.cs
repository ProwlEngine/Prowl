using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>The box of world space a world voxelizes for global illumination, see <see cref="RenderWorld.SetVoxelVolume"/>.</summary>
public struct VoxelVolumeDesc
{
    /// <summary>The point the box centres on, snapped to whole voxels. Hosts move it with the camera the GI is for.</summary>
    public Double3 Center;

    /// <summary>The edge of one voxel in metres.</summary>
    public float VoxelSize;

    /// <summary>Voxels along each axis, multiples of 32 from 32 to 512. 352 at 0.2 m by default, a 70.4 m box.</summary>
    public Int3 Resolution;

    /// <summary>The layers of the renderables drawn into the voxels.</summary>
    public uint CullingMask;

    /// <summary>
    /// Frames the whole box takes to be drawn again, a slab of it a frame, so anything that moved or changed is right
    /// again within this many frames. Space the box scrolls into is drawn the frame it does. 1 draws all of it every frame.
    /// </summary>
    public int UpdateFrames;

    public VoxelVolumeDesc()
    {
        VoxelSize = 0.2f;
        Resolution = new Int3(352);
        CullingMask = ~0u;
        UpdateFrames = 8;
    }
}

public sealed partial class RenderWorld
{
    private VoxelVolumeDesc? _voxelVolume;

    /// <summary>
    /// The box the world voxelizes every frame, or null for none: its renderables' Voxelize passes draw their albedo and
    /// emission into a volume the world's global illumination traces. Host thread.
    /// </summary>
    public void SetVoxelVolume(VoxelVolumeDesc? desc)
    {
        if (desc is { } given)
            Validate(given);
        _voxelVolume = desc;
    }

    public VoxelVolumeDesc? VoxelVolume => _voxelVolume;

    private static void Validate(in VoxelVolumeDesc desc)
    {
        if (!(desc.VoxelSize > 0) || !float.IsFinite(desc.VoxelSize))
            throw new ArgumentException($"A voxel volume needs a positive voxel size, {desc.VoxelSize} was given.", nameof(desc));
        foreach (int axis in (ReadOnlySpan<int>)[desc.Resolution.X, desc.Resolution.Y, desc.Resolution.Z])
        {
            if (axis is < 32 or > 512 || axis % 32 != 0)
                throw new ArgumentException($"A voxel volume's resolution is a multiple of 32 from 32 to 512 on each axis, {desc.Resolution} was given.", nameof(desc));
        }
        if (desc.UpdateFrames < 1)
            throw new ArgumentException($"A voxel volume is drawn over at least one frame, {desc.UpdateFrames} was given.", nameof(desc));
    }

    private void ExtractVoxels() => Voxels.Desc = _voxelVolume;

    internal WorldVoxels Voxels { get; private set; } = null!;
}

/// <summary>The voxel volume's header as the shaders read it, 80 bytes, matching Graphite_VoxelHeader.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct VoxelHeader
{
    public const uint Size = 80;

    public uint ResolutionX, ResolutionY, ResolutionZ, Count;
    public uint WrapX, WrapY, WrapZ, _pad0;
    public float VoxelSize, InverseVoxelSize, EmissionExposure, _pad1;
    public uint Occupancy, Level1, Level2, Level3;
    public uint Level4, Level5, Words, EmptyLevels;
}

/// <summary>A box of a voxel volume's voxels, from Min up to Max, in the box's own coordinates.</summary>
internal readonly record struct VoxelRegion(Int3 Min, Int3 Max)
{
    public int Count => (Max.X - Min.X) * (Max.Y - Min.Y) * (Max.Z - Min.Z);
}

/// <summary>A world's voxel volume as a view's pass reads it, with its radiance cache, which the pass may add entries to, the sun shadow over the box and the sky capture.</summary>
internal readonly record struct VoxelInputs(WorldVoxels Voxels, DeviceBuffer Cache, Texture SunShadow, Texture Sky, Sampler ShadowSampler);

/// <summary>What lights the voxels as the shaders read it, 112 bytes, matching Graphite_VoxelLightingData. Light is pre-exposed by the header's emission exposure.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct VoxelLightingRecord
{
    /// <summary>From voxel coordinates, the box's first corner at the origin, to the sun shadow's clip space.</summary>
    public Float4x4 SunShadow;

    /// <summary>Toward the sun, then 1 while it casts a shadow.</summary>
    public Float4 SunDirection;

    /// <summary>The sun's illuminance, then 1 while there is a sun.</summary>
    public Float4 SunColor;

    /// <summary>The sky's colour while it has no capture, then the capture's mip to read, or -1 without one.</summary>
    public Float4 Sky;
}

/// <summary>
/// The render side of a world's voxel volume: the box snapped to whole voxels around the host's centre and the buffer
/// the Voxelize pass writes, laid out as Graphite/VoxelData.glsl reads it, and what lights it.
/// </summary>
internal sealed class WorldVoxels(TableHost tables)
{
    private readonly PersistentBuffer _buffer = new(RenderResourceID.Intern($"{tables.Prefix}.Voxels"), BufferUsage.StructuredBufferReadWrite);

    /// <summary>One <see cref="VoxelLightingRecord"/>, written each frame the world has a box.</summary>
    public GpuArray Lighting { get; } = tables.Array("Lighting");

    /// <summary>Slots of the radiance cache, matching GRAPHITE_VOXEL_CACHE_SIZE, 32 bytes each. See Graphite/VoxelCache.glsl.</summary>
    public const uint CacheEntries = 1 << 20;

    private readonly PersistentBuffer _cache = new(RenderResourceID.Intern($"{tables.Prefix}.Cache"), BufferUsage.StructuredBufferReadWrite);
    private Int3 _cacheResolution;

    /// <summary>The radiance cache, null while the world has no box.</summary>
    public DeviceBuffer? Cache { get; private set; }

    /// <summary>Whether the cache is emptied before its next update: it is new, the box's resolution or voxel size changed its keys, or it moved past itself.</summary>
    public bool CacheStale;

    /// <summary>Voxels the box scrolled along each axis since the cache last let go of the slabs it scrolled into.</summary>
    public Int3 CacheScrolled;

    /// <summary>One in this many cache slots updates a frame, so each entry every this many frames.</summary>
    public uint CacheStride = 8;

    /// <summary>Counts the cache's updates, picking the slots and seeding their rays.</summary>
    public uint CacheFrame;

    /// <summary>The host's box as of the last extraction, null for none.</summary>
    public VoxelVolumeDesc? Desc;

    /// <summary>The voxel buffer, null while the world has no box.</summary>
    public DeviceBuffer? Data { get; private set; }

    public VoxelHeader Header;

    /// <summary>The box's first voxel in world voxel coordinates.</summary>
    public (long X, long Y, long Z) Origin { get; private set; }

    /// <summary>The middle of the box in world space, where the voxel view sits.</summary>
    public Double3 Center { get; private set; }

    /// <summary>The box's size in metres.</summary>
    public Float3 Extent { get; private set; }

    /// <summary>Snaps the box, lays out its header and makes its buffer, or lets it go while the world has none. Render thread.</summary>
    public void Update(float emissionExposure)
    {
        if (Desc is not { } desc)
        {
            _buffer.Release(tables.Store);
            _drawn = null;
            _cache.Release(tables.Store);
            Data = null;
            Cache = null;
            return;
        }

        DeviceBuffer? cache = Cache;
        Cache = _cache.Resolve(tables.Device, tables.Store, CacheEntries * 32);
        CacheStale |= Cache != cache || desc.Resolution != _cacheResolution;
        _cacheResolution = desc.Resolution;

        Int3 r = desc.Resolution;
        double size = desc.VoxelSize;
        long x = (long)Math.Floor(desc.Center.X / size) - r.X / 2;
        long y = (long)Math.Floor(desc.Center.Y / size) - r.Y / 2;
        long z = (long)Math.Floor(desc.Center.Z / size) - r.Z / 2;
        Origin = (x, y, z);
        DeviceBuffer? data = Data;
        Center = new Double3((x + r.X / 2) * size, (y + r.Y / 2) * size, (z + r.Z / 2) * size);
        Extent = new Float3(r.X, r.Y, r.Z) * desc.VoxelSize;

        uint count = (uint)(r.X * r.Y * r.Z);
        uint at = count;
        uint occupancy = at;
        at += (count + 3) / 4;
        Span<uint> levels = stackalloc uint[5];
        for (int level = 1; level <= 5; level++)
        {
            levels[level - 1] = at;
            at += ((count >> (3 * level)) + 31) / 32;
        }
        uint emptyLevels = at;
        at += ((count >> 3) + 3) / 4;

        Header = new VoxelHeader
        {
            ResolutionX = (uint)r.X, ResolutionY = (uint)r.Y, ResolutionZ = (uint)r.Z, Count = count,
            WrapX = Wrap(x, r.X), WrapY = Wrap(y, r.Y), WrapZ = Wrap(z, r.Z),
            VoxelSize = desc.VoxelSize, InverseVoxelSize = 1f / desc.VoxelSize, EmissionExposure = emissionExposure,
            Occupancy = occupancy, Level1 = levels[0], Level2 = levels[1], Level3 = levels[2],
            Level4 = levels[3], Level5 = levels[4], Words = at, EmptyLevels = emptyLevels,
        };
        Data = _buffer.Resolve(tables.Device, tables.Store, VoxelHeader.Size + at * 4);
        PlanRegions(desc, Data != data);
    }

    private static uint Wrap(long coordinate, int resolution) => (uint)(((coordinate % resolution) + resolution) % resolution);

    /// <summary>The parts of the box, in its voxels, cleared and drawn again this frame.</summary>
    public readonly List<VoxelRegion> Regions = new();

    private (long X, long Y, long Z) _drawnOrigin;
    private VoxelVolumeDesc? _drawn;
    private int _roll;

    // All of the box when it is new, changed shape or moved past itself, else the slabs it scrolled into and the next
    // slab of its rolling redraw along its longest axis
    private void PlanRegions(in VoxelVolumeDesc desc, bool fresh)
    {
        Regions.Clear();
        Int3 r = desc.Resolution;
        (long X, long Y, long Z) d = (Origin.X - _drawnOrigin.X, Origin.Y - _drawnOrigin.Y, Origin.Z - _drawnOrigin.Z);
        bool moved = _drawn is not { } drawn || drawn.Resolution != r || drawn.VoxelSize != desc.VoxelSize
            || Math.Abs(d.X) >= r.X || Math.Abs(d.Y) >= r.Y || Math.Abs(d.Z) >= r.Z;
        bool whole = fresh || desc.UpdateFrames <= 1 || moved;
        if (moved)
        {
            CacheStale = true;
            CacheScrolled = Int3.Zero;
        }
        else
        {
            CacheScrolled += new Int3((int)d.X, (int)d.Y, (int)d.Z);
            CacheStale |= Math.Abs(CacheScrolled.X) >= r.X || Math.Abs(CacheScrolled.Y) >= r.Y || Math.Abs(CacheScrolled.Z) >= r.Z;
        }
        _drawn = desc;
        _drawnOrigin = Origin;
        if (whole)
        {
            Regions.Add(new VoxelRegion(Int3.Zero, r));
            return;
        }

        Scrolled(0, d.X, r);
        Scrolled(1, d.Y, r);
        Scrolled(2, d.Z, r);
        int axis = r.X >= r.Y && r.X >= r.Z ? 0 : r.Y >= r.Z ? 1 : 2;
        int length = axis == 0 ? r.X : axis == 1 ? r.Y : r.Z;
        int slab = (length + desc.UpdateFrames - 1) / desc.UpdateFrames;
        int start = _roll++ % desc.UpdateFrames * slab;
        if (start < length)
            Regions.Add(Slab(axis, start, Math.Min(start + slab, length), r));
    }

    private void Scrolled(int axis, long moved, Int3 r)
    {
        int length = axis == 0 ? r.X : axis == 1 ? r.Y : r.Z;
        if (moved > 0)
            Regions.Add(Slab(axis, length - (int)moved, length, r));
        else if (moved < 0)
            Regions.Add(Slab(axis, 0, (int)-moved, r));
    }

    private static VoxelRegion Slab(int axis, int start, int end, Int3 r) => axis switch
    {
        0 => new VoxelRegion(new Int3(start, 0, 0), new Int3(end, r.Y, r.Z)),
        1 => new VoxelRegion(new Int3(0, start, 0), new Int3(r.X, end, r.Z)),
        _ => new VoxelRegion(new Int3(0, 0, start), new Int3(r.X, r.Y, end)),
    };
}
