using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>When a world's sky is captured into its sky probe.</summary>
public enum SkyCaptureMode : byte
{
    /// <summary>When the sky material, the environment or the directional lights change, at most every other frame.</summary>
    OnChange,
    EveryFrame,

    /// <summary>Once, when the world first draws.</summary>
    Never,
}

public enum ReflectionProbeMode : byte
{
    /// <summary>Shows <see cref="ReflectionProbeDesc.Baked"/>, a capture the host keeps, from <see cref="RenderWorld.BakeReflectionProbe"/>.</summary>
    Baked,

    /// <summary>Captured while the world draws, as often as <see cref="ReflectionProbeDesc.Refresh"/> says.</summary>
    Realtime,
}

public enum ReflectionProbeRefresh : byte
{
    /// <summary>When added, when its mode changes and when its capture position, clip planes or mask change.</summary>
    Once,

    /// <summary>Again as soon as the last capture finishes.</summary>
    EveryFrame,

    /// <summary>When added, then only when <see cref="RenderWorld.RenderReflectionProbe"/> asks.</summary>
    OnRequest,
}

public enum ReflectionProbeTimeSlicing : byte
{
    /// <summary>All six faces and the filtering in one frame.</summary>
    AllFacesAtOnce,

    /// <summary>One face a frame, filtered on the seventh, so a capture costs a sixth as much each frame.</summary>
    IndividualFaces,
}

/// <summary>
/// A box the reflection of a captured cube covers. Reflections are projected onto the box, so a probe fitted to a room
/// shows its walls in the right place from anywhere inside it. Where probes overlap the one with the higher importance
/// wins, then the smaller one, fading over the blend distance. Surfaces no probe covers reflect the sky.
/// </summary>
public struct ReflectionProbeDesc
{
    /// <summary>The middle of the box, where captures are taken from.</summary>
    public Double3 Position;
    public Quaternion Rotation;

    /// <summary>Size of the box the probe affects and projects reflections onto.</summary>
    public Float3 BoxSize;

    /// <summary>How far in from the box faces reflections fade in, so neighbouring probes blend.</summary>
    public float BlendDistance;

    /// <summary>Projects reflections onto the box rather than treating them as infinitely far away.</summary>
    public bool BoxProjection;

    /// <summary>Wins over overlapping probes with a lower value, whatever their size.</summary>
    public int Importance;
    public float Intensity;

    public ReflectionProbeMode Mode;
    public ReflectionProbeRefresh Refresh;
    public ReflectionProbeTimeSlicing TimeSlicing;
    public float NearClip, FarClip;
    public uint CullingMask;

    /// <summary>A capture to show in <see cref="ReflectionProbeMode.Baked"/> mode, null for none yet.</summary>
    public BakedReflectionProbe? Baked;

    public ReflectionProbeDesc()
    {
        Rotation = Quaternion.Identity;
        BoxSize = new Float3(10, 10, 10);
        BlendDistance = 1;
        BoxProjection = true;
        Importance = 1;
        Intensity = 1;
        NearClip = 0.1f;
        FarClip = 1000;
        CullingMask = ~0u;
    }
}

/// <summary>A prefiltered capture as half float RGBA faces, every mip of every face: Faces[face * Mips + mip].</summary>
public sealed class BakedReflectionProbe
{
    public int Size;
    public int Mips;
    public byte[][] Faces = [];
}

public readonly record struct ReflectionProbeHandle(int Index, int Generation)
{
    public bool IsValid => Generation > 0;
}

/// <summary>A reflection probe as the shaders read it, 80 bytes, matching Graphite_ReflectionProbe. Its centre is in its region's space.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ReflectionProbeRecord
{
    public Float4 Row0, Row1, Row2;
    public Float4 Box;
    public Float4 Params;
}

/// <summary>What a frame captures of a probe: faces [FirstFace, EndFace), then the filtering, then a read back for a bake.</summary>
internal readonly record struct ProbeCapture(int Index, int FirstFace, int EndFace, bool Prefilter, Action<BakedReflectionProbe>? Bake);

public sealed partial class RenderWorld
{
    private readonly HandleStore<ReflectionProbeDesc> _reflectionProbes = new();
    private readonly List<int> _probeCaptureRequests = new();
    private readonly List<(int Index, Action<BakedReflectionProbe> Done)> _probeBakes = new();

    public ReflectionProbeHandle AddReflectionProbe(in ReflectionProbeDesc desc)
    {
        (int index, int generation) = _reflectionProbes.Add(desc);
        return new ReflectionProbeHandle(index, generation);
    }

    public void SetReflectionProbe(ReflectionProbeHandle handle, in ReflectionProbeDesc desc) => _reflectionProbes.Set(handle.Index, handle.Generation, desc);

    public void RemoveReflectionProbe(ReflectionProbeHandle handle) => _reflectionProbes.Remove(handle.Index, handle.Generation);

    public bool IsAlive(ReflectionProbeHandle handle) => _reflectionProbes.IsAlive(handle.Index, handle.Generation);

    /// <summary>Asks a realtime probe to capture again, before a later frame's views draw. Host thread.</summary>
    public void RenderReflectionProbe(ReflectionProbeHandle handle)
    {
        _reflectionProbes.Get(handle.Index, handle.Generation);
        _probeCaptureRequests.Add(handle.Index);
    }

    /// <summary>
    /// Captures a probe, prefilters it and reads it back for the host to keep, as <see cref="ReflectionProbeDesc.Baked"/>.
    /// <paramref name="done"/> runs on the host thread in a later <see cref="Renderer.SubmitFrame"/>. Host thread.
    /// </summary>
    public void BakeReflectionProbe(ReflectionProbeHandle handle, Action<BakedReflectionProbe> done)
    {
        ArgumentNullException.ThrowIfNull(done);
        _reflectionProbes.Get(handle.Index, handle.Generation);
        _probeBakes.Add((handle.Index, done));
    }

    private void ExtractReflections()
    {
        _reflectionProbes.Extract(Reflections.ReceiveProbe);
        foreach (int index in _probeCaptureRequests)
            Reflections.Request(index);
        _probeCaptureRequests.Clear();
        foreach ((int index, Action<BakedReflectionProbe> done) in _probeBakes)
            Reflections.RequestBake(index, done);
        _probeBakes.Clear();
    }
}

/// <summary>
/// The render side of a world's reflections: one cube array prefiltered by roughness across its mips, layer 0 holding the
/// sky and one layer per probe. Captures draw six layer views, or one face a frame when sliced, then filter with GGX into
/// every mip, reading the source mip whose texels match each sample's solid angle. Captured probes live in regions like
/// static lights, so each pixel finds the probes around it with a tree walk.
/// </summary>
internal sealed class WorldReflections : IDisposable
{
    /// <summary>Edge length of every probe's cube.</summary>
    public const uint Resolution = 128;

    /// <summary>The smallest prefiltered mip is 4 texels across, below that the roughest reflections turn blocky.</summary>
    public const uint SmallestMip = 4;

    public const uint MipCount = 6;
    public const int SkyLayer = 0;
    public const int PrefilterSamples = 64;
    private const int SkyFrameInterval = 2;

    /// <summary>A reversed Z perspective with an infinite far plane and a 90 degree field of view, looking down +Z.</summary>
    public static readonly Float4x4 FaceProjection = new(new Float4(1, 0, 0, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 0, 1), new Float4(0, 0, 0.05f, 0));

    // Each face's view: its rows are the directions the image's x, y and depth run along, the cube's own face layout
    private static readonly Float4x4[] s_faceViews =
    [
        Rows(new(0, 0, -1), new(0, -1, 0), new(1, 0, 0)),
        Rows(new(0, 0, 1), new(0, -1, 0), new(-1, 0, 0)),
        Rows(new(1, 0, 0), new(0, 0, 1), new(0, 1, 0)),
        Rows(new(1, 0, 0), new(0, 0, -1), new(0, -1, 0)),
        Rows(new(1, 0, 0), new(0, -1, 0), new(0, 0, 1)),
        Rows(new(-1, 0, 0), new(0, -1, 0), new(0, 0, -1)),
    ];

    private static readonly SamplerDescription s_sampler = SamplerDescription.Linear with
    {
        AddressModeU = SamplerAddressMode.Clamp,
        AddressModeV = SamplerAddressMode.Clamp,
        AddressModeW = SamplerAddressMode.Clamp,
    };

    private readonly GraphicsDevice _device;
    private readonly IMaterialResources _resources;
    private readonly Action<string, string> _report;
    private bool _skyCaptured;
    private int _capturedFrame;
    private Material? _capturedSky;
    private uint _capturedVersion;
    private int _capturedInputs;

    private ReflectionProbeDesc[] _probes = new ReflectionProbeDesc[8];
    private bool[] _alive = new bool[8];
    private int[] _layer = new int[8];
    private bool[] _captured = new bool[8];
    private bool[] _requested = new bool[8];
    private BakedReflectionProbe?[] _uploaded = new BakedReflectionProbe?[8];
    private readonly Stack<int> _freeLayers = new();
    private int _nextLayer = SkyLayer + 1;
    private uint _layers;
    private int _slicing = -1;
    private int _joining = -1;
    private int _slicedFaces;
    private int _nextRealtime;
    private readonly List<(int Index, Action<BakedReflectionProbe> Done)> _bakes = new();
    private readonly Dictionary<int, PrefilterView[]> _layerViews = new();
    private readonly List<PendingBake> _pendingBakes = new();

    private sealed class PendingBake
    {
        public required DeviceBuffer Buffer;
        public required Action<BakedReflectionProbe> Done;
        public ulong Execution;
    }

    public WorldReflections(GraphicsDevice device, IMaterialResources resources, Action<string, string> report)
    {
        _device = device;
        _resources = resources;
        _report = report;
        Regions = new StaticRegions<ReflectionProbeRecord>(device, "Reflection probes", Pack, report);
    }

    /// <summary>The captured probes, in regions with a tree each.</summary>
    public StaticRegions<ReflectionProbeRecord> Regions { get; }

    /// <summary>The prefiltered cube array, layer 0 the sky. Null until the first capture.</summary>
    public Texture? Probes { get; private set; }

    /// <summary>The sky rendered into a cube with a full mip chain, the sky prefilter's source.</summary>
    public Texture? SkyCube { get; private set; }

    public Framebuffer? CaptureTarget { get; private set; }

    /// <summary>A probe's faces rendered into a cube with a full mip chain, its prefilter's source.</summary>
    public Texture? ProbeCube { get; private set; }

    public Framebuffer? ProbeTarget { get; private set; }

    /// <summary>One framebuffer per face of the probe cube, for captures spread one face a frame.</summary>
    public Framebuffer[] ProbeFaceTargets { get; private set; } = [];

    public uint ProbeSlot { get; private set; }
    public uint SamplerSlot { get; private set; }

    /// <summary>Whether the sky is captured this frame. Decided before the frame's records are written.</summary>
    public bool CaptureDue { get; private set; }

    /// <summary>What this frame captures of a probe, if anything.</summary>
    public ProbeCapture? Capture { get; private set; }

    /// <summary>Baked probes copied into their layers this frame.</summary>
    public List<int> Uploads { get; } = new();

    public static Float4x4 FaceView(int face) => s_faceViews[face];

    /// <summary>A reversed Z perspective with a 90 degree field of view between two clip planes.</summary>
    public static Float4x4 FaceProjectionBetween(float near, float far)
    {
        near = MathF.Max(near, 0.001f);
        far = MathF.Max(far, near + 0.01f);
        return new Float4x4(new Float4(1, 0, 0, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, -near / (far - near), 1), new Float4(0, 0, near * far / (far - near), 0));
    }

    public ReflectionProbeDesc Probe(int index) => _probes[index];

    public int Layer(int index) => _layer[index];

    internal void ReceiveProbe(int index, int generation, uint sequence, bool alive, in ReflectionProbeDesc desc)
    {
        if (index >= _probes.Length)
        {
            int size = Math.Max(index + 1, _probes.Length * 2);
            Array.Resize(ref _probes, size);
            Array.Resize(ref _alive, size);
            Array.Resize(ref _layer, size);
            Array.Resize(ref _captured, size);
            Array.Resize(ref _requested, size);
            Array.Resize(ref _uploaded, size);
        }

        ReflectionProbeDesc old = _probes[index];
        bool wasAlive = _alive[index];
        if (!alive)
        {
            if (wasAlive)
            {
                if (_joining == index)
                    _joining = -1;
                _freeLayers.Push(_layer[index]);
                Uncapture(index);
                if (_slicing == index)
                    _slicing = -1;
            }
            _alive[index] = false;
            _probes[index] = default;
            return;
        }

        _probes[index] = desc;
        _alive[index] = true;
        if (!wasAlive)
        {
            _layer[index] = _freeLayers.Count > 0 ? _freeLayers.Pop() : _nextLayer++;
            Uncapture(index);
            return;
        }

        if (old.Mode != desc.Mode)
        {
            Uncapture(index);
            if (_slicing == index)
                _slicing = -1;
            return;
        }
        if (desc.Mode == ReflectionProbeMode.Realtime && desc.Refresh == ReflectionProbeRefresh.Once
            && (old.Position != desc.Position || old.Rotation != desc.Rotation || old.NearClip != desc.NearClip || old.FarClip != desc.FarClip || old.CullingMask != desc.CullingMask))
            _requested[index] = true;
        if (_captured[index])
            Regions.Set(index, desc.Position, Radius(desc));
    }

    internal void Request(int index)
    {
        if (index < _alive.Length && _alive[index])
            _requested[index] = true;
    }

    internal void RequestBake(int index, Action<BakedReflectionProbe> done) => _bakes.Add((index, done));

    /// <summary>The sky capture this frame could not draw the sky material yet, its shader still compiling, so it runs again.</summary>
    public void SkyIncomplete() => _skyCaptured = false;

    /// <summary>A frame failed after it was planned: everything it would have captured or uploaded is done again.</summary>
    public void FrameFailed()
    {
        _skyCaptured = false;
        _slicing = -1;
        _joining = -1;
        for (int i = 0; i < _probes.Length; i++)
        {
            if (_alive[i])
                Uncapture(i);
        }
    }

    /// <summary>
    /// Decides this frame's sky capture, baked uploads and probe capture, and marks what they make ready. Render thread,
    /// before the lighting records flush.
    /// </summary>
    public void Plan(int frame, WorldLighting lighting)
    {
        Capture = null;
        Uploads.Clear();
        bool regrown = EnsureArray();
        if (_joining >= 0 && !regrown && _alive[_joining])
            MarkCaptured(_joining);
        _joining = -1;
        PlanSky(frame, lighting, regrown);
        PlanBaked();
        PlanRealtime();
        Regions.Update();
        if (regrown)
            lighting.SetReflections(ProbeSlot, SamplerSlot, MipCount);
    }

    public bool Flush(Uploader uploader, List<DeviceBuffer> replaced)
    {
        int before = replaced.Count;
        if (Regions.Records.Flush(uploader) is { } records)
            replaced.Add(records);
        if (Regions.Nodes.Flush(uploader) is { } nodes)
            replaced.Add(nodes);
        return replaced.Count != before;
    }

    /// <summary>The views filtering into every mip of a layer, made the first time a layer is filtered.</summary>
    public PrefilterView[] LayerViews(int layer)
    {
        if (_layerViews.TryGetValue(layer, out PrefilterView[]? views))
            return views;

        views = new PrefilterView[MipCount];
        for (uint mip = 0; mip < MipCount; mip++)
        {
            FramebufferDescription description = new(null, [new FramebufferAttachment(Probes!, (uint)layer * 6, mip)]) { ViewCount = 6 };
            views[mip] = new PrefilterView(_device.ResourceFactory.CreateFramebuffer(description), (int)mip, Resolution >> (int)mip);
        }
        _layerViews[layer] = views;
        return views;
    }

    /// <summary>A buffer a bake reads its layer back into, collected once its frame completes. Render thread.</summary>
    public DeviceBuffer BakeBuffer(Action<BakedReflectionProbe> done)
    {
        uint bytes = 0;
        for (int mip = 0; mip < MipCount; mip++)
            bytes += 6 * FaceBytes(mip);
        DeviceBuffer buffer = _device.ResourceFactory.CreateBuffer(new BufferDescription(bytes, BufferUsage.Staging));
        buffer.Name = "Reflection probe bake";
        _pendingBakes.Add(new PendingBake { Buffer = buffer, Done = done });
        return buffer;
    }

    public static uint FaceBytes(int mip) => (Resolution >> mip) * (Resolution >> mip) * 8;

    /// <summary>Gives this frame's bakes the execution that reads them back. Render thread, after dispatch.</summary>
    public void Dispatched(ulong execution)
    {
        foreach (PendingBake bake in _pendingBakes)
        {
            if (bake.Execution == 0)
                bake.Execution = execution;
        }
    }

    /// <summary>Hands finished bakes to their callbacks. Host thread, render thread idle.</summary>
    public void CollectBakes(ulong completed, List<(Action<BakedReflectionProbe> Done, BakedReflectionProbe Baked)> finished)
    {
        for (int i = _pendingBakes.Count - 1; i >= 0; i--)
        {
            PendingBake bake = _pendingBakes[i];
            if (bake.Execution == 0 || bake.Execution > completed)
                continue;

            ReadOnlySpan<byte> data = _device.Map<byte>(bake.Buffer);
            BakedReflectionProbe baked = new() { Size = (int)Resolution, Mips = (int)MipCount, Faces = new byte[6 * MipCount][] };
            int offset = 0;
            for (int face = 0; face < 6; face++)
            {
                for (int mip = 0; mip < MipCount; mip++)
                {
                    int bytes = (int)FaceBytes(mip);
                    baked.Faces[face * MipCount + mip] = data.Slice(offset, bytes).ToArray();
                    offset += bytes;
                }
            }
            _device.Unmap(bake.Buffer);
            bake.Buffer.Dispose();
            _pendingBakes.RemoveAt(i);
            finished.Add((bake.Done, baked));
        }
    }

    public void Dispose()
    {
        DisposeArray();
        foreach (Framebuffer target in ProbeFaceTargets)
            target.Dispose();
        ProbeTarget?.Dispose();
        ProbeCube?.Dispose();
        CaptureTarget?.Dispose();
        SkyCube?.Dispose();
        foreach (PendingBake bake in _pendingBakes)
            bake.Buffer.Dispose();
        Regions.Records.Dispose();
        Regions.Nodes.Dispose();
    }

    private void PlanSky(int frame, WorldLighting lighting, bool regrown)
    {
        EnvironmentDesc environment = lighting.Environment;
        Material? sky = environment.Sky is { IsDisposed: false } live ? live : null;
        bool changed = regrown || !_skyCaptured || sky != _capturedSky || lighting.SkyVersion != _capturedVersion || lighting.SkyInputs != _capturedInputs;
        CaptureDue = regrown || environment.SkyCapture switch
        {
            SkyCaptureMode.EveryFrame => true,
            SkyCaptureMode.Never => !_skyCaptured,
            _ => changed && (!_skyCaptured || frame - _capturedFrame >= SkyFrameInterval),
        };
        if (!CaptureDue)
            return;

        _skyCaptured = true;
        _capturedFrame = frame;
        _capturedSky = sky;
        _capturedVersion = lighting.SkyVersion;
        _capturedInputs = lighting.SkyInputs;
    }

    private void PlanBaked()
    {
        for (int i = 0; i < _probes.Length; i++)
        {
            if (!_alive[i] || _probes[i].Mode != ReflectionProbeMode.Baked)
                continue;
            BakedReflectionProbe? baked = _probes[i].Baked;
            if (baked == _uploaded[i] && (_captured[i] || baked == null))
                continue;
            _uploaded[i] = baked;
            if (baked == null || !Valid(baked))
            {
                if (baked != null)
                    _report("reflection.baked", "A baked reflection probe's faces do not match its size and mips, it is not shown.");
                Uncapture(i);
                continue;
            }
            Uploads.Add(i);
            MarkCaptured(i);
        }
    }

    // One probe at a time: a bake first, else the next realtime probe that wants a capture, round robin
    private void PlanRealtime()
    {
        if (_bakes.Count > 0)
        {
            (int index, Action<BakedReflectionProbe> done) = _bakes[0];
            _bakes.RemoveAt(0);
            if (index < _alive.Length && _alive[index])
            {
                if (_slicing >= 0)
                {
                    _requested[_slicing] = true;
                    _slicing = -1;
                }
                Capture = new ProbeCapture(index, 0, 6, true, done);
                Joins(index);
                return;
            }
        }

        if (_slicing < 0)
        {
            for (int i = 0; i < _probes.Length; i++)
            {
                int index = (_nextRealtime + i) % _probes.Length;
                if (!WantsCapture(index))
                    continue;
                _nextRealtime = (index + 1) % _probes.Length;
                _requested[index] = false;
                _slicing = index;
                _slicedFaces = 0;
                break;
            }
        }
        if (_slicing < 0)
            return;

        int probe = _slicing;
        int faces = _probes[probe].TimeSlicing == ReflectionProbeTimeSlicing.IndividualFaces ? 1 : 6;
        if (_slicedFaces < 6)
        {
            int first = _slicedFaces;
            _slicedFaces = Math.Min(6, first + faces);
            if (faces == 1 || _slicedFaces < 6)
            {
                Capture = new ProbeCapture(probe, first, _slicedFaces, false, null);
                return;
            }
            Capture = new ProbeCapture(probe, first, _slicedFaces, true, null);
            Joins(probe);
        }
        else
        {
            Capture = new ProbeCapture(probe, 0, 0, true, null);
            MarkCaptured(probe);
        }
        _slicing = -1;
    }

    // A probe captured and filtered in one frame joins the tree the next, so its capture never sees its own unfilled layer
    private void Joins(int index)
    {
        if (_captured[index])
            return;
        _joining = index;
    }

    private bool WantsCapture(int index)
    {
        if (!_alive[index] || _probes[index].Mode != ReflectionProbeMode.Realtime)
            return false;
        return _requested[index] || !_captured[index] || _probes[index].Refresh == ReflectionProbeRefresh.EveryFrame;
    }

    private void MarkCaptured(int index)
    {
        _captured[index] = true;
        Regions.Set(index, _probes[index].Position, Radius(_probes[index]));
    }

    // Drops the probe from the tree until it is captured or uploaded again
    private void Uncapture(int index)
    {
        _captured[index] = false;
        _uploaded[index] = null;
        _requested[index] = _alive[index] && _probes[index].Mode == ReflectionProbeMode.Realtime;
        Regions.Remove(index);
    }

    private static bool Valid(BakedReflectionProbe baked)
    {
        if (baked.Size <= 0 || baked.Mips <= 0 || baked.Faces.Length < 6 * baked.Mips)
            return false;
        for (int face = 0; face < 6; face++)
        {
            for (int mip = 0; mip < baked.Mips; mip++)
            {
                long size = Math.Max(baked.Size >> mip, 1);
                if (baked.Faces[face * baked.Mips + mip]?.Length != size * size * 8)
                    return false;
            }
        }
        return true;
    }

    private static float Radius(in ReflectionProbeDesc desc)
    {
        Float3 half = Half(desc);
        return MathF.Sqrt(half.X * half.X + half.Y * half.Y + half.Z * half.Z);
    }

    private static Float3 Half(in ReflectionProbeDesc desc)
        => new(MathF.Max(desc.BoxSize.X * 0.5f, 0.01f), MathF.Max(desc.BoxSize.Y * 0.5f, 0.01f), MathF.Max(desc.BoxSize.Z * 0.5f, 0.01f));

    //   +0..+2  the box's axes, its centre in its region's space in w
    //   +3      box half size, blend distance
    //   +4      intensity, box projection, priority, array layer as uint bits
    private ReflectionProbeRecord Pack(int index, Double3 origin)
    {
        ReflectionProbeDesc desc = _probes[index];
        Float3 half = Half(desc);
        Quaternion rotation = desc.Rotation;
        Float3 x = rotation * new Float3(1, 0, 0), y = rotation * new Float3(0, 1, 0), z = rotation * new Float3(0, 0, 1);
        Double3 c = desc.Position - origin;
        float volume = 8 * half.X * half.Y * half.Z;
        return new ReflectionProbeRecord
        {
            Row0 = new Float4(x.X, x.Y, x.Z, (float)c.X),
            Row1 = new Float4(y.X, y.Y, y.Z, (float)c.Y),
            Row2 = new Float4(z.X, z.Y, z.Z, (float)c.Z),
            Box = new Float4(half.X, half.Y, half.Z, MathF.Max(desc.BlendDistance, 0)),
            Params = new Float4(MathF.Max(desc.Intensity, 0), desc.BoxProjection ? 1 : 0, desc.Importance + 1f / (1f + volume), BitConverter.UInt32BitsToSingle((uint)_layer[index])),
        };
    }

    // Grows by doubling, a new array holds nothing so everything is captured or uploaded into it again
    private bool EnsureArray()
    {
        uint needed = (uint)Math.Max(_nextLayer, 2);
        if (Probes != null && _layers >= needed)
            return false;

        uint layers = Probes != null ? _layers : 2;
        while (layers < needed)
            layers *= 2;
        DisposeArray();

        ResourceFactory factory = _device.ResourceFactory;
        _layers = layers;
        Probes = factory.CreateTexture(TextureDescription.CreateCube(Resolution, MipCount, layers, PixelFormat.R16_G16_B16_A16_Float, TextureUsage.Sampled | TextureUsage.RenderTarget));
        Probes.Name = "Reflection probes";
        ProbeSlot = _resources.TextureSlot(Probes, MaterialPropertyType.TextureCubeArray, "black");
        SamplerSlot = _resources.SamplerSlot(s_sampler);

        if (SkyCube == null)
        {
            uint fullChain = (uint)Math.Log2(Resolution) + 1;
            SkyCube = factory.CreateTexture(TextureDescription.CreateCube(Resolution, fullChain, 1, PixelFormat.R16_G16_B16_A16_Float, TextureUsage.Sampled | TextureUsage.RenderTarget));
            SkyCube.Name = "Sky capture";
            CaptureTarget = factory.CreateFramebuffer(new FramebufferDescription(null, SkyCube) { ViewCount = 6 });
            ProbeCube = factory.CreateTexture(TextureDescription.CreateCube(Resolution, fullChain, 1, PixelFormat.R16_G16_B16_A16_Float, TextureUsage.Sampled | TextureUsage.RenderTarget));
            ProbeCube.Name = "Reflection probe capture";
            ProbeTarget = factory.CreateFramebuffer(new FramebufferDescription(null, ProbeCube) { ViewCount = 6 });
            ProbeFaceTargets = new Framebuffer[6];
            for (uint face = 0; face < 6; face++)
                ProbeFaceTargets[face] = factory.CreateFramebuffer(new FramebufferDescription(null, [new FramebufferAttachment(ProbeCube, face)]));
        }

        _skyCaptured = false;
        _slicing = -1;
        for (int i = 0; i < _probes.Length; i++)
        {
            if (_alive[i])
                Uncapture(i);
        }
        return true;
    }

    private void DisposeArray()
    {
        foreach (PrefilterView[] views in _layerViews.Values)
        {
            foreach (PrefilterView view in views)
                view.Target.Dispose();
        }
        _layerViews.Clear();
        Probes?.Dispose();
        Probes = null;
    }

    private static Float4x4 Rows(Float3 x, Float3 y, Float3 z)
        => new(new Float4(x.X, y.X, z.X, 0), new Float4(x.Y, y.Y, z.Y, 0), new Float4(x.Z, y.Z, z.Z, 0), new Float4(0, 0, 0, 1));
}

/// <summary>One prefiltered mip of a probe layer as a render view: its six faces drawn at once.</summary>
internal sealed class PrefilterView(Framebuffer target, int mip, uint size) : IRenderView
{
    public Framebuffer Target { get; } = target;
    Framebuffer? IRenderView.Target => Target;
    public int Mip { get; } = mip;
    public uint PixelWidth { get; } = size;
    public uint PixelHeight { get; } = size;
    public int ViewId => -1 - Mip;
}

/// <summary>
/// Fills every mip of a probe layer from a cube, run once per mip view: GGX filtered from a capture whose mips the first
/// view builds, or copied from a baked capture's nearest mip. A bake then reads the layer back.
/// </summary>
internal sealed class PrefilterPass(GraphicsProgram program, Sampler sampler) : IPass
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Push
    {
        public float Roughness;
        public float SourceSize;
        public uint SampleCount;
        public float SourceLod;
    }

    private readonly PropertySet _properties = new();
    private TextureHandle _target;

    public Texture Source = null!;

    /// <summary>The baked capture's mip count to copy from, or 0 to filter a capture.</summary>
    public int CopyMips;

    /// <summary>Where a bake reads the filled layer back to, or null.</summary>
    public DeviceBuffer? Readback;
    public Texture? Destination;
    public int Layer;

    public string Name => "Graphite.Prefilter";

    public void Setup(RenderContextBuilder builder) => _target = builder.DeclareViewTarget();

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        PrefilterView view = (PrefilterView)context.View;
        int mips = (int)WorldReflections.MipCount;
        if (view.Mip == 0 && CopyMips == 0)
            cmd.GenerateMipmaps(Source);

        cmd.SetFramebuffer(context.GetRenderTexture(_target).Framebuffer, new TargetLoadStoreOps(new AttachmentOps(LoadAction.DontCare, StoreAction.Store), AttachmentOps.Loaded));
        cmd.SetFullViewport();
        cmd.SetShader(program);
        cmd.SetVertexSource(VertexSource.None);
        _properties.SetTexture("Graphite_Source", Source, sampler);
        cmd.SetProperties(_properties);
        float sourceLod = CopyMips > 0 ? MathF.Round(mips > 1 ? view.Mip * (CopyMips - 1) / (float)(mips - 1) : 0) : -1;
        cmd.SetPushConstants(new Push
        {
            Roughness = mips > 1 ? view.Mip / (float)(mips - 1) : 0,
            SourceSize = Source.Width,
            SampleCount = view.Mip == 0 ? 1u : WorldReflections.PrefilterSamples,
            SourceLod = sourceLod,
        });
        cmd.Draw(3);

        if (Readback != null && view.Mip == mips - 1)
        {
            uint offset = 0;
            for (int face = 0; face < 6; face++)
            {
                for (int mip = 0; mip < mips; mip++)
                {
                    uint size = WorldReflections.Resolution >> mip;
                    cmd.CopyTextureToBuffer(Destination!, Readback, offset, new TextureRegion(0, 0, 0, size, size, 1, (uint)mip, (uint)(Layer * 6 + face)));
                    offset += WorldReflections.FaceBytes(mip);
                }
            }
        }
    }
}
