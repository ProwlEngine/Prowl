// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite.Rendering;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using GLightType = Prowl.Graphite.Rendering.LightType;
using GShadowFilter = Prowl.Graphite.Rendering.ShadowFilter;

namespace Prowl.Runtime;

/// <summary>The shape a light shines from.</summary>
public enum LightType
{
    /// <summary>Parallel rays from infinitely far away, like the sun.</summary>
    Directional = 0,

    /// <summary>Every direction from a point, or a sphere of <see cref="Light.SourceRadius"/>.</summary>
    Point = 1,

    /// <summary>A cone along Forward.</summary>
    Spot = 2,

    /// <summary>A one sided rectangle facing Forward, <see cref="Light.Width"/> along Right and <see cref="Light.Height"/> along Up.</summary>
    Rect = 3,

    /// <summary>A one sided disk facing Forward, <see cref="Light.Width"/> across.</summary>
    Disk = 4,

    /// <summary>A capsule along Forward, <see cref="Light.Length"/> long and <see cref="Light.SourceRadius"/> thick.</summary>
    Tube = 5,
}

/// <summary>How a light's shadow edges are filtered.</summary>
public enum ShadowFilter
{
    /// <summary>One comparison a pixel, hard aliased edges.</summary>
    Hard = 0,

    /// <summary>A smooth tent filter of even width.</summary>
    Tent = 1,

    /// <summary>Penumbrae that widen with the distance from the caster, sharp where objects touch.</summary>
    ContactHardening = 2,
}

/// <summary>The largest shadow map tile a light may take. Local lights size theirs from how big they are on screen up to this.</summary>
public enum ShadowResolution
{
    _256 = 256,
    _512 = 512,
    _1024 = 1024,
    _2048 = 2048,
    _4096 = 4096,
}

/// <summary>How many cascades a directional light's shadow is split into along the view.</summary>
public enum ShadowCascades
{
    One = 1,
    Two = 2,
    Four = 4,
}

/// <summary>How a light participates in lightmap baking.</summary>
public enum LightBakeMode
{
    /// <summary>Not baked. Lit entirely in realtime, never written into a lightmap or probe.</summary>
    Realtime = 0,

    /// <summary>Direct light and shadows stay realtime, the bounced light is baked into lightmaps and probes.</summary>
    Mixed = 1,

    /// <summary>Fully baked into lightmaps and probes and left out of the realtime lights.</summary>
    Baked = 2,
}

/// <summary>A light in the scene's world, kept up to date from its settings and transform. It shines along Forward.</summary>
[AddComponentMenu("Rendering/Light")]
[ComponentIcon("")] // Sun
public class Light : Component, IRenderSync
{
    public LightType Type = LightType.Point;

    /// <summary>Linear colour.</summary>
    public Color Color = Color.White;
    public float Intensity = 1.0f;

    /// <summary>Distance from the light at which it reaches zero.</summary>
    [ShowIf(nameof(IsLocal))]
    public float Range = 10.0f;

    /// <summary>Half angle of the cone from its axis where the light is still full, in degrees.</summary>
    [ShowIf(nameof(IsSpot)), Range(0f, 89.9f)]
    public float InnerAngle = 30.0f;

    /// <summary>Half angle of the cone from its axis past which the light is gone, in degrees.</summary>
    [ShowIf(nameof(IsSpot)), Range(0.1f, 89.9f)]
    public float OuterAngle = 45.0f;

    /// <summary>A rect's width or a disk's diameter.</summary>
    [ShowIf(nameof(HasWidth))]
    public float Width = 1.0f;

    /// <summary>A rect's height.</summary>
    [ShowIf(nameof(IsRect))]
    public float Height = 1.0f;

    /// <summary>A tube's length along Forward.</summary>
    [ShowIf(nameof(IsTube))]
    public float Length = 1.0f;

    /// <summary>The radius of a point or spot light's sphere, or of a tube, which softens highlights and contact hardening shadows.</summary>
    [ShowIf(nameof(HasSourceRadius))]
    public float SourceRadius = 0f;

    /// <summary>The angular radius of the light's disk in degrees, the sun's about 0.27, which softens contact hardening shadows.</summary>
    [ShowIf(nameof(IsDirectional))]
    public float AngularRadius = 0.27f;

    /// <summary>A texture the light shines through: a 2D texture across a spot's cone, a cubemap around a point.</summary>
    [ShowIf(nameof(HasProfile))]
    public Texture? Cookie;

    /// <summary>A photometric profile as a 2D texture: the angle from the axis across U, around it across V, intensity in R.</summary>
    [ShowIf(nameof(HasProfile))]
    public Texture2D? IesProfile;

    /// <summary>Which layers the light reaches.</summary>
    public LayerMask LayerMask = LayerMask.Everything;

    [ShowIf(nameof(CanCastShadows))]
    public bool CastShadows = true;

    [ShowIf(nameof(ShowsShadows)), Range(0f, 1f)]
    public float ShadowStrength = 1.0f;

    [ShowIf(nameof(ShowsShadows))]
    public ShadowResolution ShadowResolution = ShadowResolution._1024;

    [ShowIf(nameof(ShowsShadows))]
    public ShadowFilter ShadowFilter = ShadowFilter.Tent;

    /// <summary>How far receivers are pushed toward the light before the shadow test, in shadow map texels.</summary>
    [ShowIf(nameof(ShowsShadows))]
    public float DepthBias = 1.0f;

    /// <summary>How far receivers are pushed out along their surface normal before the shadow test, in shadow map texels.</summary>
    [ShowIf(nameof(ShowsShadows))]
    public float NormalBias = 1.0f;

    [ShowIf(nameof(ShowsCascades))]
    public ShadowCascades Cascades = ShadowCascades.Two;

    /// <summary>How far along the view shadows reach, in world units. They fade out over the last tenth.</summary>
    [ShowIf(nameof(ShowsCascades))]
    public float ShadowDistance = 50f;

    /// <summary>The fraction of each cascade blended into the next, 0 for a hard switch.</summary>
    [ShowIf(nameof(ShowsCascades)), Range(0f, 1f)]
    public float CascadeBlend = 0.1f;

    /// <summary>Baked lights live only in the lightmaps and probes, Mixed and Realtime ones light in realtime.</summary>
    public LightBakeMode BakeMode = LightBakeMode.Realtime;

    [System.NonSerialized, NotHeld] private LightHandle _handle;
    [System.NonSerialized, NotHeld] private LightDesc _last;
    [System.NonSerialized, NotHeld] private RenderWorld? _world;
    [System.NonSerialized, NotHeld] private Scene? _sunScene;

    private bool IsDirectional => Type == LightType.Directional;
    private bool IsLocal => Type != LightType.Directional;
    private bool IsSpot => Type == LightType.Spot;
    private bool IsRect => Type == LightType.Rect;
    private bool IsTube => Type == LightType.Tube;
    private bool HasWidth => Type is LightType.Rect or LightType.Disk;
    private bool HasSourceRadius => Type is LightType.Point or LightType.Spot or LightType.Tube;
    private bool HasProfile => Type is LightType.Point or LightType.Spot;
    private bool ShowsShadows => CanCastShadows && CastShadows;
    private bool ShowsCascades => ShowsShadows && IsDirectional;

    /// <summary>Whether this type of light can cast shadows. Area lights do not yet.</summary>
    public bool CanCastShadows => Type is LightType.Directional or LightType.Point or LightType.Spot;

    /// <summary>The light in the scene's world, invalid while it is not there.</summary>
    public LightHandle Handle => _handle;

    /// <summary>The light as Graphite describes it, from the settings and the transform.</summary>
    public LightDesc Describe()
    {
        Float3 position = Transform.Position;
        var desc = new LightDesc
        {
            Type = (GLightType)Type,
            Position = new Double3(position.X, position.Y, position.Z),
            Direction = Float3.Normalize(Transform.Forward),
            Up = Float3.Normalize(Transform.Up),
            Color = new Float3(Color.R, Color.G, Color.B),
            Intensity = Intensity,
            Range = Maths.Max(Range, 0.01f),
            LayerMask = LayerMask.Mask,
            Static = GameObject.IsStatic,
            HostData = FogData(),
            Shadow = new ShadowDesc
            {
                Enabled = CanCastShadows && CastShadows && ShadowStrength > 0,
                Resolution = (uint)ShadowResolution,
                DepthBias = DepthBias,
                NormalBias = NormalBias,
                Strength = ShadowStrength,
                Filter = (GShadowFilter)ShadowFilter,
            },
        };

        switch (Type)
        {
            case LightType.Directional:
                desc.Range = 0;
                desc.AngularRadius = AngularRadius;
                desc.Shadow.Cascades = (byte)Maths.Clamp((int)Cascades, 1, 4);
                desc.Shadow.Distance = Maths.Max(ShadowDistance, 0.01f);
                desc.Shadow.CascadeBlend = CascadeBlend;
                break;
            case LightType.Point:
                desc.SourceRadius = SourceRadius;
                break;
            case LightType.Spot:
                desc.SourceRadius = SourceRadius;
                desc.OuterAngle = Maths.Clamp(OuterAngle, 0.1f, 89.9f);
                desc.InnerAngle = Maths.Clamp(InnerAngle, 0f, desc.OuterAngle);
                break;
            case LightType.Rect:
                desc.Size = new Float2(Maths.Max(Width, 0f), Maths.Max(Height, 0f));
                break;
            case LightType.Disk:
                desc.Size = new Float2(Maths.Max(Width, 0f) * 0.5f, 0f);
                break;
            case LightType.Tube:
                desc.Size = new Float2(Maths.Max(Length, 0f), 0f);
                desc.SourceRadius = SourceRadius;
                break;
        }

        if (HasProfile)
        {
            Texture? cookie = Cookie;
            if (cookie.IsValid() && cookie.IsLoaded)
            {
                desc.Cookie = cookie.GpuIfLoaded;
                desc.CookieSampler = cookie.SamplerUnchecked;
            }
            Texture2D? ies = IesProfile;
            if (ies.IsValid() && ies.IsLoaded)
            {
                desc.IesProfile = ies.GpuIfLoaded;
                if (desc.CookieSampler == null) desc.CookieSampler = ies.SamplerUnchecked;
            }
        }
        return desc;
    }

    // What volumetric fog reads as the light's HostData: its scattering colour times intensity, then its scattering bias
    // with 4 added when it casts no fog shadows
    private Float4 FogData()
    {
        Color color = Color;
        float intensity = Intensity, bias = 0f;
        bool shadows = true;
        FogLight? fog = GetComponent<FogLight>();
        if (fog.IsValid() && fog.EnabledInHierarchy)
        {
            intensity *= fog.IntensityMultiplier;
            if (fog.UseOverrideColor) color = fog.OverrideColor;
            bias = Maths.Clamp(fog.ScatteringBias, -2f, 2f);
            shadows = fog.CastFogShadows;
        }
        return new Float4(color.R * intensity, color.G * intensity, color.B * intensity, bias + (shadows ? 0f : 4f));
    }

    bool IRenderSync.Sync(RenderWorld world)
    {
        if (_world != null && _world != world) Remove();
        if (BakeMode == LightBakeMode.Baked)
        {
            Remove();
            return false;
        }

        LightDesc desc = Describe();
        if (!_handle.IsValid || !world.IsAlive(_handle))
        {
            _handle = world.AddLight(desc);
            _world = world;
            _last = desc;
        }
        else if (!Same(desc, _last))
        {
            world.SetLight(_handle, desc);
            _last = desc;
        }

        if (IsDirectional && _sunScene.IsNotValid() && GameObject.Scene.IsValid())
        {
            _sunScene = GameObject.Scene;
            _sunScene.Rendering.AddDirectional(this);
        }
        else if (!IsDirectional) LeaveSuns();
        return GameObject.IsStatic && (!HasProfile || (Ready(Cookie) && Ready(IesProfile)));
    }

    // A texture the light was given is in the world, none or loaded
    private static bool Ready(Texture? texture) => texture.IsNotValid() || texture.IsLoaded;

    void IRenderSync.Detach(RenderWorld world) => Remove();

    private void Remove()
    {
        LeaveSuns();
        if (_world != null && _handle.IsValid && _world.IsAlive(_handle))
            _world.RemoveLight(_handle);
        _handle = default;
        _world = null;
    }

    private void LeaveSuns()
    {
        if (_sunScene.IsValid() && !_sunScene.IsDisposed) _sunScene.Rendering.RemoveDirectional(this);
        _sunScene = null;
    }

    private static bool Same(in LightDesc a, in LightDesc b)
        => a.Type == b.Type && a.Position.Equals(b.Position) && a.Direction == b.Direction && a.Up == b.Up && a.Color == b.Color
        && a.Intensity == b.Intensity && a.Range == b.Range && a.SourceRadius == b.SourceRadius && a.Size == b.Size
        && a.AngularRadius == b.AngularRadius && a.InnerAngle == b.InnerAngle && a.OuterAngle == b.OuterAngle
        && a.LayerMask == b.LayerMask && a.Static == b.Static && a.Cookie == b.Cookie && a.IesProfile == b.IesProfile
        && System.Nullable.Equals(a.CookieSampler, b.CookieSampler) && a.HostData == b.HostData
        && a.Shadow.Enabled == b.Shadow.Enabled && a.Shadow.Resolution == b.Shadow.Resolution && a.Shadow.DepthBias == b.Shadow.DepthBias
        && a.Shadow.NormalBias == b.Shadow.NormalBias && a.Shadow.Cascades == b.Shadow.Cascades && a.Shadow.Distance == b.Shadow.Distance
        && a.Shadow.CascadeBlend == b.Shadow.CascadeBlend && a.Shadow.Strength == b.Shadow.Strength && a.Shadow.Filter == b.Shadow.Filter;

    public override void DrawGizmos()
    {
        Texture2D? icon = Texture2D.LoadDefault(DefaultTexture.IconLight);
        if (icon.IsValid()) Debug.DrawIcon(icon, Transform.Position, 0.5f, Color.White);

        Float3 position = Transform.Position, forward = Transform.Forward, right = Transform.Right, up = Transform.Up;
        switch (Type)
        {
            case LightType.Directional:
                Debug.DrawArrow(position, forward, Color.Yellow);
                Debug.DrawWireCircle(position, forward, 0.5f, Color.Yellow);
                break;
            case LightType.Rect:
                Float3 x = right * (Width * 0.5f), y = up * (Height * 0.5f);
                Debug.DrawLine(position - x - y, position + x - y, Color.Yellow);
                Debug.DrawLine(position + x - y, position + x + y, Color.Yellow);
                Debug.DrawLine(position + x + y, position - x + y, Color.Yellow);
                Debug.DrawLine(position - x + y, position - x - y, Color.Yellow);
                Debug.DrawArrow(position, forward * 0.5f, Color.Yellow);
                break;
            case LightType.Disk:
                Debug.DrawWireCircle(position, forward, Width * 0.5f, Color.Yellow);
                Debug.DrawArrow(position, forward * 0.5f, Color.Yellow);
                break;
            case LightType.Tube:
                Float3 half = forward * (Length * 0.5f);
                Debug.DrawWireCapsule(position - half, position + half, Maths.Max(SourceRadius, 0.02f), Color.Yellow);
                break;
        }
    }

    public override void DrawGizmosSelected()
    {
        Float3 position = Transform.Position, forward = Transform.Forward;
        switch (Type)
        {
            case LightType.Directional:
                break;
            case LightType.Spot:
                Debug.DrawArrow(position, forward, Color.Yellow);
                Debug.DrawWireConeAngle(position, forward, Maths.Clamp(OuterAngle, 0.1f, 89.9f), Range, Color.Yellow);
                break;
            default:
                Debug.DrawWireSphere(position, Range, Color.Yellow);
                break;
        }
    }
}
