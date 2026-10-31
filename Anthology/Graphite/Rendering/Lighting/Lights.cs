using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

using Vector4 = System.Numerics.Vector4;

namespace Prowl.Graphite.Rendering;

public enum LightType : byte
{
    Directional,

    /// <summary>A point, or a sphere of <see cref="LightDesc.SourceRadius"/>.</summary>
    Point,

    /// <summary>A cone along the direction, from a point or a sphere of <see cref="LightDesc.SourceRadius"/>.</summary>
    Spot,

    /// <summary>A one sided rectangle facing the direction, <see cref="LightDesc.Size"/> wide along right and high along up.</summary>
    Rect,

    /// <summary>A one sided disk facing the direction, of radius <see cref="LightDesc.Size"/> X.</summary>
    Disk,

    /// <summary>A capsule along the direction, <see cref="LightDesc.Size"/> X long, of radius <see cref="LightDesc.SourceRadius"/>.</summary>
    Tube,
}

/// <summary>How a light casts shadows. Spot and directional lights cast them, other kinds not yet.</summary>
public struct ShadowDesc
{
    public bool Enabled;

    /// <summary>A spot light's largest tile, or each of a directional light's cascades, in texels.</summary>
    public uint Resolution;

    /// <summary>How far receivers move toward the light before the test, in texels.</summary>
    public float DepthBias;

    /// <summary>How far receivers move along their geometric normal before the test, in texels.</summary>
    public float NormalBias;

    /// <summary>A directional light's cascades, 1 to 4.</summary>
    public byte Cascades;

    /// <summary>How far along the view a directional light casts, fading over the last tenth.</summary>
    public float Distance;

    /// <summary>The fraction of each cascade blended into the next, 0 for a hard switch.</summary>
    public float CascadeBlend;
    public float Strength;
    public ShadowFilter Filter;

    public ShadowDesc()
    {
        Resolution = 1024;
        DepthBias = 1;
        NormalBias = 1;
        Cascades = 4;
        Distance = 50;
        CascadeBlend = 0.1f;
        Strength = 1;
        Filter = ShadowFilter.Tent;
    }
}

public struct LightDesc
{
    public LightType Type;

    /// <summary>Name debug tools show, or null.</summary>
    public string? Name;

    /// <summary>World position, double so lights far from the origin keep their place.</summary>
    public Double3 Position;

    /// <summary>The direction the light shines or faces, world space, the axis of a tube.</summary>
    public Float3 Direction;

    /// <summary>Up for the light's shape, cookie and IES profile, made square to the direction.</summary>
    public Float3 Up;

    /// <summary>Linear colour.</summary>
    public Float3 Color;

    /// <summary>
    /// Illuminance in lux for a directional light, luminous intensity in candela for a point or spot, luminance in nits
    /// for a rect, disk or tube.
    /// </summary>
    public float Intensity;

    /// <summary>
    /// Makes a spot's intensity the flux of a point light of that intensity, focused into its outer cone, so narrowing
    /// the cone brightens it rather than leaving it as a masked point light.
    /// </summary>
    public bool NormalizeSpotCone;

    /// <summary>Distance from its centre at which a local light reaches zero.</summary>
    public float Range;

    /// <summary>The radius of a sphere light, a point or spot of a size, and of a tube.</summary>
    public float SourceRadius;

    /// <summary>A rect's width and height, a disk's radius in X, a tube's length in X.</summary>
    public Float2 Size;

    /// <summary>The angular radius of a directional light's disk in degrees, the sun's about 0.27.</summary>
    public float AngularRadius;

    /// <summary>
    /// A texture the light shines through: a 2D one for a spot, across its cone with U along right and V along up, a cube
    /// for a point, in its own space of right, up and direction.
    /// </summary>
    public Texture? Cookie;

    /// <summary>
    /// A photometric profile as a 2D texture: the angle from the direction across U's texel centres from 0 to 180 degrees,
    /// the angle around it from up toward right across V from 0 to 360, relative intensity in R. For points and spots.
    /// </summary>
    public Texture? IesProfile;

    /// <summary>How the cookie and profile are sampled.</summary>
    public SamplerDescription? CookieSampler;

    /// <summary>Data for custom lighting, read by shaders through Graphite_LightHostData.</summary>
    public Float4 HostData;

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
        Up = new Float3(0, 1, 0);
        Color = new Float3(1, 1, 1);
        Intensity = 1;
        Range = 10;
        InnerAngle = 30;
        OuterAngle = 45;
        LayerMask = ~0u;
        Shadow = new ShadowDesc();
    }
}

/// <summary>A light in a world. Generation checked, so a handle of a removed light stays invalid.</summary>
[Serializable]
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

    /// <summary>Two more textures for a custom surface shader's decals, read as Graphite_DecalRecord.extra.</summary>
    public Texture? Extra0, Extra1;

    /// <summary>Data for a custom surface shader's decals, read as Graphite_DecalRecord.hostData.</summary>
    public Float4 HostData;

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
    /// <summary>The uniform and hemisphere ambient light, and the fog's colour, are radiance in nits.</summary>
    public Float4 AmbientColor = new(0.43f, 0.55f, 0.65f, 1);
    public Float4 AmbientSky = new(0.3f, 0.3f, 0.4f, 1);
    public Float4 AmbientGround = new(0.2f, 0.2f, 0.2f, 1);
    public float AmbientIntensity = 1;
    public FogDesc Fog = new();

    /// <summary>The directional light that is the sun, else the most intense directional light is.</summary>
    public LightHandle Sun;

    /// <summary>The sky, a material with a Sky pass, drawn behind everything and captured into the sky probe. Null for none.</summary>
    public Material? Sky;

    /// <summary>What the sky probe sees where no sky material draws, in nits.</summary>
    public Float4 SkyColor = new(0.2f, 0.3f, 0.5f, 1);
    public SkyCaptureMode SkyCapture;

    /// <summary>Edge length of the sky probe's cube, a power of two.</summary>
    public uint SkyProbeResolution = 128;

    /// <summary>Frames at least between two sky captures in <see cref="SkyCaptureMode.OnChange"/>.</summary>
    public int SkyCaptureInterval = 2;

    /// <summary>
    /// The exposure value the sky probe and reflection probes are captured at, which keeps their half float texels in
    /// range. Captures store it and are rescaled to each view's exposure when sampled, so it changes no image.
    /// </summary>
    public float CaptureEV100 = 15;

    internal EnvironmentDesc Copy() => (EnvironmentDesc)MemberwiseClone();

    internal bool Same(EnvironmentDesc other)
        => Ambient == other.Ambient && AmbientColor == other.AmbientColor && AmbientSky == other.AmbientSky && AmbientGround == other.AmbientGround
        && AmbientIntensity == other.AmbientIntensity && Fog == other.Fog && Sun == other.Sun
        && Sky == other.Sky && SkyColor == other.SkyColor && SkyCapture == other.SkyCapture
        && SkyProbeResolution == other.SkyProbeResolution && SkyCaptureInterval == other.SkyCaptureInterval && CaptureEV100 == other.CaptureEV100;
}

/// <summary>A light record as the shaders read it, 144 bytes, matching Graphite_LightRecord. Positions are in its tree's space.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct LightRecord
{
    public Float4 PositionRange;
    public Float4 ColorIntensity;
    public Float4 DirectionType;
    public Float4 UpSize;
    public Float4 Shape;
    public Float4 SpotShadow;
    public Float4 Shadow;
    public uint Cookie, Ies, Sampler;
    private uint _pad;
    public Float4 HostData;
}

/// <summary>
/// A decal record as the shaders read it, 160 bytes, matching Graphite_DecalRecord. Its rows are the inverse of the linear
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
    public uint Extra0, Extra1;
    private uint _pad3, _pad4;
    public Float4 HostData;
}

/// <summary>A directional light as the shaders read it, 80 bytes, matching Graphite_DirectionalRecord.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DirectionalRecord
{
    public Float4 DirectionShadow;
    public Float4 Color;
    public Float4 Params;
    public Float4 HostData;

    /// <summary>Shadow depth and normal bias, strength, filter.</summary>
    public Float4 Shadow;
}

/// <summary>A world's ambient light, fog and light counts, matching Graphite_WorldLighting. Directional lights have their own buffer, the sun first.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WorldLightingRecord
{
    public uint DirectionalCount;
    private uint _pad0;
    public uint HasProbes;
    private uint _pad1;
    public uint ProbeSlot;
    public uint ProbeSampler;
    public uint ProbeMips;
    public uint ProbesReady;

    /// <summary>One over the exposure the sky probe and its harmonics were captured at.</summary>
    public Float4 SkyScale;
    public Float4 AmbientColor;
    public Float4 AmbientSky;
    public Float4 AmbientGround;
    public Float4 AmbientMode;
    public Float4 FogColor;
    public Float4 FogParams;
    public Float4 FogStates;
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
        Globals?.SetKeyword("GRAPHITE_LIGHT_MASKS", VariantSpace.ToggleValues[Lighting.MaskedLights > 0 ? 1 : 0]);
        Globals?.SetKeyword("GRAPHITE_CONTACT_SHADOWS", VariantSpace.ToggleValues[Lighting.ContactLights > 0 ? 1 : 0]);
        Globals?.SetKeyword("GRAPHITE_DECALS", VariantSpace.ToggleValues[Lighting.LiveDecals > 0 ? 1 : 0]);
        Globals?.SnapshotKeywords();
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
internal sealed class WorldLighting
{
    private readonly IMaterialResources _resources;
    private readonly Action<RendererError> _report;

    private LightDesc[] _lights = new LightDesc[16];
    private LightSlots[] _lightSlots = new LightSlots[16];
    private int[] _lightGeneration = new int[16];
    private bool[] _lightAlive = new bool[16];
    private readonly List<int> _directionalLights = new();
    private bool _lightMembership = true;

    /// <summary>Live lights with a cookie or profile, and live lights with contact hardening shadows.</summary>
    public int MaskedLights { get; private set; }
    public int ContactLights { get; private set; }

    /// <summary>Live decals.</summary>
    public int LiveDecals { get; private set; }

    private DecalDesc[] _decals = new DecalDesc[16];
    private bool[] _decalAlive = new bool[16];
    private DecalLook[] _decalLooks = new DecalLook[16];
    private uint[] _decalSequence = new uint[16];

    private EnvironmentDesc _environment = new();
    private bool _worldRecordDirty = true;
    private bool _hasProbes;
    private uint _probeSlot, _probeSampler, _probeMips;
    private float _skyExposure = 1;
    private readonly List<int> _directionalOrder = new();

    private struct LightSlots
    {
        public uint Cookie, Ies, Sampler, Flags;
    }

    /// <summary>What a decal looks like, independent of where it is: texture slots, tint and parameters.</summary>
    private struct DecalLook
    {
        public Double4x4 Inverse;
        public Double3 Center;
        public float Radius;
        public Float4 Color;
        public uint Albedo, Normal, Mask, Sampler, Extra0, Extra1;
        public Float4 Params;
        public Float4 Axis;
        public Float4 HostData;
        public uint Sequence;
    }

    public WorldLighting(TableHost tables, IMaterialResources resources, Action<RendererError> report)
    {
        _resources = resources;
        _report = report;
        Lights = new SpatialIndex<LightRecord>(tables.Scope("Lights"), SpatialIndex.LightsSet, Pack, report);
        Decals = new SpatialIndex<DecalRecord>(tables.Scope("Decals"), SpatialIndex.DecalsSet, (index, origin) => Pack(_decalLooks[index], origin), report);
        World = tables.Of<WorldLightingRecord>("World", 1);
        Directionals = tables.Of<DirectionalRecord>("Directionals", 8);
        Directionals.Set(0, default(DirectionalRecord));
        Tables = [Lights.StaticRecords, Lights.StaticNodes, Decals.StaticRecords, Decals.StaticNodes, World, Directionals];
    }

    /// <summary>Point, spot and area lights, static ones in regions and moving ones in a tree per view.</summary>
    public SpatialIndex<LightRecord> Lights { get; }
    public SpatialIndex<DecalRecord> Decals { get; }
    public GpuTable World { get; }

    /// <summary>Every directional light, the sun first, as many as there are.</summary>
    public GpuTable Directionals { get; }
    public GpuTable[] Tables { get; }

    public IMaterialResources Resources => _resources;

    /// <summary>One past the highest light index received.</summary>
    public int LightCapacity => _lights.Length;

    public bool IsAlive(int index) => _lightAlive[index];

    /// <summary>A light as of the last extraction, normalized. Render thread.</summary>
    public ref readonly LightDesc Light(int index) => ref _lights[index];

    public LightHandle Handle(int index) => new(index, _lightGeneration[index]);

    /// <summary>The directional lights' indices in the order shaders read them, the sun first.</summary>
    public IReadOnlyList<int> DirectionalOrder => _shaderOrder;
    private readonly List<int> _shaderOrder = new();

    internal void ReceiveLight(int index, int generation, uint sequence, bool alive, in LightDesc given)
    {
        if (index >= _lights.Length)
        {
            int size = Math.Max(index + 1, _lights.Length * 2);
            Array.Resize(ref _lights, size);
            Array.Resize(ref _lightSlots, size);
            Array.Resize(ref _lightGeneration, size);
            Array.Resize(ref _lightAlive, size);
        }

        LightDesc desc = Normalized(given);
        LightDesc old = _lights[index];
        bool wasAlive = _lightAlive[index];
        if (wasAlive)
            CountFeatures(old, _lightSlots[index], -1);
        _lights[index] = desc;
        _lightGeneration[index] = generation;
        _lightAlive[index] = alive;
        if (alive)
        {
            _lightSlots[index] = Slots(desc);
            CountFeatures(desc, _lightSlots[index], 1);
        }

        if (wasAlive != alive || old.Type != desc.Type || old.Static != desc.Static)
            _lightMembership = true;
        if (alive && desc.Type != LightType.Directional)
            Lights.Set(index, desc.Position, Math.Max(desc.Range, 1e-4f), desc.Static);
        else
            Lights.Remove(index);
        if ((wasAlive && old.Type == LightType.Directional) || (alive && desc.Type == LightType.Directional))
            _worldRecordDirty = true;
    }

    // The lights needing shader code that is only compiled in while one exists
    private void CountFeatures(in LightDesc light, in LightSlots slots, int delta)
    {
        if ((slots.Flags & 96u) != 0)
            MaskedLights += delta;
        if (light.Shadow.Enabled && light.Shadow.Filter == ShadowFilter.ContactHardening)
            ContactLights += delta;
    }

    // The cookie and profile's bindless slots, with the record's flags for what the light has
    private LightSlots Slots(in LightDesc light)
    {
        LightSlots slots = default;
        bool point = light.Type == LightType.Point, spot = light.Type == LightType.Spot;
        if (light.Cookie is { IsDisposed: false } cookie && (point || spot))
        {
            bool cube = cookie.Type == TextureType.TextureCube;
            if (cube == point)
            {
                slots.Cookie = _resources.TextureSlot(cookie, cube ? MaterialPropertyType.TextureCube : MaterialPropertyType.Texture2D, "white");
                slots.Flags |= 32;
            }
            else
            {
                _report(new RendererError("lighting.cookie.type", $"A {(point ? "point" : "spot")} light's cookie must be a {(point ? "cube" : "2D")} texture, it is left off.", RendererErrorSeverity.Warning, cookie));
            }
        }
        if (light.IesProfile is { IsDisposed: false } ies && (point || spot))
        {
            slots.Ies = _resources.TextureSlot(ies, MaterialPropertyType.Texture2D, "white");
            slots.Flags |= 64;
        }
        slots.Sampler = _resources.SamplerSlot(light.CookieSampler);
        return slots;
    }

    /// <summary>Resolves the slots of lights whose cookie or profile was freed, every one when freed is null. Host thread, at extraction.</summary>
    public void RepackLights(HashSet<Texture>? freed)
    {
        bool Freed(Texture? texture) => texture != null && (freed == null || freed.Contains(texture));

        for (int i = 0; i < _lights.Length; i++)
        {
            LightDesc light = _lights[i];
            if (!_lightAlive[i] || !(Freed(light.Cookie) || Freed(light.IesProfile)))
                continue;
            CountFeatures(light, _lightSlots[i], -1);
            _lightSlots[i] = Slots(light);
            CountFeatures(light, _lightSlots[i], 1);
            if (light.Type != LightType.Directional)
                Lights.Repack(i);
        }
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
        LiveDecals += (alive ? 1 : 0) - (wasAlive ? 1 : 0);
        _decals[index] = desc;
        _decalAlive[index] = alive;
        _decalSequence[index] = sequence;
        if (alive)
            _decalLooks[index] = Look(desc, _decalSequence[index]);

        if (alive)
            Decals.Set(index, _decalLooks[index].Center, _decalLooks[index].Radius, desc.Static);
        else
            Decals.Remove(index);
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

    internal void SetSkyExposure(float exposure)
    {
        if (exposure == _skyExposure)
            return;
        _skyExposure = exposure;
        _worldRecordDirty = true;
    }

    internal void SetProbes(bool hasProbes)
    {
        _hasProbes = hasProbes;
        _worldRecordDirty = true;
    }

    /// <summary>Repacks the decals using textures that were disposed and freed. Host thread, at extraction.</summary>
    public void RepackDecals(HashSet<Texture>? freed)
    {
        bool Freed(Texture? texture) => texture != null && (freed == null || freed.Contains(texture));

        for (int i = 0; i < _decals.Length; i++)
        {
            DecalDesc decal = _decals[i];
            if (!_decalAlive[i] || !(Freed(decal.Albedo) || Freed(decal.Normal) || Freed(decal.Mask) || Freed(decal.Extra0) || Freed(decal.Extra1)))
                continue;
            _decalLooks[i] = Look(_decals[i], _decalSequence[i]);
            Decals.Repack(i);
        }
    }

    /// <summary>Rebuilds the static regions that changed and rewrites the records and the world record. Render thread.</summary>
    public void Update()
    {
        if (_lightMembership)
            RebuildLightLists();
        Lights.Update();
        Decals.Update();
        if (_worldRecordDirty)
            WriteWorldRecord();
    }

    public bool Flush(Uploader uploader)
    {
        bool resized = false;
        foreach (GpuTable table in Tables)
            resized |= table.Flush(uploader);
        return resized;
    }

    private void RebuildLightLists()
    {
        _lightMembership = false;
        _directionalLights.Clear();
        for (int i = 0; i < _lights.Length; i++)
        {
            if (_lightAlive[i] && _lights[i].Type == LightType.Directional)
                _directionalLights.Add(i);
        }
    }

    // Directions unit length, a zero one pointing down +Z, up square to it, sizes and the inner cone in range
    private static LightDesc Normalized(in LightDesc light)
    {
        LightDesc result = light;
        Float3 d = light.Direction;
        float length = MathF.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
        Float3 forward = length > 1e-12f ? d / length : new Float3(0, 0, 1);
        result.Direction = forward;
        Float3 up = light.Up - forward * (light.Up.X * forward.X + light.Up.Y * forward.Y + light.Up.Z * forward.Z);
        float upLength = MathF.Sqrt(up.X * up.X + up.Y * up.Y + up.Z * up.Z);
        if (upLength < 1e-6f)
        {
            Float3 other = MathF.Abs(forward.Y) < 0.99f ? new Float3(0, 1, 0) : new Float3(1, 0, 0);
            up = other - forward * (other.X * forward.X + other.Y * forward.Y + other.Z * forward.Z);
            upLength = MathF.Sqrt(up.X * up.X + up.Y * up.Y + up.Z * up.Z);
        }
        result.Up = up / upLength;
        result.InnerAngle = MathF.Min(light.InnerAngle, light.OuterAngle);
        result.SourceRadius = MathF.Max(light.SourceRadius, 0);
        result.Size = new Float2(MathF.Max(light.Size.X, 0), MathF.Max(light.Size.Y, 0));
        result.AngularRadius = Math.Clamp(light.AngularRadius, 0, 45);
        return result;
    }

    //   +0 position in tree space, range
    //   +1 colour, intensity
    //   +2 direction it shines or faces, type in bits 0 to 3, shadow 4, cookie 5, profile 6 as uint bits
    //   +3 up, rect width, disk radius or tube length
    //   +4 rect height, source radius, diffuse scale, specular scale
    //   +5 cos of the outer and inner spot angles, shadow depth and normal bias
    //   +6 shadow strength, shadow filter, shadow slot as int bits, layer mask as uint bits
    //   +7 cookie, profile and sampler slots
    //   +8 host data
    private LightRecord Pack(int index, Double3 origin)
    {
        LightDesc light = _lights[index];
        LightSlots slots = _lightSlots[index];
        Double3 p = light.Position - origin;
        uint type = light.Type switch
        {
            LightType.Spot => 2u,
            LightType.Rect => 3u,
            LightType.Disk => 4u,
            LightType.Tube => 5u,
            _ => 1u,
        };
        bool spot = light.Type == LightType.Spot;
        bool shadowed = light.Shadow.Enabled && (spot || light.Type == LightType.Point);
        uint flags = type | (shadowed ? 16u : 0) | slots.Flags;
        float outer = MathF.Cos(light.OuterAngle * MathF.PI / 180f);
        float intensity = spot && light.NormalizeSpotCone ? light.Intensity * 2f / MathF.Max(1f - outer, 1e-4f) : light.Intensity;
        return new LightRecord
        {
            PositionRange = new Float4((float)p.X, (float)p.Y, (float)p.Z, light.Range),
            ColorIntensity = new Float4(light.Color.X, light.Color.Y, light.Color.Z, intensity),
            DirectionType = new Float4(light.Direction.X, light.Direction.Y, light.Direction.Z, BitConverter.UInt32BitsToSingle(flags)),
            UpSize = new Float4(light.Up.X, light.Up.Y, light.Up.Z, light.Size.X),
            Shape = new Float4(light.Size.Y, light.SourceRadius, 0, 0),
            SpotShadow = new Float4(spot ? outer : -1f, spot ? MathF.Max(MathF.Cos(light.InnerAngle * MathF.PI / 180f), outer + 1e-4f) : 1f, light.Shadow.DepthBias, light.Shadow.NormalBias),
            Shadow = new Float4(light.Shadow.Strength, (float)light.Shadow.Filter, BitConverter.Int32BitsToSingle(shadowed ? index : -1), BitConverter.UInt32BitsToSingle(light.LayerMask)),
            Cookie = slots.Cookie,
            Ies = slots.Ies,
            Sampler = slots.Sampler,
            HostData = light.HostData,
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
            Extra0 = _resources.TextureSlot(decal.Extra0, MaterialPropertyType.Texture2D, "white"),
            Extra1 = _resources.TextureSlot(decal.Extra1, MaterialPropertyType.Texture2D, "white"),
            Params = new Float4(decal.NormalStrength, decal.Priority, decal.AngleFade, BitConverter.UInt32BitsToSingle(decal.LayerMask)),
            Axis = new Float4(axis.X, axis.Y, axis.Z, BitConverter.UInt32BitsToSingle(flags)),
            HostData = decal.HostData,
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
            Extra0 = look.Extra0,
            Extra1 = look.Extra1,
            HostData = look.HostData,
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

        SkyInputs = HashCode.Combine(sun >= 0 ? _lights[sun].Direction : default, sun >= 0 ? _lights[sun].Color * _lights[sun].Intensity : default, env.SkyColor, env.CaptureEV100);

        uint count = 0;
        _shaderOrder.Clear();
        if (sun >= 0)
        {
            Directionals.Set(count++, Directional(_lights[sun]));
            _shaderOrder.Add(sun);
        }
        foreach (int index in _directionalOrder)
        {
            if (index == sun)
                continue;
            Directionals.Set(count++, Directional(_lights[index]));
            _shaderOrder.Add(index);
        }
        record.DirectionalCount = count;
        record.HasProbes = _hasProbes ? 1u : 0;
        record.ProbeSlot = _probeSlot;
        record.ProbeSampler = _probeSampler;
        record.ProbeMips = _probeMips;
        record.ProbesReady = _probeMips > 0 ? 1u : 0;
        record.SkyScale = new Float4(1f / _skyExposure, 0, 0, 0);

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
    private static DirectionalRecord Directional(in LightDesc light)
    {
        float radius = light.AngularRadius * MathF.PI / 180f;
        return new DirectionalRecord
        {
            DirectionShadow = new Float4(-light.Direction.X, -light.Direction.Y, -light.Direction.Z, light.Shadow.Enabled ? 1 : 0),
            Color = new Float4(light.Color.X * light.Intensity, light.Color.Y * light.Intensity, light.Color.Z * light.Intensity, BitConverter.UInt32BitsToSingle(light.LayerMask)),
            Params = new Float4(MathF.Sin(radius), MathF.Cos(radius), 0, 0),
            HostData = light.HostData,
            Shadow = new Float4(light.Shadow.DepthBias, light.Shadow.NormalBias, light.Shadow.Strength, (float)light.Shadow.Filter),
        };
    }
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
    private readonly Action<RendererError> _report;
    private Region?[] _itemRegion = new Region?[16];
    private int[] _itemSlot = new int[16];
    private Double3[] _center = new Double3[16];
    private float[] _radius = new float[16];
    private uint[] _recordIndex = new uint[16];
    private Vector4[] _spheres = new Vector4[64];
    private LightBounds[] _boxes = new LightBounds[16];
    private RegionEntry[] _entries = new RegionEntry[16];

    public StaticRegions(TableHost tables, Func<int, Double3, TRecord> pack, Action<RendererError> report)
    {
        _pack = pack;
        _report = report;
        Records = tables.Of<TRecord>("Records", 64);
        Nodes = tables.Of<LightTreeNode>("Nodes", 16);
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
        if ((builder.Depth - 1) * 3 > LightTreeBuilder.ShaderStack)
            _report(new RendererError("lighting.regions.visible", $"A view sees {_visible.Count} static light or decal regions, so deep a tree that overlapping regions may be skipped."));

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
            _report(new RendererError("lighting.regions.depth", $"A static light or decal region is {_builder.Depth} levels deep, more than the shader's traversal stack holds, so some may be skipped."));
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
        _report(new RendererError("lighting.regions.full", "The static light or decal tables are full, a region is not drawn."));
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

/// <summary>
/// One kind of item found by where it is, as lights, decals, reflection probes and host sets are. Static items live in
/// regions with a tree each, rebuilt only when one changes; moving ones in a tree per view around its camera, every
/// frame. Shaders reach a set's buffers by its slot in the view's bindless arrays.
/// </summary>
internal abstract class SpatialIndex(int slot)
{
    public const int MaxSets = 16;
    public const int LightsSet = 0, DecalsSet = 1, ProbesSet = 2, FirstHostSet = 3;

    public int Slot { get; } = slot;
    public abstract GpuTable StaticRecords { get; }
    public abstract GpuTable StaticNodes { get; }

    /// <summary>Rebuilds the static regions that changed and rewrites repacked records. Render thread.</summary>
    public abstract void Update();

    /// <summary>Builds a view's trees over the regions and moving items it sees, around its camera. Returns their roots. Render thread.</summary>
    public abstract SpatialRoots BuildView(in CullFrustum frustum, SpatialView view, Uploader uploader);
}

/// <summary>A set's roots in a view, matching an entry of Graphite_SpatialRoots: its regions tree and its moving tree.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SpatialRoots
{
    public uint Regions, Moving;
    private uint _pad0, _pad1;

    public static readonly SpatialRoots None = new() { Regions = LightTreeBuilder.Invalid, Moving = LightTreeBuilder.Invalid };
}

/// <summary>The buffers a view builds a set's trees into each frame.</summary>
internal sealed class SpatialView(TableHost tables)
{
    public readonly GpuArray Records = tables.Array("Records");
    public readonly GpuArray Nodes = tables.Array("Nodes");
    public readonly GpuArray RegionEntries = tables.Array("RegionEntries");
    public readonly GpuArray RegionNodes = tables.Array("RegionNodes");
    public readonly LightTreeBuilder Tree = new();
    public readonly LightTreeBuilder RegionTree = new();

    public void Release()
    {
        Records.Release();
        Nodes.Release();
        RegionEntries.Release();
        RegionNodes.Release();
    }

    public void AddBuffers(BufferSet buffers)
    {
        buffers.Add(Records.Persistent);
        buffers.Add(Nodes.Persistent);
        buffers.Add(RegionEntries.Persistent);
        buffers.Add(RegionNodes.Persistent);
    }
}

/// <summary>A spatial set of one record type, packed for a tree around an origin by the pack function.</summary>
internal sealed class SpatialIndex<TRecord> : SpatialIndex where TRecord : unmanaged
{
    private readonly StaticRegions<TRecord> _static;
    private readonly Func<int, Double3, TRecord> _pack;
    private readonly Action<RendererError> _report;
    private readonly List<int> _moving = new();
    private readonly List<int> _culled = new();
    private int[] _movingSlot = [];
    private Double3[] _center = [];
    private float[] _radius = [];
    private TRecord[] _scratch = new TRecord[64];
    private Vector4[] _spheres = new Vector4[64];

    public SpatialIndex(TableHost tables, int slot, Func<int, Double3, TRecord> pack, Action<RendererError> report) : base(slot)
    {
        _pack = pack;
        _report = report;
        _static = new StaticRegions<TRecord>(tables, pack, report);
    }

    public override GpuTable StaticRecords => _static.Records;
    public override GpuTable StaticNodes => _static.Nodes;

    /// <summary>Adds or moves an item, static ones into their region, moving ones into each view's tree.</summary>
    public void Set(int item, Double3 center, float radius, bool isStatic)
    {
        Grow(item);
        _center[item] = center;
        _radius[item] = radius;
        if (isStatic)
        {
            LeaveMoving(item);
            _static.Set(item, center, radius);
        }
        else
        {
            _static.Remove(item);
            if (_movingSlot[item] < 0)
            {
                _movingSlot[item] = _moving.Count;
                _moving.Add(item);
            }
        }
    }

    public void Remove(int item)
    {
        _static.Remove(item);
        if (item < _movingSlot.Length)
            LeaveMoving(item);
    }

    /// <summary>Rewrites a static item's record without moving it. Moving items are packed every frame anyway.</summary>
    public void Repack(int item) => _static.Repack(item);

    public override void Update() => _static.Update();

    public override SpatialRoots BuildView(in CullFrustum frustum, SpatialView view, Uploader uploader)
    {
        SpatialRoots roots = SpatialRoots.None;
        roots.Regions = _static.BuildView(frustum, view.RegionTree, uploader, view.RegionEntries, view.RegionNodes);
        roots.Moving = BuildMoving(frustum, view, uploader);
        return roots;
    }

    private uint BuildMoving(in CullFrustum frustum, SpatialView view, Uploader uploader)
    {
        _culled.Clear();
        foreach (int item in _moving)
        {
            if (frustum.Sphere(_center[item], _radius[item]))
                _culled.Add(item);
        }
        if (_culled.Count == 0)
            return LightTreeBuilder.Invalid;

        if (_spheres.Length < _culled.Count)
            _spheres = new Vector4[_culled.Count * 2];
        for (int i = 0; i < _culled.Count; i++)
        {
            Float3 p = frustum.Relative(_center[_culled[i]]);
            _spheres[i] = new Vector4(p.X, p.Y, p.Z, _radius[_culled[i]]);
        }

        LightTreeBuilder builder = view.Tree;
        builder.Build(_spheres.AsSpan(0, _culled.Count), Math.Clamp(_culled.Count / 1024, 4, 128), morton: true);
        if ((builder.Depth - 1) * 3 > LightTreeBuilder.ShaderStack)
            _report(new RendererError("lighting.tree.depth", $"A tree of moving items is {builder.Depth} levels deep, more than the shader's traversal stack holds, so some may be skipped."));
        if (_scratch.Length < _culled.Count)
            _scratch = new TRecord[_culled.Count * 2];
        ReadOnlySpan<int> order = builder.Order;
        for (int i = 0; i < _culled.Count; i++)
            _scratch[i] = _pack(_culled[order[i]], frustum.Camera);
        view.Records.Write<TRecord>(uploader, _scratch.AsSpan(0, _culled.Count));
        view.Nodes.Write(uploader, builder.Nodes);
        return builder.NodeCount > 0 ? 0u : LightTreeBuilder.Invalid;
    }

    private void LeaveMoving(int item)
    {
        int slot = _movingSlot[item];
        if (slot < 0)
            return;
        int last = _moving[^1];
        _moving[slot] = last;
        _movingSlot[last] = slot;
        _moving.RemoveAt(_moving.Count - 1);
        _movingSlot[item] = -1;
    }

    private void Grow(int item)
    {
        if (item < _movingSlot.Length)
            return;
        int old = _movingSlot.Length;
        int size = Math.Max(item + 1, Math.Max(16, old * 2));
        Array.Resize(ref _movingSlot, size);
        Array.Fill(_movingSlot, -1, old, size - old);
        Array.Resize(ref _center, size);
        Array.Resize(ref _radius, size);
    }
}

/// <summary>The slots host spatial sets get by name, shared by every world of a renderer. Host thread.</summary>
internal sealed class SpatialSlots
{
    private readonly Dictionary<string, (int Slot, Type Type, int Worlds)> _slots = new(StringComparer.Ordinal);

    /// <summary>The slot for a name, from the renderer's registry when the world has one, else the world's own.</summary>
    public int Slot(SpatialSlots? renderer, string name, Type type)
    {
        SpatialSlots registry = renderer ?? this;
        lock (registry._slots)
        {
            if (registry._slots.TryGetValue(name, out (int Slot, Type Type, int Worlds) known))
            {
                if (known.Type != type)
                    throw new InvalidOperationException($"The spatial set {name} holds {known.Type.Name} records, not {type.Name}.");
                registry._slots[name] = known with { Worlds = known.Worlds + 1 };
                return known.Slot;
            }
            for (int slot = SpatialIndex.FirstHostSet; slot < SpatialIndex.MaxSets; slot++)
            {
                if (!registry.Used(slot))
                {
                    registry._slots[name] = (slot, type, 1);
                    return slot;
                }
            }
            throw new InvalidOperationException($"A renderer has room for {SpatialIndex.MaxSets - SpatialIndex.FirstHostSet} spatial set names at once.");
        }
    }

    /// <summary>A world's set of the name is gone, freeing its slot when no other world has one.</summary>
    public void Release(SpatialSlots? renderer, string name)
    {
        SpatialSlots registry = renderer ?? this;
        lock (registry._slots)
        {
            if (!registry._slots.TryGetValue(name, out (int Slot, Type Type, int Worlds) known))
                return;
            if (known.Worlds > 1)
                registry._slots[name] = known with { Worlds = known.Worlds - 1 };
            else
                registry._slots.Remove(name);
        }
    }

    private bool Used(int slot)
    {
        foreach ((int Slot, Type, int) entry in _slots.Values)
        {
            if (entry.Slot == slot)
                return true;
        }
        return false;
    }
}

/// <summary>A handle to an item of a host's spatial set.</summary>
public readonly record struct SpatialHandle(int Index, int Generation)
{
    public bool IsValid => Generation > 0;
}

/// <summary>
/// Items of a host's kind found by where they are, as lights are: fog volumes, post volumes, water bodies. Shaders walk
/// the ones holding a point with Graphite_BeginItems(Index, Stride, worldPos) and Graphite_NextItem, reading each record's
/// data with Graphite_SpatialVec4(array, index * Stride + 1 + i). T must be a whole number of 16 byte words, laid out as
/// the shader reads it. Static items join regions rebuilt only when one changes, moving ones a tree per view. Host thread.
/// </summary>
public sealed class SpatialSet<T> where T : unmanaged
{
    private struct Item
    {
        public Double3 Center;
        public float Radius;
        public T Record;
        public bool Static;
    }

    private readonly HandleStore<Item> _items = new();
    private Item[] _render = new Item[16];

    internal SpatialSet(TableHost tables, int slot, Action<RendererError> report)
    {
        Index = slot;
        Render = new SpatialIndex<SpatialRecord<T>>(tables, slot, Pack, report);
    }

    /// <summary>The set's index, which shaders query it by.</summary>
    public int Index { get; }

    /// <summary>16 byte words per record as shaders read them, the centre and radius first.</summary>
    public int Stride => Unsafe.SizeOf<SpatialRecord<T>>() / 16;

    internal SpatialIndex<SpatialRecord<T>> Render { get; }

    public SpatialHandle Add(Double3 center, float radius, in T record, bool isStatic = false)
    {
        Check(center, radius);
        (int index, int generation) = _items.Add(new Item { Center = center, Radius = radius, Record = record, Static = isStatic });
        return new SpatialHandle(index, generation);
    }

    public void Set(SpatialHandle handle, Double3 center, float radius, in T record, bool isStatic = false)
    {
        Check(center, radius);
        _items.Set(handle.Index, handle.Generation, new Item { Center = center, Radius = radius, Record = record, Static = isStatic });
    }

    private static void Check(Double3 center, float radius)
    {
        if (!double.IsFinite(center.X) || !double.IsFinite(center.Y) || !double.IsFinite(center.Z))
            throw new ArgumentOutOfRangeException(nameof(center), "An item's centre must be finite.");
        if (!float.IsFinite(radius) || radius < 0)
            throw new ArgumentOutOfRangeException(nameof(radius), "An item's radius must be finite and not negative.");
    }

    public void Remove(SpatialHandle handle) => _items.Remove(handle.Index, handle.Generation);

    public bool IsAlive(SpatialHandle handle) => _items.IsAlive(handle.Index, handle.Generation);

    // At SubmitFrame, render thread idle
    internal void Extract() => _items.Extract(Receive);

    private void Receive(int index, int generation, uint sequence, bool alive, in Item item)
    {
        if (index >= _render.Length)
            Array.Resize(ref _render, Math.Max(index + 1, _render.Length * 2));
        _render[index] = item;
        if (alive)
            Render.Set(index, item.Center, MathF.Max(item.Radius, 1e-4f), item.Static);
        else
            Render.Remove(index);
    }

    private SpatialRecord<T> Pack(int index, Double3 origin)
    {
        Item item = _render[index];
        Double3 c = item.Center - origin;
        return new SpatialRecord<T> { CenterRadius = new Float4((float)c.X, (float)c.Y, (float)c.Z, MathF.Max(item.Radius, 1e-4f)), Record = item.Record };
    }
}

/// <summary>A host set's record as shaders read it: its centre in its tree's space and radius, then the host's data.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SpatialRecord<T> where T : unmanaged
{
    public Float4 CenterRadius;
    public T Record;
}

/// <summary>Texture and sampler slots for a world without a renderer, as in tests: always slot 0.</summary>
internal sealed class NoMaterialResources : IMaterialResources
{
    public static readonly NoMaterialResources Instance = new();

    public uint TextureSlot(Texture? texture, MaterialPropertyType type, string defaultName) => 0;

    public uint SamplerSlot(SamplerDescription? sampler) => 0;
}
