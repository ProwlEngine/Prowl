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
}

public enum FogMode : byte
{
    Off,
    Linear,
    Exponential,
    ExponentialSquared,
}

public struct FogDesc
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

    internal EnvironmentDesc Copy() => (EnvironmentDesc)MemberwiseClone();

    internal bool Same(EnvironmentDesc other)
        => Ambient == other.Ambient && AmbientColor == other.AmbientColor && AmbientSky == other.AmbientSky && AmbientGround == other.AmbientGround
        && AmbientIntensity == other.AmbientIntensity && Fog.Equals(other.Fog) && Sun == other.Sun;
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
/// part with the high part of the centre in w, then the low part of the centre and the decal's id.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DecalRecord
{
    public Float4 Row0, Row1, Row2;
    public Float4 CenterLow;
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
    public uint LightmapPages;
    public uint HasProbes;
    public uint LightmapEncoding;
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
        if (_extractedEnvironment == null || !_environment.Same(_extractedEnvironment))
        {
            _extractedEnvironment = _environment.Copy();
            Lighting.ReceiveEnvironment(_extractedEnvironment);
        }
    }
}

/// <summary>Host storage for lights and decals: descriptions keyed by a dense index with a free list, generation checked.</summary>
internal sealed class HandleStore<T> where T : struct
{
    private T[] _items = new T[16];
    private int[] _generation = new int[16];
    private bool[] _alive = new bool[16];
    private bool[] _dirty = new bool[16];
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
        }
        _generation[index]++;
        _alive[index] = true;
        _items[index] = item;
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

    public delegate void Receiver(int index, int generation, bool alive, in T item);

    public void Extract(Receiver receive)
    {
        foreach (int index in _dirtyList)
        {
            _dirty[index] = false;
            receive(index, _generation[index], _alive[index], _items[index]);
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
/// and decals build one tree each per world, only when one changes. Moving ones are culled to each view and built into
/// a fresh tree per view every frame. Directional lights, ambient light and fog live in one world record.
/// </summary>
internal sealed class WorldLighting : IDisposable
{
    private const int StaticLeafSize = 8;

    private readonly IMaterialResources _resources;
    private readonly Action<string, string> _report;
    private readonly LightTreeBuilder _staticLightTree = new();
    private readonly LightTreeBuilder _staticDecalTree = new();

    private LightDesc[] _lights = new LightDesc[16];
    private int[] _lightGeneration = new int[16];
    private bool[] _lightAlive = new bool[16];
    private int[] _lightTableIndex = new int[16];
    private readonly List<int> _staticLights = new();
    private readonly List<int> _dynamicLights = new();
    private readonly List<int> _directionalLights = new();
    private bool _lightMembership = true;
    private bool _staticLightsMoved = true;
    private readonly List<int> _staticLightRecords = new();

    private DecalDesc[] _decals = new DecalDesc[16];
    private bool[] _decalAlive = new bool[16];
    private DecalLook[] _decalLooks = new DecalLook[16];
    private int[] _decalTableIndex = new int[16];
    private readonly List<int> _staticDecals = new();
    private readonly List<int> _dynamicDecals = new();
    private bool _decalMembership = true;
    private bool _staticDecalsMoved = true;
    private readonly List<int> _staticDecalRecords = new();

    private EnvironmentDesc _environment = new();
    private bool _worldRecordDirty = true;
    private int _lightmapPages;
    private LightmapEncoding _lightmapEncoding;
    private bool _hasProbes;
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
        public uint Id;
    }

    public WorldLighting(GraphicsDevice device, IMaterialResources resources, Action<string, string> report)
    {
        _resources = resources;
        _report = report;
        StaticLights = GpuTable.Of<LightRecord>(device, "Static lights", 64);
        StaticLightNodes = GpuTable.Of<LightTreeNode>(device, "Static light nodes", 16);
        StaticDecals = GpuTable.Of<DecalRecord>(device, "Static decals", 16);
        StaticDecalNodes = GpuTable.Of<LightTreeNode>(device, "Static decal nodes", 16);
        World = GpuTable.Of<WorldLightingRecord>(device, "World lighting", 1);
        Tables = [StaticLights, StaticLightNodes, StaticDecals, StaticDecalNodes, World];
    }

    public GpuTable StaticLights { get; }
    public GpuTable StaticLightNodes { get; }
    public GpuTable StaticDecals { get; }
    public GpuTable StaticDecalNodes { get; }
    public GpuTable World { get; }
    public GpuTable[] Tables { get; }

    public IMaterialResources Resources => _resources;

    /// <summary>World position the static light tree is built around. Shaders shift camera relative positions by the view's offset to it.</summary>
    public Double3 StaticLightOrigin { get; private set; }
    public Double3 StaticDecalOrigin { get; private set; }

    public uint StaticLightRoot { get; private set; } = LightTreeBuilder.Invalid;
    public uint StaticDecalRoot { get; private set; } = LightTreeBuilder.Invalid;

    public int StaticLightCount => _staticLights.Count;
    public int DynamicLightCount => _dynamicLights.Count;

    internal void ReceiveLight(int index, int generation, bool alive, in LightDesc given)
    {
        if (index >= _lights.Length)
        {
            int size = Math.Max(index + 1, _lights.Length * 2);
            Array.Resize(ref _lights, size);
            Array.Resize(ref _lightGeneration, size);
            Array.Resize(ref _lightAlive, size);
            Array.Resize(ref _lightTableIndex, size);
        }

        LightDesc desc = Normalized(given);
        LightDesc old = _lights[index];
        bool wasAlive = _lightAlive[index];
        _lights[index] = desc;
        _lightGeneration[index] = generation;
        _lightAlive[index] = alive;

        bool wasStatic = wasAlive && old.Static && old.Type != LightType.Directional;
        bool isStatic = alive && desc.Static && desc.Type != LightType.Directional;
        if (wasAlive != alive || old.Type != desc.Type || old.Static != desc.Static)
            _lightMembership = true;
        if (wasStatic || isStatic)
        {
            if (!wasStatic || !isStatic || old.Position != desc.Position || old.Range != desc.Range)
                _staticLightsMoved = true;
            else
                _staticLightRecords.Add(index);
        }
        if (old.Type == LightType.Directional || desc.Type == LightType.Directional)
            _worldRecordDirty = true;
    }

    internal void ReceiveDecal(int index, int generation, bool alive, in DecalDesc desc)
    {
        if (index >= _decals.Length)
        {
            int size = Math.Max(index + 1, _decals.Length * 2);
            Array.Resize(ref _decals, size);
            Array.Resize(ref _decalAlive, size);
            Array.Resize(ref _decalLooks, size);
            Array.Resize(ref _decalTableIndex, size);
        }

        DecalDesc old = _decals[index];
        bool wasAlive = _decalAlive[index];
        _decals[index] = desc;
        _decalAlive[index] = alive;
        if (alive)
            _decalLooks[index] = Look(desc, index);

        bool wasStatic = wasAlive && old.Static;
        bool isStatic = alive && desc.Static;
        if (wasAlive != alive || old.Static != desc.Static)
            _decalMembership = true;
        if (wasStatic || isStatic)
        {
            if (!wasStatic || !isStatic || old.Transform != desc.Transform)
                _staticDecalsMoved = true;
            else
                _staticDecalRecords.Add(index);
        }
    }

    internal void ReceiveEnvironment(EnvironmentDesc environment)
    {
        _environment = new EnvironmentDesc
        {
            Ambient = environment.Ambient,
            AmbientColor = environment.AmbientColor,
            AmbientSky = environment.AmbientSky,
            AmbientGround = environment.AmbientGround,
            AmbientIntensity = environment.AmbientIntensity,
            Fog = environment.Fog,
            Sun = environment.Sun,
        };
        _worldRecordDirty = true;
    }

    internal void SetLightmaps(int pages, LightmapEncoding encoding)
    {
        _lightmapPages = pages;
        _lightmapEncoding = encoding;
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
            _decalLooks[i] = Look(_decals[i], i);
            if (_decals[i].Static)
                _staticDecalRecords.Add(i);
        }
    }

    /// <summary>Rebuilds the static trees that changed and rewrites the records and the world record. Render thread.</summary>
    public void Update()
    {
        if (_lightMembership)
            RebuildLightLists();
        if (_decalMembership)
            RebuildDecalLists();

        if (_staticLightsMoved)
            BuildStaticLights();
        else
        {
            foreach (int index in _staticLightRecords)
            {
                if (_lightAlive[index] && _lights[index].Static)
                    StaticLights.Set((uint)_lightTableIndex[index], Pack(_lights[index], StaticLightOrigin));
            }
        }
        _staticLightRecords.Clear();

        if (_staticDecalsMoved)
            BuildStaticDecals();
        else
        {
            foreach (int index in _staticDecalRecords)
            {
                if (_decalAlive[index] && _decals[index].Static)
                    StaticDecals.Set((uint)_decalTableIndex[index], Pack(_decalLooks[index]));
            }
        }
        _staticDecalRecords.Clear();

        if (_worldRecordDirty)
            WriteWorldRecord();
    }

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
        CheckDepth(builder);
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
        CheckDepth(builder);
        if (_decalScratch.Length < _culled.Count)
            _decalScratch = new DecalRecord[_culled.Count * 2];
        ReadOnlySpan<int> order = builder.Order;
        for (int i = 0; i < _culled.Count; i++)
            _decalScratch[i] = Pack(_decalLooks[_culled[order[i]]]);
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
        _staticLights.Clear();
        _dynamicLights.Clear();
        _directionalLights.Clear();
        for (int i = 0; i < _lights.Length; i++)
        {
            if (!_lightAlive[i])
                continue;
            if (_lights[i].Type == LightType.Directional)
                _directionalLights.Add(i);
            else if (_lights[i].Static)
                _staticLights.Add(i);
            else
                _dynamicLights.Add(i);
        }
    }

    private void RebuildDecalLists()
    {
        _decalMembership = false;
        _staticDecals.Clear();
        _dynamicDecals.Clear();
        for (int i = 0; i < _decals.Length; i++)
        {
            if (_decalAlive[i])
                (_decals[i].Static ? _staticDecals : _dynamicDecals).Add(i);
        }
    }

    private void BuildStaticLights()
    {
        _staticLightsMoved = false;
        int count = _staticLights.Count;
        StaticLightOrigin = Center(count, i => _lights[_staticLights[i]].Position);
        Span<Vector4> spheres = Spheres(count);
        for (int i = 0; i < count; i++)
        {
            LightDesc light = _lights[_staticLights[i]];
            Double3 p = light.Position - StaticLightOrigin;
            spheres[i] = new Vector4((float)p.X, (float)p.Y, (float)p.Z, Math.Max(light.Range, 1e-4f));
        }

        _staticLightTree.Build(spheres, StaticLeafSize, morton: false);
        CheckDepth(_staticLightTree);
        ReadOnlySpan<int> order = _staticLightTree.Order;
        for (int i = 0; i < count; i++)
        {
            int index = _staticLights[order[i]];
            _lightTableIndex[index] = i;
            StaticLights.Set((uint)i, Pack(_lights[index], StaticLightOrigin));
        }
        WriteNodes(StaticLightNodes, _staticLightTree.Nodes);
        StaticLightRoot = _staticLightTree.NodeCount > 0 ? 0u : LightTreeBuilder.Invalid;
    }

    private void BuildStaticDecals()
    {
        _staticDecalsMoved = false;
        int count = _staticDecals.Count;
        StaticDecalOrigin = Center(count, i => _decalLooks[_staticDecals[i]].Center);
        Span<Vector4> spheres = Spheres(count);
        for (int i = 0; i < count; i++)
        {
            DecalLook look = _decalLooks[_staticDecals[i]];
            Double3 p = look.Center - StaticDecalOrigin;
            spheres[i] = new Vector4((float)p.X, (float)p.Y, (float)p.Z, look.Radius);
        }

        _staticDecalTree.Build(spheres, StaticLeafSize, morton: false);
        CheckDepth(_staticDecalTree);
        ReadOnlySpan<int> order = _staticDecalTree.Order;
        for (int i = 0; i < count; i++)
        {
            int index = _staticDecals[order[i]];
            _decalTableIndex[index] = i;
            StaticDecals.Set((uint)i, Pack(_decalLooks[index]));
        }
        WriteNodes(StaticDecalNodes, _staticDecalTree.Nodes);
        StaticDecalRoot = _staticDecalTree.NodeCount > 0 ? 0u : LightTreeBuilder.Invalid;
    }

    private static void WriteNodes(GpuTable table, ReadOnlySpan<LightTreeNode> nodes)
    {
        for (int i = 0; i < nodes.Length; i++)
            table.Set((uint)i, nodes[i]);
    }

    private void CheckDepth(LightTreeBuilder tree)
    {
        if (tree.Depth * 3 > LightTreeBuilder.ShaderStack)
            _report("lighting.depth", $"A light or decal tree is {tree.Depth} levels deep, more than the shader's traversal stack holds, so some may be skipped.");
    }

    private Span<Vector4> Spheres(int count)
    {
        if (_spheres.Length < count)
            _spheres = new Vector4[Math.Max(count, _spheres.Length * 2)];
        return _spheres.AsSpan(0, count);
    }

    private static Double3 Center(int count, Func<int, Double3> position)
    {
        if (count == 0)
            return default;
        Double3 min = new(double.MaxValue), max = new(double.MinValue);
        for (int i = 0; i < count; i++)
        {
            Double3 p = position(i);
            min = new Double3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
            max = new Double3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
        }
        return (min + max) * 0.5;
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

    private DecalLook Look(in DecalDesc decal, int id)
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
            Id = (uint)id,
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

    private static DecalRecord Pack(in DecalLook look)
    {
        Double4x4 inv = look.Inverse;
        (Float3 high, Float3 low) = Precise.Split(look.Center);
        return new DecalRecord
        {
            Row0 = new Float4((float)inv.c0.X, (float)inv.c1.X, (float)inv.c2.X, high.X),
            Row1 = new Float4((float)inv.c0.Y, (float)inv.c1.Y, (float)inv.c2.Y, high.Y),
            Row2 = new Float4((float)inv.c0.Z, (float)inv.c1.Z, (float)inv.c2.Z, high.Z),
            CenterLow = new Float4(low.X, low.Y, low.Z, BitConverter.UInt32BitsToSingle(look.Id)),
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
        _directionalOrder.AddRange(_directionalLights);
        _directionalOrder.Sort((a, b) => _lights[b].Intensity.CompareTo(_lights[a].Intensity));
        int sun = _directionalOrder.Count > 0 ? _directionalOrder[0] : -1;
        if (env.Sun.IsValid && (uint)env.Sun.Index < (uint)_lights.Length && _lightAlive[env.Sun.Index]
            && _lightGeneration[env.Sun.Index] == env.Sun.Generation && _lights[env.Sun.Index].Type == LightType.Directional)
            sun = env.Sun.Index;

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
        record.LightmapPages = (uint)_lightmapPages;
        record.HasProbes = _hasProbes ? 1u : 0;
        record.LightmapEncoding = (uint)_lightmapEncoding;

        record.AmbientColor = env.AmbientColor;
        record.AmbientSky = env.AmbientSky;
        record.AmbientGround = env.AmbientGround;
        record.AmbientMode = new Float4(env.Ambient == AmbientMode.Uniform ? 1 : 0, env.Ambient == AmbientMode.Hemisphere ? 1 : 0, env.AmbientIntensity, 0);

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

/// <summary>Texture and sampler slots for a world without a renderer, as in tests: always slot 0.</summary>
internal sealed class NoMaterialResources : IMaterialResources
{
    public static readonly NoMaterialResources Instance = new();

    public uint TextureSlot(Texture? texture, MaterialPropertyType type, string defaultName) => 0;

    public uint SamplerSlot(SamplerDescription? sampler) => 0;
}
