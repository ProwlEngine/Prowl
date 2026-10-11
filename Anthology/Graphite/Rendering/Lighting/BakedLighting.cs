using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>How lightmap texels hold light.</summary>
public enum LightmapEncoding
{
    /// <summary>Light as is, for float formats.</summary>
    Linear,

    /// <summary>Colour times alpha times 8, for 8 bit formats.</summary>
    RGBM,
}

/// <summary>
/// Order 2 RGB spherical harmonics, the raw radiance projection in band 0, 1, 2 order. The light probe representation
/// for diffuse light on objects without a lightmap.
/// </summary>
public struct SphericalHarmonicsL2
{
    private const float K0 = 0.2820947918f;
    private const float K1 = 0.4886025119f;
    private const float K2a = 1.0925484306f;
    private const float K2b = 0.3153915652f;
    private const float K2c = 0.5462742153f;

    // Cosine lobe convolution over pi per band
    private const float A0 = 1.0f;
    private const float A1 = 2.0f / 3.0f;
    private const float A2 = 1.0f / 4.0f;

    public Float3 C0, C1, C2, C3, C4, C5, C6, C7, C8;

    /// <summary>Harmonics that light every normal with the same radiance.</summary>
    public static SphericalHarmonicsL2 FromConstant(Float3 color) => new() { C0 = color / (A0 * K0) };

    /// <summary>The seven vectors the shaders evaluate with one dot each per band, so a normal gives irradiance over pi.</summary>
    internal readonly ProbeShRecord Pack()
    {
        Float3 c0 = C0 * (A0 * K0);
        Float3 c1 = C1 * (A1 * K1), c2 = C2 * (A1 * K1), c3 = C3 * (A1 * K1);
        Float3 c4 = C4 * (A2 * K2a), c5 = C5 * (A2 * K2a), c7 = C7 * (A2 * K2a);
        Float3 c6 = C6 * (A2 * K2b);
        Float3 c8 = C8 * (A2 * K2c);
        return new ProbeShRecord
        {
            Ar = new Float4(c3.X, c1.X, c2.X, c0.X - c6.X),
            Ag = new Float4(c3.Y, c1.Y, c2.Y, c0.Y - c6.Y),
            Ab = new Float4(c3.Z, c1.Z, c2.Z, c0.Z - c6.Z),
            Br = new Float4(c4.X, c5.X, c6.X * 3f, c7.X),
            Bg = new Float4(c4.Y, c5.Y, c6.Y * 3f, c7.Y),
            Bb = new Float4(c4.Z, c5.Z, c6.Z * 3f, c7.Z),
            C = new Float4(c8.X, c8.Y, c8.Z, 0),
        };
    }
}

/// <summary>Baked light probes: positions, their harmonics and a tetrahedralization of them, from the host's baker.</summary>
public sealed class LightProbeData
{
    public Double3[] Positions = [];
    public SphericalHarmonicsL2[] Coefficients = [];

    /// <summary>Four probe indices per tetrahedron.</summary>
    public int[] Tetrahedra = [];

    /// <summary>Four per tetrahedron: the one across the face opposite each corner, or -1 on the hull.</summary>
    public int[] Neighbours = [];
}

/// <summary>Packed harmonics as the shaders read them, 112 bytes, matching Graphite_ProbeSH.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ProbeShRecord
{
    public Float4 Ar, Ag, Ab, Br, Bg, Bb, C;
}

/// <summary>A probe for the sampling pass, 144 bytes: its position split high and low, then its packed harmonics.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ProbeRecord
{
    public Float4 PositionHigh;
    public Float4 PositionLow;
    public ProbeShRecord Sh;
}

/// <summary>A tetrahedron for the sampling pass, 80 bytes. Its rows give the barycentrics of corners 1 to 3 from a point's offset to corner 0.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ProbeTetRecord
{
    public const uint Degenerate = uint.MaxValue;

    public uint Corner0, Corner1, Corner2, Corner3;
    public int Neighbour0, Neighbour1, Neighbour2, Neighbour3;
    public Float4 Row1, Row2, Row3;
}

/// <summary>A lightmap page: its texture and sampler slots.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct LightmapPageRecord
{
    public const uint NotReady = uint.MaxValue;

    public uint Texture;
    public uint Sampler;
}

public sealed partial class RenderWorld
{
    private static readonly SamplerDescription s_lightmapSampler = SamplerDescription.Linear with
    {
        AddressModeU = SamplerAddressMode.Clamp,
        AddressModeV = SamplerAddressMode.Clamp,
        AddressModeW = SamplerAddressMode.Clamp,
    };

    private Texture?[] _lightmaps = [];
    private LightmapEncoding _lightmapEncoding;
    private bool _lightmapsDirty;
    private LightProbeData? _probeData;
    private bool _probesDirty;

    /// <summary>
    /// The lightmap pages renderables index with <see cref="LightmapBinding.Page"/>. A null page, one still loading, lights
    /// its renderables with ambient light until it is set. Renderables whose page is past the end use the light probes. Host thread.
    /// </summary>
    public void SetLightmaps(ReadOnlySpan<Texture?> pages, LightmapEncoding encoding)
    {
        _lightmaps = pages.ToArray();
        _lightmapEncoding = encoding;
        _lightmapsDirty = true;
    }

    /// <summary>
    /// The baked light probes that light renderables without a lightmap, sampled at their bounds center, or null for
    /// none. The data is copied. Host thread.
    /// </summary>
    public void SetLightProbes(LightProbeData? data)
    {
        if (data != null)
            Validate(data);

        _probeData = data == null ? null : new LightProbeData
        {
            Positions = [.. data.Positions],
            Coefficients = [.. data.Coefficients],
            Tetrahedra = [.. data.Tetrahedra],
            Neighbours = [.. data.Neighbours],
        };
        _probesDirty = true;
    }

    private static void Validate(LightProbeData data)
    {
        if (data.Coefficients.Length != data.Positions.Length || data.Tetrahedra.Length % 4 != 0 || data.Neighbours.Length != data.Tetrahedra.Length)
            throw new ArgumentException("Light probe data needs a harmonic per position and four corners and four neighbours per tetrahedron.", nameof(data));
        int tets = data.Tetrahedra.Length / 4;
        foreach (int corner in data.Tetrahedra)
        {
            if ((uint)corner >= (uint)data.Positions.Length)
                throw new ArgumentException($"A tetrahedron corner {corner} is not a probe.", nameof(data));
        }
        foreach (int neighbour in data.Neighbours)
        {
            if (neighbour < -1 || neighbour >= tets)
                throw new ArgumentException($"A tetrahedron neighbour {neighbour} is not a tetrahedron or -1.", nameof(data));
        }
    }

    /// <summary>Resolves the lightmap pages' slots again after a bindless slot was freed. Host thread, at extraction.</summary>
    internal void RepackLightmaps() => _lightmapsDirty = true;

    private void ExtractBakedLighting()
    {
        if (_lightmapsDirty)
        {
            _lightmapsDirty = false;
            IMaterialResources resources = Lighting.Resources;
            LightmapPageRecord[] pages = new LightmapPageRecord[_lightmaps.Length];
            uint sampler = resources.SamplerSlot(s_lightmapSampler);
            for (int i = 0; i < pages.Length; i++)
            {
                Texture? page = _lightmaps[i];
                uint slot = page is { IsDisposed: false } ? resources.TextureSlot(page, MaterialPropertyType.Texture2D, "black") : LightmapPageRecord.NotReady;
                pages[i] = new LightmapPageRecord { Texture = slot, Sampler = sampler };
            }
            Probes.ReceiveLightmaps(pages);
            Lighting.SetLightmaps(pages.Length, _lightmapEncoding);
            _render.RefreshProbeSlots();
        }
        if (_probesDirty)
        {
            _probesDirty = false;
            Probes.ReceiveProbes(_probeData);
            Lighting.SetProbes(Probes.Active);
            _render.RefreshProbeSlots();
        }
    }

    internal WorldProbes Probes { get; private set; } = null!;
}

/// <summary>
/// The render side of a world's baked lighting: the lightmap page table, and light probe harmonics per renderable
/// without a lightmap. Those are sampled on the GPU in FrameStart for renderables that moved, or all of them when the
/// probes change: each finds the tetrahedron holding its bounds center, starting from the one it was in last, and
/// blends its corners, or blends the nearest probes outside the probe hull.
/// </summary>
internal sealed class WorldProbes : IDisposable
{
    public const uint NoSlot = uint.MaxValue;

    private readonly GraphicsDevice _device;
    private readonly Stack<uint> _freeSlots = new();
    private uint _nextSlot;
    private uint[] _slotObject = new uint[64];
    private bool[] _requested = new bool[64];
    private readonly List<int> _requests = new();
    private bool _requestAll;
    private Float4[] _requestScratch = new Float4[128];
    private LightProbeData? _pending;
    private bool _probesChanged;

    public WorldProbes(GraphicsDevice device)
    {
        _device = device;
        LightmapPages = GpuTable.Of<LightmapPageRecord>(device, "Lightmap pages", 16);
        ProbeRecords = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, "Light probes");
        TetRecords = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, "Light probe tetrahedra");
        Requests = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, "Light probe requests");
        ObjectSh = CreateSh(64);
    }

    public GpuTable LightmapPages { get; }
    public GpuArray ProbeRecords { get; }
    public GpuArray TetRecords { get; }

    /// <summary>This frame's renderables to sample, two vectors each: the bounds center's high part with the harmonics slot as uint bits, then its low part.</summary>
    public GpuArray Requests { get; }

    /// <summary>Harmonics per slot, written by the sampling pass. The last vector's w holds the tetrahedron the walk ended in.</summary>
    public DeviceBuffer ObjectSh { get; private set; }

    public uint ProbeCount { get; private set; }
    public uint TetCount { get; private set; }
    public uint RequestCount { get; private set; }

    /// <summary>Whether the world has probes. Renderables hold harmonics slots only while it does.</summary>
    public bool Active { get; private set; }

    public int PageCount { get; private set; }

    /// <summary>Whether a renderable on this lightmap page is lit by the probes: it has no page, or one past the end.</summary>
    public bool Wants(int page) => Active && (page < 0 || page >= PageCount);

    public void ReceiveLightmaps(ReadOnlySpan<LightmapPageRecord> pages)
    {
        PageCount = pages.Length;
        for (int i = 0; i < pages.Length; i++)
            LightmapPages.Set((uint)i, pages[i]);
    }

    public void ReceiveProbes(LightProbeData? data)
    {
        _pending = data;
        _probesChanged = true;
        Active = data != null && data.Positions.Length > 0;
    }

    /// <summary>Gives a renderable a harmonics slot. Extraction or render thread.</summary>
    public uint Assign(int objectIndex)
    {
        uint slot = _freeSlots.Count > 0 ? _freeSlots.Pop() : _nextSlot++;
        if (slot >= _slotObject.Length)
        {
            Array.Resize(ref _slotObject, Math.Max((int)slot + 1, _slotObject.Length * 2));
            Array.Resize(ref _requested, _slotObject.Length);
        }
        _slotObject[slot] = (uint)objectIndex;
        Request(slot);
        return slot;
    }

    public void Release(uint slot)
    {
        _slotObject[slot] = NoSlot;
        _freeSlots.Push(slot);
    }

    /// <summary>Samples the slot's harmonics again this frame.</summary>
    public void Request(uint slot)
    {
        if (_requested[slot])
            return;
        _requested[slot] = true;
        _requests.Add((int)slot);
    }

    /// <summary>
    /// Uploads changed probes and this frame's requests, growing the harmonics buffer to the slots in use and adding
    /// a replaced one to <paramref name="replaced"/>. Render thread, after the world's render side updated.
    /// </summary>
    public void Update(RenderWorld.RenderSide render, Uploader uploader, List<DeviceBuffer> replaced)
    {
        if (_probesChanged)
        {
            _probesChanged = false;
            UploadProbes(uploader);
            _requestAll = true;
        }

        if (ObjectSh.SizeInBytes < Math.Max(_nextSlot, 1) * (uint)Marshal.SizeOf<ProbeShRecord>())
        {
            replaced.Add(ObjectSh);
            uint capacity = ObjectSh.SizeInBytes / (uint)Marshal.SizeOf<ProbeShRecord>();
            while (capacity < _nextSlot)
                capacity *= 2;
            ObjectSh = CreateSh(capacity);
            _requestAll = true;
        }

        if (_requestAll)
        {
            _requestAll = false;
            for (uint slot = 0; slot < _nextSlot; slot++)
            {
                if (_slotObject[slot] != NoSlot)
                    Request(slot);
            }
        }

        RequestCount = 0;
        if (ProbeCount > 0)
        {
            if (_requestScratch.Length < _requests.Count * 2)
                _requestScratch = new Float4[_requests.Count * 4];
            foreach (int slot in _requests)
            {
                uint objectIndex = _slotObject[slot];
                if (objectIndex == NoSlot)
                    continue;
                (Float3 high, Float3 low) = Precise.Split(render.WorldCenter[objectIndex]);
                _requestScratch[RequestCount * 2] = new Float4(high.X, high.Y, high.Z, BitConverter.UInt32BitsToSingle((uint)slot));
                _requestScratch[RequestCount * 2 + 1] = new Float4(low.X, low.Y, low.Z, 0);
                RequestCount++;
            }
            Requests.Write<Float4>(uploader, _requestScratch.AsSpan(0, (int)RequestCount * 2));
        }

        foreach (int slot in _requests)
            _requested[slot] = false;
        _requests.Clear();
    }

    public bool Flush(Uploader uploader, List<DeviceBuffer> replaced)
    {
        if (LightmapPages.Flush(uploader) is not { } old)
            return false;
        replaced.Add(old);
        return true;
    }

    public void Dispose()
    {
        LightmapPages.Dispose();
        ProbeRecords.Dispose();
        TetRecords.Dispose();
        Requests.Dispose();
        ObjectSh.Dispose();
    }

    private void UploadProbes(Uploader uploader)
    {
        LightProbeData? data = _pending;
        _pending = null;
        if (data == null || data.Positions.Length == 0)
        {
            ProbeCount = 0;
            TetCount = 0;
            return;
        }

        ProbeRecord[] probes = new ProbeRecord[data.Positions.Length];
        for (int i = 0; i < probes.Length; i++)
        {
            (Float3 high, Float3 low) = Precise.Split(data.Positions[i]);
            probes[i] = new ProbeRecord { PositionHigh = new Float4(high.X, high.Y, high.Z, 0), PositionLow = new Float4(low.X, low.Y, low.Z, 0), Sh = data.Coefficients[i].Pack() };
        }

        int tetCount = data.Tetrahedra.Length / 4;
        ProbeTetRecord[] tets = new ProbeTetRecord[Math.Max(tetCount, 1)];
        for (int t = 0; t < tetCount; t++)
            tets[t] = Tet(data, t);

        ProbeRecords.Write<ProbeRecord>(uploader, probes);
        TetRecords.Write<ProbeTetRecord>(uploader, tets.AsSpan(0, tetCount));
        ProbeCount = (uint)probes.Length;
        TetCount = (uint)tetCount;
    }

    // Barycentrics of corners 1 to 3 by Cramer's rule over the edges from corner 0, solved in double once here. The shader
    // applies them to the point's offset from corner 0, taken high and low, so tetrahedra keep their precision anywhere.
    private ProbeTetRecord Tet(LightProbeData data, int t)
    {
        ReadOnlySpan<int> corners = data.Tetrahedra.AsSpan(t * 4, 4);
        ReadOnlySpan<int> neighbours = data.Neighbours.AsSpan(t * 4, 4);
        ProbeTetRecord record = new()
        {
            Corner0 = (uint)corners[0],
            Corner1 = (uint)corners[1],
            Corner2 = (uint)corners[2],
            Corner3 = (uint)corners[3],
            Neighbour0 = neighbours[0],
            Neighbour1 = neighbours[1],
            Neighbour2 = neighbours[2],
            Neighbour3 = neighbours[3],
        };

        Double3 a = data.Positions[corners[0]];
        Double3 v1 = data.Positions[corners[1]] - a;
        Double3 v2 = data.Positions[corners[2]] - a;
        Double3 v3 = data.Positions[corners[3]] - a;
        double det = Dot(v1, Cross(v2, v3));
        if (Math.Abs(det) < 1e-12)
        {
            record.Corner0 = ProbeTetRecord.Degenerate;
            return record;
        }

        record.Row1 = Row(Cross(v2, v3) / det);
        record.Row2 = Row(Cross(v3, v1) / det);
        record.Row3 = Row(Cross(v1, v2) / det);
        return record;
    }

    private static Float4 Row(Double3 r) => new((float)r.X, (float)r.Y, (float)r.Z, 0);

    private static double Dot(Double3 a, Double3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static Double3 Cross(Double3 a, Double3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    private DeviceBuffer CreateSh(uint capacity)
    {
        DeviceBuffer buffer = _device.ResourceFactory.CreateBuffer(new BufferDescription(capacity * (uint)Marshal.SizeOf<ProbeShRecord>(), BufferUsage.StructuredBufferReadWrite));
        buffer.Name = "Object light probes";
        return buffer;
    }
}

/// <summary>Samples the light probes for every world's requests, after the upload and before any camera reads them.</summary>
internal sealed class ProbePass(ComputeProgram program) : IPass, IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Push
    {
        public uint ProbeCount;
        public uint TetCount;
        public uint RequestCount;
        private uint _pad;
    }

    private readonly PropertySet _properties = new();

    public DeviceBuffer[] Buffers = [];
    public readonly List<WorldProbes> Worlds = new();

    public string Name => "Graphite.LightProbes";

    public void Setup(RenderContextBuilder builder)
    {
        for (int i = 0; i < Buffers.Length; i++)
            builder.DeclareImportedBuffer(FramePipeline.BufferId(i), Buffers[i], BufferAccess.ShaderRead | BufferAccess.ShaderWrite);
    }

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        bool bound = false;
        foreach (WorldProbes probes in Worlds)
        {
            if (probes.RequestCount == 0)
                continue;
            if (!bound)
            {
                cmd.SetComputeShader(program);
                bound = true;
            }
            _properties.SetBuffer("Graphite_Probes", probes.ProbeRecords.Buffer);
            _properties.SetBuffer("Graphite_ProbeTets", probes.TetRecords.Buffer);
            _properties.SetBuffer("Graphite_ProbeRequests", probes.Requests.Buffer);
            _properties.SetBuffer("Graphite_ProbeOutput", probes.ObjectSh);
            cmd.SetProperties(_properties);
            cmd.SetPushConstants(new Push { ProbeCount = probes.ProbeCount, TetCount = probes.TetCount, RequestCount = probes.RequestCount });
            cmd.Dispatch((probes.RequestCount + 63) / 64, 1, 1);
        }
    }

    public void Dispose() => program.Dispose();
}
