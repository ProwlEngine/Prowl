using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Prowl.Vector;

using Vector4 = System.Numerics.Vector4;

namespace Prowl.Graphite.Rendering;

public enum LightType : byte
{
    Directional,
    Point,
    Spot,
}

/// <summary>How a light casts shadows. Carried on the light now, drawn by the shadow system later.</summary>
public struct ShadowDesc
{
    public bool Enabled;
    public uint Resolution;
    public float Bias;
    public float NormalBias;
    public byte Cascades;
    public float Distance;
    public float Strength;
    public float Quality;
}

public struct LightDesc
{
    public LightType Type;

    /// <summary>World position, double so lights far from the origin keep their place.</summary>
    public Double3 Position;

    /// <summary>The direction the light shines, world space.</summary>
    public Float3 Direction;

    /// <summary>Linear colour.</summary>
    public Float3 Color;
    public float Intensity;

    /// <summary>Distance at which point and spot lights reach zero.</summary>
    public float Range;

    /// <summary>Spot cone half angles from the axis, in degrees: full intensity inside the inner, none past the outer.</summary>
    public float InnerAngle, OuterAngle;

    /// <summary>Which renderables it lights, against their layer mask.</summary>
    public uint LayerMask;

    /// <summary>A hint: static lights join the world's static light tree, built only when one changes.</summary>
    public bool Static;
    public ShadowDesc Shadow;

    public LightDesc()
    {
        Type = LightType.Point;
        Direction = new Float3(0, 0, 1);
        Color = new Float3(1, 1, 1);
        Intensity = 1;
        Range = 10;
        InnerAngle = 30;
        OuterAngle = 45;
        LayerMask = ~0u;
    }
}

/// <summary>A light in a world. Generation checked, so a handle of a removed light stays invalid.</summary>
public readonly record struct LightHandle(int Index, int Generation)
{
    public bool IsValid => Generation > 0;
}

/// <summary>
/// A box projected decal. The unit box from -0.5 to 0.5 under <see cref="Transform"/> is its volume, projected along
/// its local -Z onto the surfaces inside it, which face its local +Z.
/// </summary>
public struct DecalDesc
{
    public Double4x4 Transform;

    /// <summary>Albedo with alpha as coverage, null for the tint alone.</summary>
    public Texture? Albedo;

    /// <summary>Tangent space normal map in the decal's X and Y, null to leave normals alone.</summary>
    public Texture? Normal;

    /// <summary>Occlusion in R, roughness in G and metallic in B, null to leave them alone.</summary>
    public Texture? Mask;
    public SamplerDescription? Sampler;

    /// <summary>Tint, alpha scales coverage.</summary>
    public Float4 Color;
    public float NormalStrength;

    /// <summary>Surfaces turned away from the decal fade out below this cosine to its axis, 0 for none.</summary>
    public float AngleFade;

    /// <summary>Higher draws over lower where decals overlap.</summary>
    public int Priority;

    /// <summary>Which renderables it lands on, against their layer mask.</summary>
    public uint LayerMask;

    /// <summary>A hint: static decals join the world's static decal tree.</summary>
    public bool Static;

    public DecalDesc()
    {
        Transform = Double4x4.Identity;
        Color = new Float4(1, 1, 1, 1);
        NormalStrength = 1;
        LayerMask = ~0u;
    }
}

public readonly record struct DecalHandle(int Index, int Generation)
{
    public bool IsValid => Generation > 0;
}

public enum AmbientMode : byte
{
    Uniform,
    Hemisphere,

    /// <summary>The sky's light, from the sky probe projected onto spherical harmonics after each capture.</summary>
    Sky,
}

public enum FogMode : byte
{
    Off,
    Linear,
    Exponential,
    ExponentialSquared,
}

public struct FogDesc : IEquatable<FogDesc>
{
    public FogMode Mode;
    public Float4 Color;
    public float Density, Start, End;

    /// <summary>Colours the fog with the sky behind it once a sky probe exists.</summary>
    public bool UseSky;

    public FogDesc()
    {
        Mode = FogMode.ExponentialSquared;
        Color = new Float4(0.5f, 0.5f, 0.5f, 1);
        Density = 0.01f;
        Start = 20;
        End = 100;
    }

    public readonly bool Equals(FogDesc other)
        => Mode == other.Mode && Color == other.Color && Density == other.Density && Start == other.Start && End == other.End && UseSky == other.UseSky;

    public override readonly bool Equals(object? obj) => obj is FogDesc other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(Mode, Color, Density, Start, End, UseSky);

    public static bool operator ==(FogDesc a, FogDesc b) => a.Equals(b);

    public static bool operator !=(FogDesc a, FogDesc b) => !a.Equals(b);
}

/// <summary>A world's ambient light, fog and sun. Changes to the world's own instance apply at the next frame.</summary>
public sealed class EnvironmentDesc
{
    public AmbientMode Ambient;
    public Float4 AmbientColor = new(0.43f, 0.55f, 0.65f, 1);
    public Float4 AmbientSky = new(0.3f, 0.3f, 0.4f, 1);
    public Float4 AmbientGround = new(0.2f, 0.2f, 0.2f, 1);
    public float AmbientIntensity = 1;
    public FogDesc Fog = new();

    /// <summary>The directional light that is the sun, else the most intense directional light is.</summary>
    public LightHandle Sun;

    /// <summary>The sky, a material with a Sky pass, drawn behind everything and captured into the sky probe. Null for none.</summary>
    public Material? Sky;

    /// <summary>What the sky probe sees where no sky material draws.</summary>
    public Float4 SkyColor = new(0.2f, 0.3f, 0.5f, 1);
    public SkyCaptureMode SkyCapture;

    /// <summary>Edge length of the sky probe's cube, a power of two.</summary>
    public uint SkyProbeResolution = 128;

    /// <summary>Frames at least between two sky captures in <see cref="SkyCaptureMode.OnChange"/>.</summary>
    public int SkyCaptureInterval = 2;

    internal EnvironmentDesc Copy() => (EnvironmentDesc)MemberwiseClone();

    internal bool Same(EnvironmentDesc other)
        => Ambient == other.Ambient && AmbientColor == other.AmbientColor && AmbientSky == other.AmbientSky && AmbientGround == other.AmbientGround
        && AmbientIntensity == other.AmbientIntensity && Fog == other.Fog && Sun == other.Sun
        && Sky == other.Sky && SkyColor == other.SkyColor && SkyCapture == other.SkyCapture
        && SkyProbeResolution == other.SkyProbeResolution && SkyCaptureInterval == other.SkyCaptureInterval;
}

/// <summary>A light record as the shaders read it, 80 bytes, matching Graphite_LightRecord. Positions are in its tree's space.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct LightRecord
{
    public Float4 PositionRange;
    public Float4 ColorIntensity;
    public Float4 DirectionFlags;
    public Float4 SpotShadow;
    public Float4 Shadow;
}

/// <summary>
/// A decal record as the shaders read it, 128 bytes, matching Graphite_DecalRecord. Its rows are the inverse of the linear
/// part with its centre in its tree's space in w, then the order it was added in.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DecalRecord
{
    public Float4 Row0, Row1, Row2;
    public uint Sequence;
    private uint _pad0, _pad1, _pad2;
    public Float4 Color;
    public uint Albedo, Normal, Mask, Sampler;
    public Float4 Params;
    public Float4 Axis;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DirectionalRecord
{
    public Float4 DirectionShadow;
    public Float4 Color;
}

[InlineArray(WorldLightingRecord.MaxDirectional)]
internal struct DirectionalRecords
{
    private DirectionalRecord _first;
}

/// <summary>A world's directional lights, ambient light and fog, matching Graphite_WorldLighting. The sun is the first directional light.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WorldLightingRecord
{
    public const int MaxDirectional = 8;

    public uint DirectionalCount;
    private uint _pad0;
    public uint HasProbes;
    private uint _pad1;
    public uint ProbeSlot;
    public uint ProbeSampler;
    public uint ProbeMips;
    public uint ProbesReady;
    public Float4 AmbientColor;
    public Float4 AmbientSky;
    public Float4 AmbientGround;
    public Float4 AmbientMode;
    public Float4 FogColor;
    public Float4 FogParams;
    public Float4 FogStates;
    public DirectionalRecords Directional;
}

public sealed partial class RenderWorld
{
    private readonly HandleStore<LightDesc> _lights = new();
    private readonly HandleStore<DecalDesc> _decals = new();
    private EnvironmentDesc _environment = new();
    private EnvironmentDesc? _extractedEnvironment;

    internal WorldLighting Lighting { get; private set; } = null!;

    /// <summary>The world's ambient light, fog and sun, compared at each frame so edits to it apply. Host thread.</summary>
    public EnvironmentDesc Environment
    {
        get => _environment;
        set => _environment = value ?? throw new ArgumentNullException(nameof(value));
    }

    public LightHandle AddLight(in LightDesc desc)
    {
        (int index, int generation) = _lights.Add(desc);
        return new LightHandle(index, generation);
    }

    public void SetLight(LightHandle handle, in LightDesc desc) => _lights.Set(handle.Index, handle.Generation, desc);

    public void RemoveLight(LightHandle handle) => _lights.Remove(handle.Index, handle.Generation);

    public bool IsAlive(LightHandle handle) => _lights.IsAlive(handle.Index, handle.Generation);

    public LightDesc GetLight(LightHandle handle) => _lights.Get(handle.Index, handle.Generation);

    public DecalHandle AddDecal(in DecalDesc desc)
    {
        (int index, int generation) = _decals.Add(desc);
        return new DecalHandle(index, generation);
    }

    public void SetDecal(DecalHandle handle, in DecalDesc desc) => _decals.Set(handle.Index, handle.Generation, desc);

    public void RemoveDecal(DecalHandle handle) => _decals.Remove(handle.Index, handle.Generation);

    public bool IsAlive(DecalHandle handle) => _decals.IsAlive(handle.Index, handle.Generation);

    private void ExtractLighting()
    {
        _lights.Extract(Lighting.ReceiveLight);
        _decals.Extract(Lighting.ReceiveDecal);
        ExtractBakedLighting();
        ExtractReflections();
        if (_extractedEnvironment == null || !_environment.Same(_extractedEnvironment))
        {
            _extractedEnvironment = _environment.Copy();
            Lighting.ReceiveEnvironment(_extractedEnvironment);
        }
        Lighting.ReceiveSkyVersion(_extractedEnvironment.Sky?.Version ?? 0);
    }
}

/// <summary>Host storage for lights and decals: descriptions keyed by a dense index with a free list, generation checked.</summary>
internal sealed class HandleStore<T> where T : struct
{
    private T[] _items = new T[16];
    private int[] _generation = new int[16];
    private bool[] _alive = new bool[16];
    private bool[] _dirty = new bool[16];
    private uint[] _sequence = new uint[16];
    private uint _nextSequence;
    private readonly List<int> _dirtyList = new();
    private readonly Stack<int> _free = new();
    private int _count;

    public (int Index, int Generation) Add(in T item)
    {
        int index = _free.Count > 0 ? _free.Pop() : _count++;
        if (index >= _items.Length)
        {
            int size = _items.Length * 2;
            Array.Resize(ref _items, size);
            Array.Resize(ref _generation, size);
            Array.Resize(ref _alive, size);
            Array.Resize(ref _dirty, size);
            Array.Resize(ref _sequence, size);
        }
        _generation[index]++;
        _alive[index] = true;
        _items[index] = item;
        _sequence[index] = _nextSequence++;
        MarkDirty(index);
        return (index, _generation[index]);
    }

    public void Set(int index, int generation, in T item)
    {
        Check(index, generation);
        _items[index] = item;
        MarkDirty(index);
    }

    public void Remove(int index, int generation)
    {
        Check(index, generation);
        _alive[index] = false;
        _generation[index]++;
        _items[index] = default;
        _free.Push(index);
        MarkDirty(index);
    }

    public bool IsAlive(int index, int generation) => (uint)index < (uint)_count && _alive[index] && _generation[index] == generation;

    public T Get(int index, int generation)
    {
        Check(index, generation);
        return _items[index];
    }

    /// <summary>Gets an item's index, generation, the order it was added in, whether it is alive, and its description.</summary>
    public delegate void Receiver(int index, int generation, uint sequence, bool alive, in T item);

    public void Extract(Receiver receive)
    {
        foreach (int index in _dirtyList)
        {
            _dirty[index] = false;
            receive(index, _generation[index], _sequence[index], _alive[index], _items[index]);
        }
        _dirtyList.Clear();
    }

    private void MarkDirty(int index)
    {
        if (_dirty[index])
            return;
        _dirty[index] = true;
        _dirtyList.Add(index);
    }

    private void Check(int index, int generation)
    {
        if (!IsAlive(index, generation))
            throw new ArgumentException("The handle was removed, or is not from this world.");
    }
}

/// <summary>
/// The render side of a world's lights and decals, shaded with H-Forward: point and spot lights and decals live in
/// four wide trees over their bounds, and a fragment walks them as a point query, no per frame culling. Static lights
/// and decals live in regions of space with a tree each, rebuilt only when one of their members changes. Moving ones
/// are culled to each view and built into a fresh tree per view every frame. Directional lights, ambient light and fog
/// live in one world record.
/// </summary>
internal sealed class WorldLighting : IDisposable
{
    private readonly IMaterialResources _resources;
    private readonly Action<string, string> _report;
    private readonly StaticRegions<LightRecord> _staticLights;
    private readonly StaticRegions<DecalRecord> _staticDecals;

    private LightDesc[] _lights = new LightDesc[16];
    private int[] _lightGeneration = new int[16];
    private bool[] _lightAlive = new bool[16];
    private readonly List<int> _dynamicLights = new();
    private readonly List<int> _directionalLights = new();
    private bool _lightMembership = true;

    private DecalDesc[] _decals = new DecalDesc[16];
    private bool[] _decalAlive = new bool[16];
    private DecalLook[] _decalLooks = new DecalLook[16];
    private uint[] _decalSequence = new uint[16];
    private readonly List<int> _dynamicDecals = new();
    private bool _decalMembership = true;

    private EnvironmentDesc _environment = new();
    private bool _worldRecordDirty = true;
    private bool _hasProbes;
    private uint _probeSlot, _probeSampler, _probeMips;
    private Vector4[] _spheres = new Vector4[64];
    private readonly List<int> _culled = new();
    private readonly List<int> _directionalOrder = new();
    private LightRecord[] _lightScratch = new LightRecord[64];
    private DecalRecord[] _decalScratch = new DecalRecord[64];

    /// <summary>What a decal looks like, independent of where it is: texture slots, tint and parameters.</summary>
    private struct DecalLook
    {
        public Double4x4 Inverse;
        public Double3 Center;
        public float Radius;
        public Float4 Color;
        public uint Albedo, Normal, Mask, Sampler;
        public Float4 Params;
        public Float4 Axis;
        public uint Sequence;
    }

    public WorldLighting(GraphicsDevice device, IMaterialResources resources, Action<string, string> report)
    {
        _resources = resources;
        _report = report;
        _staticLights = new StaticRegions<LightRecord>(device, "Static lights", (index, origin) => Pack(_lights[index], origin), report);
        _staticDecals = new StaticRegions<DecalRecord>(device, "Static decals", (index, origin) => Pack(_decalLooks[index], origin), report);
        World = GpuTable.Of<WorldLightingRecord>(device, "World lighting", 1);
        Tables = [_staticLights.Records, _staticLights.Nodes, _staticDecals.Records, _staticDecals.Nodes, World];
    }

    public GpuTable StaticLights => _staticLights.Records;
    public GpuTable StaticLightNodes => _staticLights.Nodes;
    public GpuTable StaticDecals => _staticDecals.Records;
    public GpuTable StaticDecalNodes => _staticDecals.Nodes;
    public GpuTable World { get; }
    public GpuTable[] Tables { get; }

    public IMaterialResources Resources => _resources;

    internal void ReceiveLight(int index, int generation, uint sequence, bool alive, in LightDesc given)
    {
        if (index >= _lights.Length)
        {
            int size = Math.Max(index + 1, _lights.Length * 2);
            Array.Resize(ref _lights, size);
            Array.Resize(ref _lightGeneration, size);
            Array.Resize(ref _lightAlive, size);
        }

        LightDesc desc = Normalized(given);
        LightDesc old = _lights[index];
        bool wasAlive = _lightAlive[index];
        _lights[index] = desc;
        _lightGeneration[index] = generation;
        _lightAlive[index] = alive;

        if (wasAlive != alive || old.Type != desc.Type || old.Static != desc.Static)
            _lightMembership = true;
        if (alive && desc.Static && desc.Type != LightType.Directional)
            _staticLights.Set(index, desc.Position, Math.Max(desc.Range, 1e-4f));
        else
            _staticLights.Remove(index);
        if ((wasAlive && old.Type == LightType.Directional) || (alive && desc.Type == LightType.Directional))
            _worldRecordDirty = true;
    }

    internal void ReceiveDecal(int index, int generation, uint sequence, bool alive, in DecalDesc desc)
    {
        if (index >= _decals.Length)
        {
            int size = Math.Max(index + 1, _decals.Length * 2);
            Array.Resize(ref _decals, size);
            Array.Resize(ref _decalAlive, size);
            Array.Resize(ref _decalLooks, size);
            Array.Resize(ref _decalSequence, size);
        }

        DecalDesc old = _decals[index];
        bool wasAlive = _decalAlive[index];
        _decals[index] = desc;
        _decalAlive[index] = alive;
        _decalSequence[index] = sequence;
        if (alive)
            _decalLooks[index] = Look(desc, _decalSequence[index]);

        if (wasAlive != alive || old.Static != desc.Static)
            _decalMembership = true;
        if (alive && desc.Static)
            _staticDecals.Set(index, _decalLooks[index].Center, _decalLooks[index].Radius);
        else
            _staticDecals.Remove(index);
    }

    internal void ReceiveEnvironment(EnvironmentDesc environment)
    {
        _environment = environment;
        _worldRecordDirty = true;
    }

    internal void ReceiveSkyVersion(uint version) => SkyVersion = version;

    /// <summary>The environment as of the last extraction.</summary>
    public EnvironmentDesc Environment => _environment;

    /// <summary>A hash of what the sky may be drawn from besides its material: the sun's direction and the sky colour.</summary>
    public int SkyInputs { get; private set; }

    /// <summary>The sky material's version as of the last extraction.</summary>
    public uint SkyVersion { get; private set; }

    internal void SetReflections(uint slot, uint sampler, uint mips)
    {
        _probeSlot = slot;
        _probeSampler = sampler;
        _probeMips = mips;
        _worldRecordDirty = true;
    }

    internal void SetProbes(bool hasProbes)
    {
        _hasProbes = hasProbes;
        _worldRecordDirty = true;
    }

    /// <summary>Repacks every decal after a bindless slot one may hold was freed. Host thread, at extraction.</summary>
    public void RepackDecals()
    {
        for (int i = 0; i < _decals.Length; i++)
        {
            if (!_decalAlive[i])
                continue;
            _decalLooks[i] = Look(_decals[i], _decalSequence[i]);
            if (_decals[i].Static)
                _staticDecals.Repack(i);
        }
    }

    /// <summary>Rebuilds the static regions that changed and rewrites the records and the world record. Render thread.</summary>
    public void Update()
    {
        if (_lightMembership)
            RebuildLightLists();
        if (_decalMembership)
            RebuildDecalLists();
        _staticLights.Update();
        _staticDecals.Update();
        if (_worldRecordDirty)
            WriteWorldRecord();
    }

    /// <summary>Builds a view's tree over the static light regions it sees. Returns the root. Render thread.</summary>
    public uint BuildViewLightRegions(in CullFrustum frustum, LightTreeBuilder builder, Uploader uploader, GpuArray entries, GpuArray nodes)
        => _staticLights.BuildView(frustum, builder, uploader, entries, nodes);

    /// <summary>Builds a view's tree over the static decal regions it sees. Returns the root. Render thread.</summary>
    public uint BuildViewDecalRegions(in CullFrustum frustum, LightTreeBuilder builder, Uploader uploader, GpuArray entries, GpuArray nodes)
        => _staticDecals.BuildView(frustum, builder, uploader, entries, nodes);

    /// <summary>
    /// Builds a view's moving light tree from the lights whose sphere meets its frustum, around the camera, and queues
    /// its records and nodes. Returns the root, or Invalid when no moving light is in view. Render thread.
    /// </summary>
    public uint BuildViewLights(in CullFrustum frustum, LightTreeBuilder builder, Uploader uploader, GpuArray records, GpuArray nodes)
    {
        _culled.Clear();
        foreach (int index in _dynamicLights)
        {
            LightDesc light = _lights[index];
            if (frustum.Sphere(light.Position, Math.Max(light.Range, 1e-4f)))
                _culled.Add(index);
        }
        if (_culled.Count == 0)
            return LightTreeBuilder.Invalid;

        Span<Vector4> spheres = Spheres(_culled.Count);
        for (int i = 0; i < _culled.Count; i++)
        {
            LightDesc light = _lights[_culled[i]];
            Float3 p = frustum.Relative(light.Position);
            spheres[i] = new Vector4(p.X, p.Y, p.Z, Math.Max(light.Range, 1e-4f));
        }

        builder.Build(spheres, Math.Clamp(_culled.Count / 1024, 4, 128), morton: true);
        CheckDepth(builder, LightTreeBuilder.ShaderStack);
        if (_lightScratch.Length < _culled.Count)
            _lightScratch = new LightRecord[_culled.Count * 2];
        ReadOnlySpan<int> order = builder.Order;
        for (int i = 0; i < _culled.Count; i++)
            _lightScratch[i] = Pack(_lights[_culled[order[i]]], frustum.Camera);
        records.Write<LightRecord>(uploader, _lightScratch.AsSpan(0, _culled.Count));
        nodes.Write(uploader, builder.Nodes);
        return builder.NodeCount > 0 ? 0u : LightTreeBuilder.Invalid;
    }

    /// <summary>Builds a view's moving decal tree, like <see cref="BuildViewLights"/>.</summary>
    public uint BuildViewDecals(in CullFrustum frustum, LightTreeBuilder builder, Uploader uploader, GpuArray records, GpuArray nodes)
    {
        _culled.Clear();
        foreach (int index in _dynamicDecals)
        {
            if (frustum.Sphere(_decalLooks[index].Center, _decalLooks[index].Radius))
                _culled.Add(index);
        }
        if (_culled.Count == 0)
            return LightTreeBuilder.Invalid;

        Span<Vector4> spheres = Spheres(_culled.Count);
        for (int i = 0; i < _culled.Count; i++)
        {
            DecalLook look = _decalLooks[_culled[i]];
            Float3 p = frustum.Relative(look.Center);
            spheres[i] = new Vector4(p.X, p.Y, p.Z, look.Radius);
        }

        builder.Build(spheres, Math.Clamp(_culled.Count / 1024, 4, 128), morton: true);
        CheckDepth(builder, LightTreeBuilder.ShaderStack);
        if (_decalScratch.Length < _culled.Count)
            _decalScratch = new DecalRecord[_culled.Count * 2];
        ReadOnlySpan<int> order = builder.Order;
        for (int i = 0; i < _culled.Count; i++)
            _decalScratch[i] = Pack(_decalLooks[_culled[order[i]]], frustum.Camera);
        records.Write<DecalRecord>(uploader, _decalScratch.AsSpan(0, _culled.Count));
        nodes.Write(uploader, builder.Nodes);
        return builder.NodeCount > 0 ? 0u : LightTreeBuilder.Invalid;
    }

    public bool Flush(Uploader uploader, List<DeviceBuffer> replaced)
    {
        int before = replaced.Count;
        foreach (GpuTable table in Tables)
        {
            if (table.Flush(uploader) is { } old)
                replaced.Add(old);
        }
        return replaced.Count != before;
    }

    public void Dispose()
    {
        foreach (GpuTable table in Tables)
            table.Dispose();
    }

    private void RebuildLightLists()
    {
        _lightMembership = false;
        _dynamicLights.Clear();
        _directionalLights.Clear();
        for (int i = 0; i < _lights.Length; i++)
        {
            if (!_lightAlive[i])
                continue;
            if (_lights[i].Type == LightType.Directional)
                _directionalLights.Add(i);
            else if (!_lights[i].Static)
                _dynamicLights.Add(i);
        }
    }

    private void RebuildDecalLists()
    {
        _decalMembership = false;
        _dynamicDecals.Clear();
        for (int i = 0; i < _decals.Length; i++)
        {
            if (_decalAlive[i] && !_decals[i].Static)
                _dynamicDecals.Add(i);
        }
    }

    // Each level above the last defers at most three siblings
    private void CheckDepth(LightTreeBuilder tree, int stack)
    {
        if ((tree.Depth - 1) * 3 > stack)
            _report("lighting.depth", $"A light or decal tree is {tree.Depth} levels deep, more than the shader's traversal stack holds, so some may be skipped.");
    }

    private Span<Vector4> Spheres(int count)
    {
        if (_spheres.Length < count)
            _spheres = new Vector4[Math.Max(count, _spheres.Length * 2)];
        return _spheres.AsSpan(0, count);
    }

    //   +0 position in tree space, range
    //   +1 colour, intensity
    //   +2 direction it shines, type in the low 2 bits and shadow enabled in bit 2 as int bits
    //   +3 cos of the outer and inner spot angles, shadow depth and normal bias
    //   +4 shadow strength, shadow quality, shadow slot as int bits, layer mask as uint bits
    // Directions unit length, a zero one pointing down +Z, and the inner cone never past the outer
    private static LightDesc Normalized(in LightDesc light)
    {
        LightDesc result = light;
        Float3 d = light.Direction;
        float length = MathF.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
        result.Direction = length > 1e-12f ? d / length : new Float3(0, 0, 1);
        result.InnerAngle = MathF.Min(light.InnerAngle, light.OuterAngle);
        return result;
    }

    private static LightRecord Pack(in LightDesc light, Double3 origin)
    {
        Double3 p = light.Position - origin;
        int type = light.Type == LightType.Spot ? 2 : 1;
        int flags = type | ((light.Shadow.Enabled ? 1 : 0) << 2);
        bool spot = light.Type == LightType.Spot;
        float outer = MathF.Cos(light.OuterAngle * MathF.PI / 180f);
        return new LightRecord
        {
            PositionRange = new Float4((float)p.X, (float)p.Y, (float)p.Z, light.Range),
            ColorIntensity = new Float4(light.Color.X, light.Color.Y, light.Color.Z, light.Intensity),
            DirectionFlags = new Float4(light.Direction.X, light.Direction.Y, light.Direction.Z, BitConverter.Int32BitsToSingle(flags)),
            SpotShadow = new Float4(spot ? outer : -1f, spot ? MathF.Max(MathF.Cos(light.InnerAngle * MathF.PI / 180f), outer + 1e-4f) : 1f, light.Shadow.Bias, light.Shadow.NormalBias),
            Shadow = new Float4(light.Shadow.Strength, light.Shadow.Quality, BitConverter.Int32BitsToSingle(-1), BitConverter.UInt32BitsToSingle(light.LayerMask)),
        };
    }

    private DecalLook Look(in DecalDesc decal, uint sequence)
    {
        Double4x4 m = decal.Transform;
        Double3 x = new(m.c0.X, m.c0.Y, m.c0.Z), y = new(m.c1.X, m.c1.Y, m.c1.Z), z = new(m.c2.X, m.c2.Y, m.c2.Z);
        double zLength = Math.Sqrt(z.X * z.X + z.Y * z.Y + z.Z * z.Z);
        Float3 axis = zLength > 0 ? new Float3((float)(z.X / zLength), (float)(z.Y / zLength), (float)(z.Z / zLength)) : new Float3(0, 0, 1);
        uint flags = (decal.Albedo != null ? 1u : 0) | (decal.Normal != null ? 2u : 0) | (decal.Mask != null ? 4u : 0);
        return new DecalLook
        {
            Inverse = m.Invert(),
            Center = new Double3(m.c3.X, m.c3.Y, m.c3.Z),
            Radius = (float)(0.5 * Math.Max(Math.Max(Length(x + y + z), Length(x + y - z)), Math.Max(Length(x - y + z), Length(y + z - x)))),
            Sequence = sequence,
            Color = decal.Color,
            Albedo = _resources.TextureSlot(decal.Albedo, MaterialPropertyType.Texture2D, "white"),
            Normal = _resources.TextureSlot(decal.Normal, MaterialPropertyType.Texture2D, "normal"),
            Mask = _resources.TextureSlot(decal.Mask, MaterialPropertyType.Texture2D, "white"),
            Sampler = _resources.SamplerSlot(decal.Sampler),
            Params = new Float4(decal.NormalStrength, decal.Priority, decal.AngleFade, BitConverter.UInt32BitsToSingle(decal.LayerMask)),
            Axis = new Float4(axis.X, axis.Y, axis.Z, BitConverter.UInt32BitsToSingle(flags)),
        };
    }

    private static double Length(Double3 v) => Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

    private static DecalRecord Pack(in DecalLook look, Double3 origin)
    {
        Double4x4 inv = look.Inverse;
        Double3 c = look.Center - origin;
        return new DecalRecord
        {
            Row0 = new Float4((float)inv.c0.X, (float)inv.c1.X, (float)inv.c2.X, (float)c.X),
            Row1 = new Float4((float)inv.c0.Y, (float)inv.c1.Y, (float)inv.c2.Y, (float)c.Y),
            Row2 = new Float4((float)inv.c0.Z, (float)inv.c1.Z, (float)inv.c2.Z, (float)c.Z),
            Sequence = look.Sequence,
            Color = look.Color,
            Albedo = look.Albedo,
            Normal = look.Normal,
            Mask = look.Mask,
            Sampler = look.Sampler,
            Params = look.Params,
            Axis = look.Axis,
        };
    }

    // The sun first: the environment's choice when it is a live directional light, else the brightest one.
    private void WriteWorldRecord()
    {
        _worldRecordDirty = false;
        EnvironmentDesc env = _environment;
        WorldLightingRecord record = default;

        _directionalOrder.Clear();
        foreach (int index in _directionalLights)
        {
            int at = _directionalOrder.Count;
            while (at > 0 && _lights[_directionalOrder[at - 1]].Intensity < _lights[index].Intensity)
                at--;
            _directionalOrder.Insert(at, index);
        }
        int sun = _directionalOrder.Count > 0 ? _directionalOrder[0] : -1;
        if (env.Sun.IsValid && (uint)env.Sun.Index < (uint)_lights.Length && _lightAlive[env.Sun.Index]
            && _lightGeneration[env.Sun.Index] == env.Sun.Generation && _lights[env.Sun.Index].Type == LightType.Directional)
            sun = env.Sun.Index;

        SkyInputs = HashCode.Combine(sun >= 0 ? _lights[sun].Direction : default, env.SkyColor);

        int count = 0;
        if (sun >= 0)
            record.Directional[count++] = Directional(_lights[sun]);
        foreach (int index in _directionalOrder)
        {
            if (index == sun)
                continue;
            if (count == WorldLightingRecord.MaxDirectional)
            {
                _report("lighting.directional", $"A world has more than {WorldLightingRecord.MaxDirectional} directional lights, the rest are not drawn.");
                break;
            }
            record.Directional[count++] = Directional(_lights[index]);
        }
        record.DirectionalCount = (uint)count;
        record.HasProbes = _hasProbes ? 1u : 0;
        record.ProbeSlot = _probeSlot;
        record.ProbeSampler = _probeSampler;
        record.ProbeMips = _probeMips;
        record.ProbesReady = _probeMips > 0 ? 1u : 0;

        record.AmbientColor = env.AmbientColor;
        record.AmbientSky = env.AmbientSky;
        record.AmbientGround = env.AmbientGround;
        record.AmbientMode = new Float4(env.Ambient == AmbientMode.Uniform ? 1 : 0, env.Ambient == AmbientMode.Hemisphere ? 1 : 0, env.AmbientIntensity, env.Ambient == AmbientMode.Sky ? 1 : 0);

        FogDesc fog = env.Fog;
        float range = fog.End - fog.Start;
        if (MathF.Abs(range) < 0.0001f)
            range = 0.0001f;
        record.FogColor = fog.Color;
        record.FogParams = new Float4(fog.Density / 1.2011224f, fog.Density / 0.693147181f, -1f / range, fog.End / range);
        record.FogStates = new Float4(fog.Mode == FogMode.Linear ? 1 : 0, fog.Mode == FogMode.Exponential ? 1 : 0, fog.Mode == FogMode.ExponentialSquared ? 1 : 0, fog.UseSky ? 1 : 0);
        World.Set(0, record);
    }

    // The direction stored points from the surface to the light, the opposite of where it shines. The layer mask rides in w.
    private static DirectionalRecord Directional(in LightDesc light) => new()
    {
        DirectionShadow = new Float4(-light.Direction.X, -light.Direction.Y, -light.Direction.Z, light.Shadow.Enabled ? 1 : 0),
        Color = new Float4(light.Color.X * light.Intensity, light.Color.Y * light.Intensity, light.Color.Z * light.Intensity, BitConverter.UInt32BitsToSingle(light.LayerMask)),
    };
}

/// <summary>
/// A static region a view sees, 32 bytes: the camera minus the region's origin split high and low, and its tree's root.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct RegionEntry
{
    public Float3 OffsetHigh;
    public uint Root;
    public Float3 OffsetLow;
    private uint _pad;
}

/// <summary>
/// Static items of one kind, split into regions of space with a tree each, built around the region's own origin, so
/// positions stay precise at any distance from the world origin. A region rebuilds only when one of its items is added,
/// removed or moved. Each view walks a small tree over the regions it sees, built every frame from the camera's offset
/// to each region's origin worked out in double, so precision follows the distance from the camera like everything else.
/// </summary>
internal sealed class StaticRegions<TRecord> where TRecord : unmanaged
{
    /// <summary>The side of the cells items are grouped by, by their center. A region's tree covers its items' full bounds.</summary>
    public const double RegionSize = 65536;

    /// <summary>Stack entries the shaders keep for walking the regions tree, after the item trees' entries.</summary>
    public const int RegionStack = 16;

    private const int LeafSize = 8;
    private const int RegionLeafSize = 4;
    private const uint MaxEntries = 1u << 26;

    private sealed class Region((long, long, long) key)
    {
        public readonly (long X, long Y, long Z) Key = key;
        public readonly List<int> Items = new();
        public Double3 Origin, Min, Max;
        public RangeAllocation Records, Nodes;
        public uint Root = LightTreeBuilder.Invalid;
        public bool Dirty;
        public int Live = -1;
    }

    private readonly Dictionary<(long, long, long), Region> _regions = new();
    private readonly List<Region> _live = new();
    private readonly List<Region> _dirty = new();
    private readonly List<Region> _retry = new();
    private readonly List<Region> _visible = new();
    private readonly List<int> _repack = new();
    private readonly RangeAllocator _recordSpace = new(MaxEntries);
    private readonly RangeAllocator _nodeSpace = new(MaxEntries);
    private readonly LightTreeBuilder _builder = new();
    private readonly Func<int, Double3, TRecord> _pack;
    private readonly Action<string, string> _report;
    private Region?[] _itemRegion = new Region?[16];
    private int[] _itemSlot = new int[16];
    private Double3[] _center = new Double3[16];
    private float[] _radius = new float[16];
    private uint[] _recordIndex = new uint[16];
    private Vector4[] _spheres = new Vector4[64];
    private LightBounds[] _boxes = new LightBounds[16];
    private RegionEntry[] _entries = new RegionEntry[16];

    public StaticRegions(GraphicsDevice device, string name, Func<int, Double3, TRecord> pack, Action<string, string> report)
    {
        _pack = pack;
        _report = report;
        Records = GpuTable.Of<TRecord>(device, name, 64);
        Nodes = GpuTable.Of<LightTreeNode>(device, name + " nodes", 16);
    }

    public GpuTable Records { get; }
    public GpuTable Nodes { get; }

    public int RegionCount => _live.Count;

    /// <summary>Adds or moves an item. One that stays where it is only has its record rewritten.</summary>
    public void Set(int item, Double3 center, float radius)
    {
        Grow(item);
        Region region = RegionAt(center);
        Region? old = _itemRegion[item];
        if (old == region && _center[item] == center && _radius[item] == radius)
        {
            _repack.Add(item);
            return;
        }

        if (old != null)
            Leave(old, item);
        _itemSlot[item] = region.Items.Count;
        region.Items.Add(item);
        _itemRegion[item] = region;
        _center[item] = center;
        _radius[item] = radius;
        MarkDirty(region);
    }

    public void Remove(int item)
    {
        if (item >= _itemRegion.Length || _itemRegion[item] is not { } region)
            return;
        Leave(region, item);
        _itemRegion[item] = null;
    }

    /// <summary>Rewrites an item's record without moving it.</summary>
    public void Repack(int item)
    {
        if (item < _itemRegion.Length && _itemRegion[item] != null)
            _repack.Add(item);
    }

    /// <summary>Rebuilds the regions that changed, retries ones the tables had no room for, and rewrites repacked records. Render thread.</summary>
    public void Update()
    {
        foreach (Region region in _retry)
            MarkDirty(region);
        _retry.Clear();

        foreach (Region region in _dirty)
            Rebuild(region);
        _dirty.Clear();

        foreach (int item in _repack)
        {
            if (_itemRegion[item] is { } region && region.Root != LightTreeBuilder.Invalid)
                Records.Set(_recordIndex[item], _pack(item, region.Origin));
        }
        _repack.Clear();
    }

    /// <summary>
    /// Builds the tree over the regions whose bounds meet the frustum, around the camera, and queues its entries and
    /// nodes. Returns the root, or Invalid when no region is in view. Render thread.
    /// </summary>
    public uint BuildView(in CullFrustum frustum, LightTreeBuilder builder, Uploader uploader, GpuArray entries, GpuArray nodes)
    {
        _visible.Clear();
        foreach (Region region in _live)
        {
            if (region.Root == LightTreeBuilder.Invalid)
                continue;
            Double3 center = (region.Min + region.Max) * 0.5;
            Double3 half = (region.Max - region.Min) * 0.5;
            if (frustum.Box(center, new Float3((float)half.X, (float)half.Y, (float)half.Z)))
                _visible.Add(region);
        }
        if (_visible.Count == 0)
            return LightTreeBuilder.Invalid;

        if (_boxes.Length < _visible.Count)
            _boxes = new LightBounds[_visible.Count * 2];
        for (int i = 0; i < _visible.Count; i++)
            _boxes[i] = new LightBounds { Min = Outward(frustum, _visible[i].Min, -1), Max = Outward(frustum, _visible[i].Max, 1) };

        builder.Build(_boxes.AsSpan(0, _visible.Count), RegionLeafSize, morton: true);
        if ((builder.Depth - 1) * 3 > RegionStack)
            _report("lighting.regions", $"A view sees {_visible.Count} static light or decal regions, so deep a tree that overlapping regions may be skipped.");

        if (_entries.Length < _visible.Count)
            _entries = new RegionEntry[_visible.Count * 2];
        ReadOnlySpan<int> order = builder.Order;
        for (int i = 0; i < _visible.Count; i++)
        {
            Region region = _visible[order[i]];
            (Float3 high, Float3 low) = Precise.Split(frustum.Camera - region.Origin);
            _entries[i] = new RegionEntry { OffsetHigh = high, OffsetLow = low, Root = region.Root };
        }
        entries.Write<RegionEntry>(uploader, _entries.AsSpan(0, _visible.Count));
        nodes.Write(uploader, builder.Nodes);
        return builder.NodeCount > 0 ? 0u : LightTreeBuilder.Invalid;
    }

    // A camera relative corner rounded outward, so the float box still holds the region
    private static System.Numerics.Vector3 Outward(in CullFrustum frustum, Double3 corner, int sign)
    {
        Float3 p = frustum.Relative(corner);
        static float Push(float v, int sign) => v + sign * (MathF.Abs(v) * 1e-6f + 1e-3f);
        return new System.Numerics.Vector3(Push(p.X, sign), Push(p.Y, sign), Push(p.Z, sign));
    }

    private Region RegionAt(Double3 center)
    {
        (long, long, long) key = ((long)Math.Floor(center.X / RegionSize), (long)Math.Floor(center.Y / RegionSize), (long)Math.Floor(center.Z / RegionSize));
        if (!_regions.TryGetValue(key, out Region? region))
        {
            region = new Region(key) { Live = _live.Count };
            _regions[key] = region;
            _live.Add(region);
        }
        return region;
    }

    private void Leave(Region region, int item)
    {
        int slot = _itemSlot[item];
        int last = region.Items[^1];
        region.Items[slot] = last;
        _itemSlot[last] = slot;
        region.Items.RemoveAt(region.Items.Count - 1);
        MarkDirty(region);
    }

    private void MarkDirty(Region region)
    {
        if (region.Dirty)
            return;
        region.Dirty = true;
        _dirty.Add(region);
    }

    private void Rebuild(Region region)
    {
        region.Dirty = false;
        if (region.Records.Size > 0)
            _recordSpace.Free(region.Records);
        if (region.Nodes.Size > 0)
            _nodeSpace.Free(region.Nodes);
        region.Records = default;
        region.Nodes = default;
        region.Root = LightTreeBuilder.Invalid;
        if (region.Items.Count == 0)
        {
            _regions.Remove(region.Key);
            Region moved = _live[^1];
            _live[region.Live] = moved;
            moved.Live = region.Live;
            _live.RemoveAt(_live.Count - 1);
            region.Live = -1;
            return;
        }

        Double3 min = new(double.MaxValue), max = new(double.MinValue);
        foreach (int item in region.Items)
        {
            Double3 c = _center[item];
            double r = _radius[item];
            min = new Double3(Math.Min(min.X, c.X - r), Math.Min(min.Y, c.Y - r), Math.Min(min.Z, c.Z - r));
            max = new Double3(Math.Max(max.X, c.X + r), Math.Max(max.Y, c.Y + r), Math.Max(max.Z, c.Z + r));
        }
        region.Min = min;
        region.Max = max;
        region.Origin = (min + max) * 0.5;

        int count = region.Items.Count;
        Span<Vector4> spheres = Spheres(count);
        for (int i = 0; i < count; i++)
        {
            int item = region.Items[i];
            Double3 p = _center[item] - region.Origin;
            spheres[i] = new Vector4((float)p.X, (float)p.Y, (float)p.Z, _radius[item]);
        }

        _builder.Build(spheres, LeafSize, morton: false);
        if ((_builder.Depth - 1) * 3 > LightTreeBuilder.ShaderStack)
            _report("lighting.depth", $"A static light or decal region is {_builder.Depth} levels deep, more than the shader's traversal stack holds, so some may be skipped.");
        if (!_recordSpace.TryAllocate((uint)count, 1, out RangeAllocation records))
        {
            Full(region);
            return;
        }
        if (!_nodeSpace.TryAllocate((uint)_builder.NodeCount, 1, out RangeAllocation nodes))
        {
            _recordSpace.Free(records);
            Full(region);
            return;
        }

        region.Records = records;
        region.Nodes = nodes;
        ReadOnlySpan<int> order = _builder.Order;
        for (int i = 0; i < count; i++)
        {
            int item = region.Items[order[i]];
            _recordIndex[item] = records.Offset + (uint)i;
            Records.Set(records.Offset + (uint)i, _pack(item, region.Origin));
        }

        // Child indices and leaf runs move to where the region's nodes and records landed in the shared tables
        ReadOnlySpan<LightTreeNode> built = _builder.Nodes;
        for (int i = 0; i < built.Length; i++)
        {
            LightTreeNode node = built[i];
            node.Child0 = Relocate(node.Child0, nodes.Offset, records.Offset);
            node.Child1 = Relocate(node.Child1, nodes.Offset, records.Offset);
            node.Child2 = Relocate(node.Child2, nodes.Offset, records.Offset);
            node.Child3 = Relocate(node.Child3, nodes.Offset, records.Offset);
            Nodes.Set(nodes.Offset + (uint)i, node);
        }
        region.Root = nodes.Offset;
    }

    // The tables have no room: the region waits undrawn and tries again next frame
    private void Full(Region region)
    {
        _report("lighting.regions.full", "The static light or decal tables are full, a region is not drawn.");
        _retry.Add(region);
    }

    private static uint Relocate(uint child, uint nodeBase, uint recordBase)
    {
        if (child == LightTreeBuilder.Invalid)
            return child;
        if ((child & LightTreeBuilder.LeafBit) != 0)
            return LightTreeBuilder.LeafBit | ((child & ~LightTreeBuilder.LeafBit) + recordBase);
        return child + nodeBase;
    }

    private void Grow(int item)
    {
        if (item < _itemRegion.Length)
            return;
        int size = Math.Max(item + 1, _itemRegion.Length * 2);
        Array.Resize(ref _itemRegion, size);
        Array.Resize(ref _itemSlot, size);
        Array.Resize(ref _center, size);
        Array.Resize(ref _radius, size);
        Array.Resize(ref _recordIndex, size);
    }

    private Span<Vector4> Spheres(int count)
    {
        if (_spheres.Length < count)
            _spheres = new Vector4[Math.Max(count, _spheres.Length * 2)];
        return _spheres.AsSpan(0, count);
    }
}

/// <summary>Texture and sampler slots for a world without a renderer, as in tests: always slot 0.</summary>
internal sealed class NoMaterialResources : IMaterialResources
{
    public static readonly NoMaterialResources Instance = new();

    public uint TextureSlot(Texture? texture, MaterialPropertyType type, string defaultName) => 0;

    public uint SamplerSlot(SamplerDescription? sampler) => 0;
}
