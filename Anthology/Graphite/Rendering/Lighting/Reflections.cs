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
    /// <summary>Shows <see cref="ReflectionProbeDesc.Cubemap"/>, a prefiltered cube the host keeps, for example made from <see cref="RenderWorld.BakeReflectionProbe"/>.</summary>
    Baked,

    /// <summary>Captured while the world draws, as often as <see cref="ReflectionProbeDesc.Refresh"/> says.</summary>
    Realtime,
}

public enum ReflectionProbeRefresh : byte
{
    /// <summary>When added or its mode or resolution changes, then only when <see cref="RenderWorld.RenderReflectionProbe"/> asks.</summary>
    Once,

    /// <summary>Again as soon as the last capture finishes.</summary>
    EveryFrame,
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

    /// <summary>Edge length of a realtime probe's cube, a power of two. Baked probes keep their baked size.</summary>
    public uint Resolution;

    /// <summary>
    /// A prefiltered cube to show in <see cref="ReflectionProbeMode.Baked"/> mode, its mips from sharp to rough (mip m at
    /// roughness m / (mips - 1)), in any format, or null for none yet. The host keeps it alive while it is shown.
    /// </summary>
    public Texture? Cubemap;

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
        Resolution = 128;
    }
}

/// <summary>
/// A prefiltered capture as half float RGBA faces, every mip of every face: Faces[face * Mips + mip]. Hosts keep it and
/// turn it into a cube with <see cref="Renderer.CreateReflectionCubemap"/>.
/// </summary>
public sealed class BakedReflectionProbe
{
    public int Size;
    public int Mips;

    /// <summary>The texel format of every face, the renderer's reflection format when baked.</summary>
    public PixelFormat Format = PixelFormat.R16_G16_B16_A16_Float;
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
internal readonly record struct ProbeCapture(int Index, int FirstFace, int EndFace, bool Prefilter, DeviceBuffer? Readback, WorldReflections.Scratch Scratch);

public sealed partial class RenderWorld
{
    private readonly HandleStore<ReflectionProbeDesc> _reflectionProbes = new();
    private readonly List<int> _probeCaptureRequests = new();
    private readonly List<(int Index, int Generation, Action<BakedReflectionProbe> Done)> _probeBakes = new();

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
    /// Captures a probe, prefilters it and reads it back for the host to keep, then show through <see cref="ReflectionProbeDesc.Cubemap"/>.
    /// <paramref name="done"/> runs on the host thread in a later <see cref="Renderer.SubmitFrame"/>. Host thread.
    /// </summary>
    public void BakeReflectionProbe(ReflectionProbeHandle handle, Action<BakedReflectionProbe> done)
    {
        ArgumentNullException.ThrowIfNull(done);
        _reflectionProbes.Get(handle.Index, handle.Generation);
        _probeBakes.Add((handle.Index, handle.Generation, done));
    }

    private void ExtractReflections()
    {
        _reflectionProbes.Extract(Reflections.ReceiveProbe);
        foreach (int index in _probeCaptureRequests)
            Reflections.Request(index);
        _probeCaptureRequests.Clear();
        foreach ((int index, int generation, Action<BakedReflectionProbe> done) in _probeBakes)
            Reflections.RequestBake(index, generation, done);
        _probeBakes.Clear();
    }
}

/// <summary>How a renderer captures and filters reflections, from <see cref="RendererOptions"/>.</summary>
internal sealed record ReflectionSettings(PixelFormat Format, int PrefilterSamples, uint SmallestMip, long TexelsPerFrame)
{
    public static readonly ReflectionSettings Default = new(PixelFormat.R16_G16_B16_A16_Float, 64, 4, 256 * 1024);
}

/// <summary>
/// The render side of a world's reflections. The sky and every probe each have their own cube, prefiltered by roughness
/// across its mips and found through its bindless slot, so probes keep their own resolution and nothing is ever resized.
/// Captures draw six layer views, or one face a frame when sliced, then filter with GGX into every mip, reading the
/// source mip whose texels match each sample's solid angle. A frame captures up to a budget of faces, the probes nearest
/// the views that need it most first. Captured probes live in regions like static lights, so each pixel finds the probes
/// around it with a tree walk.
/// </summary>
internal sealed class WorldReflections : IDisposable
{
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

    /// <summary>A cube a capture draws into, with a full mip chain for the filter, and its targets.</summary>
    public sealed class Scratch(Texture cube, Framebuffer all, Framebuffer[] faces) : IDisposable
    {
        public Texture Cube { get; } = cube;
        public Framebuffer All { get; } = all;
        public Framebuffer[] Faces { get; } = faces;

        public void Dispose()
        {
            foreach (Framebuffer face in Faces)
                face.Dispose();
            All.Dispose();
            Cube.Dispose();
        }
    }

    /// <summary>A prefiltered cube: the sky's or a probe's, its slot and the views filling its mips.</summary>
    public sealed class Target(Texture cube, uint slot, PrefilterView[] views) : IDisposable
    {
        public Texture Cube { get; } = cube;
        public uint Slot { get; } = slot;
        public PrefilterView[] Views { get; } = views;
        public uint Mips => Cube.MipLevels;

        public void Dispose()
        {
            foreach (PrefilterView view in Views)
                view.Target.Dispose();
            Cube.Dispose();
        }
    }

    // A probe capture in progress: the faces it drew so far into its own scratch cube
    private sealed class Job
    {
        public required int Index;
        public required Scratch Scratch;
        public Action<BakedReflectionProbe>? Bake;
        public int Faces;
    }

    private readonly GraphicsDevice _device;
    private readonly IMaterialResources _resources;
    private readonly Action<RendererError> _report;
    private readonly ReflectionSettings _settings;
    private bool _skyCaptured;
    private int _capturedFrame;
    private Material? _capturedSky;
    private uint _capturedVersion;
    private int _capturedInputs;

    private ReflectionProbeDesc[] _probes = new ReflectionProbeDesc[8];
    private bool[] _alive = new bool[8];
    private Target?[] _targets = new Target?[8];
    private bool[] _captured = new bool[8];
    private bool[] _requested = new bool[8];
    private bool[] _working = new bool[8];
    private int[] _capturedAt = new int[8];
    private int[] _generations = new int[8];
    private Texture?[] _baked = new Texture?[8];
    private uint[] _bakedSlot = new uint[8];
    private readonly List<int> _bakedDirty = new();
    private readonly List<Job> _jobs = new();
    private readonly List<int> _joining = new();
    private readonly List<int> _candidates = new();
    private readonly List<(int Index, int Generation, Action<BakedReflectionProbe> Done)> _bakes = new();
    private readonly Dictionary<uint, Stack<Scratch>> _scratch = new();
    private readonly List<Scratch> _scratchUsed = new();
    private readonly List<Scratch> _scratchAll = new();
    private readonly List<PendingBake> _pendingBakes = new();
    private Double3[] _views = [];
    private int _viewCount;
    private int _frame;

    private sealed class PendingBake
    {
        public required int Index;
        public required int Generation;
        public required DeviceBuffer Buffer;
        public required Action<BakedReflectionProbe> Done;
        public required int Size;
        public required int Mips;
        public ulong Execution;
    }

    public WorldReflections(GraphicsDevice device, IMaterialResources resources, Action<RendererError> report, ReflectionSettings settings)
    {
        _device = device;
        _resources = resources;
        _report = report;
        _settings = settings;
        Regions = new SpatialIndex<ReflectionProbeRecord>(device, "Reflection probes", SpatialIndex.ProbesSet, Pack, report);
        SkyAmbient = device.ResourceFactory.CreateBuffer(new BufferDescription(112, BufferUsage.StructuredBufferReadWrite));
        SkyAmbient.Name = "Sky ambient";
    }

    /// <summary>The captured probes, in regions with a tree each.</summary>
    public SpatialIndex<ReflectionProbeRecord> Regions { get; }

    /// <summary>The prefiltered sky. Null until the first capture.</summary>
    public Target? Sky { get; private set; }

    /// <summary>The sky's light as packed order 2 harmonics, written on the GPU after each sky capture.</summary>
    public DeviceBuffer SkyAmbient { get; }

    public uint SamplerSlot { get; private set; }

    public int PrefilterSamples => _settings.PrefilterSamples;

    /// <summary>Whether the sky is captured this frame. Decided before the frame's records are written.</summary>
    public bool CaptureDue { get; private set; }

    /// <summary>The sky's capture cube this frame.</summary>
    public Scratch? SkyScratch { get; private set; }

    /// <summary>What this frame captures of probes.</summary>
    public List<ProbeCapture> Captures { get; } = new();

    public static Float4x4 FaceView(int face) => s_faceViews[face];

    /// <summary>Prefiltered mips of a cube this size, down to the smallest mip's size.</summary>
    public uint MipsFor(uint resolution) => (uint)Math.Max(Math.Log2(resolution) - Math.Log2(_settings.SmallestMip), 0) + 1;

    /// <summary>A reversed Z perspective with a 90 degree field of view between two clip planes.</summary>
    public static Float4x4 FaceProjectionBetween(float near, float far)
    {
        near = MathF.Max(near, 0.001f);
        far = MathF.Max(far, near + 0.01f);
        return new Float4x4(new Float4(1, 0, 0, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, -near / (far - near), 1), new Float4(0, 0, near * far / (far - near), 0));
    }

    public ReflectionProbeDesc Probe(int index) => _probes[index];

    /// <summary>Scratch cubes made so far, for tests.</summary>
    internal int ScratchCount => _scratchAll.Count;

    internal bool HasTarget(int index) => index < _targets.Length && _targets[index] != null;

    public Target ProbeTarget(int index) => _targets[index]!;

    internal void ReceiveProbe(int index, int generation, uint sequence, bool alive, in ReflectionProbeDesc desc)
    {
        if (index >= _probes.Length)
        {
            int size = Math.Max(index + 1, _probes.Length * 2);
            Array.Resize(ref _probes, size);
            Array.Resize(ref _alive, size);
            Array.Resize(ref _targets, size);
            Array.Resize(ref _captured, size);
            Array.Resize(ref _requested, size);
            Array.Resize(ref _working, size);
            Array.Resize(ref _capturedAt, size);
            Array.Resize(ref _generations, size);
            Array.Resize(ref _baked, size);
            Array.Resize(ref _bakedSlot, size);
        }

        ReflectionProbeDesc old = _probes[index];
        bool wasAlive = _alive[index] && _generations[index] == generation;
        _generations[index] = generation;
        if (!alive)
        {
            if (_alive[index])
            {
                StopJob(index);
                Uncapture(index);
                ReleaseTarget(index);
            }
            _alive[index] = false;
            _probes[index] = default;
            return;
        }

        _probes[index] = desc;
        _alive[index] = true;
        if (desc.Mode == ReflectionProbeMode.Baked)
            _bakedDirty.Add(index);
        if (!wasAlive || old.Mode != desc.Mode || (desc.Mode == ReflectionProbeMode.Realtime && old.Resolution != desc.Resolution))
        {
            StopJob(index);
            Uncapture(index);
            ReleaseTarget(index);
            return;
        }
        if (old.TimeSlicing != desc.TimeSlicing)
            StopJob(index);
        if (_captured[index])
            Regions.Set(index, desc.Position, Radius(desc), true);
    }

    internal void Request(int index)
    {
        if (index < _alive.Length && _alive[index])
            _requested[index] = true;
    }

    internal void RequestBake(int index, int generation, Action<BakedReflectionProbe> done) => _bakes.Add((index, generation, done));

    /// <summary>Resolves the slots of baked cubes that were disposed and freed. Host thread, at extraction.</summary>
    internal void RepackBaked(HashSet<Texture>? freed)
    {
        for (int i = 0; i < _probes.Length; i++)
        {
            if (_alive[i] && _probes[i].Mode == ReflectionProbeMode.Baked && _probes[i].Cubemap is { } cube && (freed == null || freed.Contains(cube)))
            {
                _baked[i] = null;
                _bakedDirty.Add(i);
            }
        }
    }

    /// <summary>The sky capture this frame could not draw the sky material yet, its shader still compiling, so it runs again.</summary>
    public void SkyIncomplete() => _skyCaptured = false;

    /// <summary>A frame failed after it was planned: everything it would have captured is done again.</summary>
    public void FrameFailed()
    {
        _skyCaptured = false;
        foreach (Job job in _jobs)
        {
            _working[job.Index] = false;
            _scratchUsed.Add(job.Scratch);
        }
        _jobs.Clear();
        for (int i = _pendingBakes.Count - 1; i >= 0; i--)
        {
            PendingBake bake = _pendingBakes[i];
            if (bake.Execution != 0)
                continue;
            bake.Buffer.Dispose();
            _pendingBakes.RemoveAt(i);
            _bakes.Add((bake.Index, bake.Generation, bake.Done));
        }
        _joining.Clear();
        for (int i = 0; i < _probes.Length; i++)
        {
            if (_alive[i] && _probes[i].Mode == ReflectionProbeMode.Realtime)
                Uncapture(i);
        }
    }

    /// <summary>
    /// Decides this frame's sky capture, baked probes and probe captures, and marks what they make ready. cameras are the
    /// positions this world is seen from this frame. Render thread, before the lighting records flush.
    /// </summary>
    public void Plan(int frame, WorldLighting lighting, ReadOnlySpan<Double3> cameras)
    {
        if (_views.Length < cameras.Length)
            _views = new Double3[cameras.Length];
        cameras.CopyTo(_views);
        _viewCount = cameras.Length;
        _frame = frame;

        Captures.Clear();
        foreach (Scratch scratch in _scratchUsed)
            _scratch[scratch.Cube.Width].Push(scratch);
        _scratchUsed.Clear();
        foreach (int index in _joining)
        {
            if (_alive[index])
                MarkCaptured(index, frame);
        }
        _joining.Clear();
        for (int i = 0; i < _probes.Length; i++)
        {
            if (_alive[i] && _probes[i].Mode == ReflectionProbeMode.Baked && _targets[i] != null && !_working[i])
                ReleaseTarget(i);
        }

        EnvironmentDesc environment = lighting.Environment;
        uint skyResolution = Pow2(environment.SkyProbeResolution, "sky probe");
        if (Sky == null || Sky.Cube.Width != skyResolution)
        {
            Sky?.Dispose();
            SamplerSlot = _resources.SamplerSlot(s_sampler);
            Sky = CreateTarget(skyResolution, MipsFor(skyResolution), "Sky probe");
            lighting.SetReflections(Sky.Slot, SamplerSlot, Sky.Mips);
            _skyCaptured = false;
        }
        PlanSky(frame, lighting);
        PlanBaked();
        PlanCaptures(frame);
        Regions.Update();
    }

    public bool Flush(Uploader uploader, List<DeviceBuffer> replaced)
    {
        int before = replaced.Count;
        if (Regions.StaticRecords.Flush(uploader) is { } records)
            replaced.Add(records);
        if (Regions.StaticNodes.Flush(uploader) is { } nodes)
            replaced.Add(nodes);
        return replaced.Count != before;
    }

    /// <summary>A buffer a bake reads a probe's cube back into, collected once its frame completes. Render thread.</summary>
    private DeviceBuffer BakeBuffer(int index, Target target, Action<BakedReflectionProbe> done)
    {
        uint bytes = 0;
        for (int mip = 0; mip < target.Mips; mip++)
            bytes += 6 * FaceBytes(target.Cube.Width, mip, target.Cube.Format);
        DeviceBuffer buffer = _device.ResourceFactory.CreateBuffer(new BufferDescription(bytes, BufferUsage.Staging));
        buffer.Name = "Reflection probe bake";
        _pendingBakes.Add(new PendingBake { Index = index, Generation = _generations[index], Buffer = buffer, Done = done, Size = (int)target.Cube.Width, Mips = (int)target.Mips });
        return buffer;
    }

    public static uint FaceBytes(uint resolution, int mip, PixelFormat format)
    {
        uint size = Math.Max(resolution >> mip, 1);
        return size * size * format.GetSizeInBytes();
    }

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
            BakedReflectionProbe baked = new() { Size = bake.Size, Mips = bake.Mips, Format = _settings.Format, Faces = new byte[6 * bake.Mips][] };
            int offset = 0;
            for (int face = 0; face < 6; face++)
            {
                for (int mip = 0; mip < bake.Mips; mip++)
                {
                    int bytes = (int)FaceBytes((uint)bake.Size, mip, _settings.Format);
                    baked.Faces[face * bake.Mips + mip] = data.Slice(offset, bytes).ToArray();
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
        Sky?.Dispose();
        foreach (Target? target in _targets)
            target?.Dispose();
        foreach (Scratch scratch in _scratchAll)
            scratch.Dispose();
        foreach (PendingBake bake in _pendingBakes)
            bake.Buffer.Dispose();
        SkyAmbient.Dispose();
        Regions.Dispose();
    }

    private void PlanSky(int frame, WorldLighting lighting)
    {
        EnvironmentDesc environment = lighting.Environment;
        Material? sky = environment.Sky is { IsDisposed: false } live ? live : null;
        bool changed = !_skyCaptured || sky != _capturedSky || lighting.SkyVersion != _capturedVersion || lighting.SkyInputs != _capturedInputs;
        CaptureDue = environment.SkyCapture switch
        {
            SkyCaptureMode.EveryFrame => true,
            SkyCaptureMode.Never => !_skyCaptured,
            _ => changed && (!_skyCaptured || frame - _capturedFrame >= Math.Max(environment.SkyCaptureInterval, 1)),
        };
        if (!CaptureDue)
        {
            SkyScratch = null;
            return;
        }

        SkyScratch = RentScratch(Sky!.Cube.Width);
        _scratchUsed.Add(SkyScratch);
        _skyCaptured = true;
        _capturedFrame = frame;
        _capturedSky = sky;
        _capturedVersion = lighting.SkyVersion;
        _capturedInputs = lighting.SkyInputs;
    }

    // A baked probe shows the host's prefiltered cube as it is, through its own slot
    private void PlanBaked()
    {
        foreach (int i in _bakedDirty)
        {
            if (!_alive[i] || _probes[i].Mode != ReflectionProbeMode.Baked)
                continue;
            Texture? cube = _probes[i].Cubemap;
            if (cube == _baked[i] && (_captured[i] || cube == null))
                continue;
            _baked[i] = cube;
            if (cube is not { IsDisposed: false } || cube.Type != TextureType.TextureCube)
            {
                if (cube is { IsDisposed: false })
                    _report(new RendererError("reflection.baked.notcube", "A baked reflection probe's cubemap is not a cube texture, it is not shown.", RendererErrorSeverity.Error, cube));
                Uncapture(i);
                continue;
            }
            _bakedSlot[i] = _resources.TextureSlot(cube, MaterialPropertyType.TextureCube, "black");
            MarkCaptured(i, 0);
        }
        _bakedDirty.Clear();
    }

    // Bakes first, then the probes already being captured, then the ones that need it most, until the texel budget is spent.
    // The first new capture of a frame runs whatever it costs while any budget is left, so no probe waits forever.
    private void PlanCaptures(int frame)
    {
        long budget = Math.Max(_settings.TexelsPerFrame, 1);
        while (_bakes.Count > 0)
        {
            (int index, int generation, Action<BakedReflectionProbe> done) = _bakes[0];
            _bakes.RemoveAt(0);
            if (index >= _alive.Length || !_alive[index] || _generations[index] != generation)
                continue;
            StopJob(index);
            Job job = StartJob(index);
            job.Bake = done;
            budget -= Advance(job, 6);
        }

        for (int j = 0; j < _jobs.Count && budget > 0;)
        {
            Job job = _jobs[j];
            int before = _jobs.Count;
            budget -= Advance(job, FacesAtOnce(job.Index));
            if (_jobs.Count == before)
                j++;
        }

        _candidates.Clear();
        for (int i = 0; i < _probes.Length; i++)
        {
            if (WantsCapture(i))
                _candidates.Add(i);
        }
        _candidates.Sort((a, b) => Priority(b, frame).CompareTo(Priority(a, frame)));
        bool started = false;
        foreach (int index in _candidates)
        {
            int faces = FacesAtOnce(index);
            uint resolution = Pow2(_probes[index].Resolution, "reflection probe");
            if (Cost(resolution, faces, faces == 6) > budget && (started || budget <= 0))
                continue;
            started = true;
            _requested[index] = false;
            budget -= Advance(StartJob(index), faces);
            if (budget <= 0)
                break;
        }
    }

    // Draws up to faces more faces of a job and returns what that costs. A job drawing all six at once filters in the same
    // frame, one spread over frames filters on the frame after its last face.
    private long Advance(Job job, int faces)
    {
        uint resolution = job.Scratch.Cube.Width;
        int first = job.Faces;
        if (first == 6)
        {
            Captures.Add(new ProbeCapture(job.Index, 6, 6, true, Readback(job), job.Scratch));
            Finish(job, joinNow: true);
            return Cost(resolution, 0, true);
        }

        int end = Math.Min(6, first + faces);
        job.Faces = end;
        bool filter = first == 0 && end == 6;
        Captures.Add(new ProbeCapture(job.Index, first, end, filter, filter ? Readback(job) : null, job.Scratch));
        if (filter)
            Finish(job, joinNow: false);
        return Cost(resolution, end - first, filter);
    }

    // Texels drawn: each face's, and when filtering every texel of every prefiltered mip
    private long Cost(uint resolution, int faces, bool filter)
    {
        long texels = (long)resolution * resolution * faces;
        if (filter)
        {
            uint mips = MipsFor(resolution);
            for (int mip = 0; mip < mips; mip++)
            {
                long size = Math.Max(resolution >> mip, 1);
                texels += 6 * size * size;
            }
        }
        return texels;
    }

    // A probe captured and filtered in one frame joins the tree the next, so its capture never sees its own unfilled cube
    private void Finish(Job job, bool joinNow)
    {
        _jobs.Remove(job);
        _working[job.Index] = false;
        _scratchUsed.Add(job.Scratch);
        if (_probes[job.Index].Mode != ReflectionProbeMode.Realtime)
            return;
        if (_captured[job.Index])
            _capturedAt[job.Index] = _frame;
        else if (joinNow)
            MarkCaptured(job.Index, _frame);
        else
            _joining.Add(job.Index);
    }

    private int FacesAtOnce(int index) => _probes[index].TimeSlicing == ReflectionProbeTimeSlicing.IndividualFaces ? 1 : 6;

    private Job StartJob(int index)
    {
        EnsureRealtimeTarget(index);
        Job job = new() { Index = index, Scratch = RentScratch(_targets[index]!.Cube.Width) };
        _jobs.Add(job);
        _working[index] = true;
        return job;
    }

    private void StopJob(int index)
    {
        _joining.Remove(index);
        if (!_working[index])
            return;
        _working[index] = false;
        for (int j = 0; j < _jobs.Count; j++)
        {
            if (_jobs[j].Index != index)
                continue;
            _scratchUsed.Add(_jobs[j].Scratch);
            _jobs.RemoveAt(j);
            break;
        }
    }

    // The buffer a filtered bake reads back into, planned now so a failed frame asks for the bake again
    private DeviceBuffer? Readback(Job job) => job.Bake != null ? BakeBuffer(job.Index, _targets[job.Index]!, job.Bake) : null;

    private bool WantsCapture(int index)
    {
        if (!_alive[index] || _working[index] || _probes[index].Mode != ReflectionProbeMode.Realtime || _joining.Contains(index))
            return false;
        return _requested[index] || !_captured[index] || _probes[index].Refresh == ReflectionProbeRefresh.EveryFrame;
    }

    // Probes asked for or never captured first, nearest first, then the ones refreshing every frame by how long ago and how close
    private double Priority(int index, int frame)
    {
        ReflectionProbeDesc probe = _probes[index];
        float radius = Math.Max(Radius(probe), 0.01f);
        double nearest = double.MaxValue;
        for (int v = 0; v < _viewCount; v++)
        {
            Double3 d = _views[v] - probe.Position;
            nearest = Math.Min(nearest, Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z));
        }
        double closeness = radius / (Math.Min(nearest, 1e30) + radius);
        if (_requested[index] || !_captured[index])
            return 1e9 + closeness;
        return (frame - _capturedAt[index] + 1) * closeness;
    }

    private Scratch RentScratch(uint resolution)
    {
        if (!_scratch.TryGetValue(resolution, out Stack<Scratch>? free))
        {
            free = new Stack<Scratch>();
            _scratch[resolution] = free;
        }
        if (free.Count > 0)
            return free.Pop();

        ResourceFactory factory = _device.ResourceFactory;
        uint fullChain = (uint)Math.Log2(resolution) + 1;
        Texture cube = factory.CreateTexture(TextureDescription.CreateCube(resolution, fullChain, 1, _settings.Format, TextureUsage.Sampled | TextureUsage.RenderTarget));
        cube.Name = $"Reflection capture {resolution}";
        Framebuffer all = factory.CreateFramebuffer(new FramebufferDescription(null, cube) { ViewCount = 6 });
        Framebuffer[] faces = new Framebuffer[6];
        for (uint face = 0; face < 6; face++)
            faces[face] = factory.CreateFramebuffer(new FramebufferDescription(null, [new FramebufferAttachment(cube, face)]));
        Scratch scratch = new(cube, all, faces);
        _scratchAll.Add(scratch);
        return scratch;
    }

    private void EnsureRealtimeTarget(int index)
    {
        uint resolution = Pow2(_probes[index].Resolution, "reflection probe");
        if (_targets[index] is { } target && target.Cube.Width == resolution)
            return;
        ReleaseTarget(index);
        _targets[index] = CreateTarget(resolution, MipsFor(resolution), "Reflection probe");
    }

    private Target CreateTarget(uint resolution, uint mips, string name)
    {
        Texture cube = _device.ResourceFactory.CreateTexture(TextureDescription.CreateCube(resolution, mips, 1, _settings.Format, TextureUsage.Sampled | TextureUsage.RenderTarget));
        cube.Name = name;
        PrefilterView[] views = new PrefilterView[mips];
        for (uint mip = 0; mip < mips; mip++)
        {
            FramebufferDescription description = new(null, [new FramebufferAttachment(cube, 0, mip)]) { ViewCount = 6 };
            views[mip] = new PrefilterView(_device.ResourceFactory.CreateFramebuffer(description), (int)mip, (int)mips, Math.Max(resolution >> (int)mip, 1));
        }
        return new Target(cube, _resources.TextureSlot(cube, MaterialPropertyType.TextureCube, "black"), views);
    }

    private void ReleaseTarget(int index)
    {
        _targets[index]?.Dispose();
        _targets[index] = null;
    }

    private void MarkCaptured(int index, int frame)
    {
        _captured[index] = true;
        _capturedAt[index] = frame;
        Regions.Set(index, _probes[index].Position, Radius(_probes[index]), true);
    }

    // Drops the probe from the tree until it is captured or its cube shown again
    private void Uncapture(int index)
    {
        _joining.Remove(index);
        _captured[index] = false;
        _baked[index] = null;
        _requested[index] = _alive[index] && _probes[index].Mode == ReflectionProbeMode.Realtime;
        Regions.Remove(index);
    }

    private uint Pow2(uint value, string what)
    {
        uint resolution = Math.Clamp(value, _settings.SmallestMip, 2048);
        if (!uint.IsPow2(resolution))
        {
            _report(new RendererError("reflection.resolution", $"A {what} resolution {value} is not a power of two, it is rounded up.", RendererErrorSeverity.Warning) { Key = $"reflection.resolution.{what}" });
            resolution = System.Numerics.BitOperations.RoundUpToPowerOf2(resolution);
        }
        return resolution;
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
    //   +4      intensity, box projection, priority, the cube's slot and mip count (slot | mips << 16) as uint bits
    private ReflectionProbeRecord Pack(int index, Double3 origin)
    {
        ReflectionProbeDesc desc = _probes[index];
        (uint slot, uint mips) = desc.Mode == ReflectionProbeMode.Baked ? (_bakedSlot[index], desc.Cubemap!.MipLevels) : (_targets[index]!.Slot, _targets[index]!.Mips);
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
            Params = new Float4(MathF.Max(desc.Intensity, 0), desc.BoxProjection ? 1 : 0, desc.Importance + 1f / (1f + volume), BitConverter.UInt32BitsToSingle(slot | (mips << 16))),
        };
    }

    private static Float4x4 Rows(Float3 x, Float3 y, Float3 z)
        => new(new Float4(x.X, y.X, z.X, 0), new Float4(x.Y, y.Y, z.Y, 0), new Float4(x.Z, y.Z, z.Z, 0), new Float4(0, 0, 0, 1));
}

/// <summary>Projects a sky capture onto packed order 2 harmonics for the sky ambient mode, after its filter built its mips.</summary>
internal sealed class SkyAmbientPass(ComputeProgram program, Sampler sampler) : IPass
{
    private static readonly RenderResourceID s_output = RenderResourceID.Intern("Graphite.SkyAmbient");

    [StructLayout(LayoutKind.Sequential)]
    private struct Push
    {
        public float Lod;
        public uint Size;
    }

    private readonly PropertySet _properties = new();

    public Texture Source = null!;
    public DeviceBuffer Output = null!;

    public string Name => "Graphite.SkyAmbient";

    public void Setup(RenderContextBuilder builder) => builder.DeclareImportedBuffer(s_output, Output, BufferAccess.ShaderWrite);

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        const uint size = 16;
        cmd.SetComputeShader(program);
        _properties.SetTexture("Graphite_Source", Source, sampler);
        _properties.SetBuffer("Graphite_SkyAmbient", Output);
        cmd.SetProperties(_properties);
        cmd.SetPushConstants(new Push { Lod = MathF.Max(MathF.Log2(Source.Width / (float)size), 0), Size = size });
        cmd.Dispatch(1, 1, 1);
    }
}

/// <summary>One prefiltered mip of a cube as a render view: its six faces drawn at once.</summary>
internal sealed class PrefilterView(Framebuffer target, int mip, int mips, uint size) : IRenderView
{
    public Framebuffer Target { get; } = target;
    Framebuffer? IRenderView.Target => Target;
    public int Mip { get; } = mip;
    public int Mips { get; } = mips;
    public uint PixelWidth { get; } = size;
    public uint PixelHeight { get; } = size;
    public int ViewId => -1 - Mip;
}

/// <summary>
/// Filters a captured cube with GGX into every roughness mip of a prefiltered cube, run once per mip view. The first view
/// builds the source's mips, which the filter reads by each sample's solid angle. A bake then reads the cube back.
/// </summary>
internal sealed class PrefilterPass(GraphicsProgram program, Sampler sampler, int samples) : IPass
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Push
    {
        public float Roughness;
        public float SourceSize;
        public uint SampleCount;
        private uint _pad;
    }

    private readonly PropertySet _properties = new();
    private TextureHandle _target;

    public Texture Source = null!;

    /// <summary>Where a bake reads the filled cube back to, or null.</summary>
    public DeviceBuffer? Readback;
    public Texture? Destination;

    public string Name => "Graphite.Prefilter";

    public void Setup(RenderContextBuilder builder) => _target = builder.DeclareViewTarget();

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        PrefilterView view = (PrefilterView)context.View;
        int mips = view.Mips;
        if (view.Mip == 0)
            cmd.GenerateMipmaps(Source);

        cmd.SetFramebuffer(context.GetRenderTexture(_target).Framebuffer, new TargetLoadStoreOps(new AttachmentOps(LoadAction.DontCare, StoreAction.Store), AttachmentOps.Loaded));
        cmd.SetFullViewport();
        cmd.SetShader(program);
        cmd.SetVertexSource(VertexSource.None);
        _properties.SetTexture("Graphite_Source", Source, sampler);
        cmd.SetProperties(_properties);
        cmd.SetPushConstants(new Push
        {
            Roughness = mips > 1 ? view.Mip / (float)(mips - 1) : 0,
            SourceSize = Source.Width,
            SampleCount = view.Mip == 0 ? 1u : (uint)samples,
        });
        cmd.Draw(3);

        if (Readback != null && view.Mip == mips - 1)
        {
            uint offset = 0;
            uint resolution = Destination!.Width;
            for (int face = 0; face < 6; face++)
            {
                for (int mip = 0; mip < mips; mip++)
                {
                    uint size = Math.Max(resolution >> mip, 1);
                    cmd.CopyTextureToBuffer(Destination, Readback, offset, new TextureRegion(0, 0, 0, size, size, 1, (uint)mip, (uint)face));
                    offset += WorldReflections.FaceBytes(resolution, mip, Destination.Format);
                }
            }
        }
    }
}
