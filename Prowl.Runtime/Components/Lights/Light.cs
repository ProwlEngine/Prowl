// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Graphite.Rendering;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Tasks;
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
    // The 60 W bulb, the default preset of the default Point type, which a new light starts as
    private const float BulbLumens = 800f, BulbKelvin = 2700f;

    /// <summary>The shape the light shines from. Setting it keeps every other setting, it applies no preset.</summary>
    public LightType Type { get => type; set { type = value; Wake(); } }
    [SerializeField] private LightType type = LightType.Point;

    /// <summary>Linear colour. With <see cref="UseTemperature"/> on it tints the temperature's colour, like a filter in front of the light.</summary>
    public Color Color { get => color; set { color = value; Wake(); } }
    [SerializeField] private Color color = Color.White;

    /// <summary>Whether the light takes the colour of a black body at <see cref="Temperature"/>, times <see cref="Color"/>.</summary>
    public bool UseTemperature { get => useTemperature; set { useTemperature = value; Wake(); } }
    [SerializeField] private bool useTemperature = true;

    /// <summary>The colour temperature in Kelvin: a candle about 1900, a bulb 2700, daylight 5600, overcast sky 6500.</summary>
    public float Temperature { get => temperature; set { temperature = value; Wake(); } }
    [SerializeField, ShowIf(nameof(UseTemperature)), Range(1000f, 12000f)] private float temperature = BulbKelvin;

    /// <summary>
    /// Illuminance in lux for a directional light, a clear day's sun about 100000. Luminous intensity in candela for a
    /// point or spot, a 60 W bulb about 70. Luminance in nits for a rect, disk or tube.
    /// </summary>
    public float Intensity { get => intensity; set { intensity = value; Wake(); } }
    [SerializeField] private float intensity = BulbLumens / (4f * Maths.PI);

    /// <summary>Whether <see cref="Range"/> is set by hand instead of worked out by <see cref="AutomaticRange"/>.</summary>
    public bool OverrideRange { get => overrideRange; set { overrideRange = value; Wake(); } }
    [SerializeField, ShowIf(nameof(IsLocal))] private bool overrideRange;

    /// <summary>
    /// Distance from the light at which it reaches zero: the one set by hand while <see cref="OverrideRange"/> is on,
    /// else <see cref="AutomaticRange"/>. Setting it turns <see cref="OverrideRange"/> on.
    /// </summary>
    public float Range
    {
        get => overrideRange ? range : AutomaticRange;
        set { range = value; overrideRange = true; Wake(); }
    }
    [SerializeField, ShowIf(nameof(ShowsRangeOverride))] private float range = 10.0f;

    /// <summary>The whole angle of a spot's cone, in degrees.</summary>
    public float SpotAngle { get => spotAngle; set { spotAngle = value; Wake(); } }
    [SerializeField, ShowIf(nameof(IsSpot)), Range(1f, 179f)] private float spotAngle = 90.0f;

    /// <summary>How much of a spot's cone fades out toward its edge, 0 for a hard edge and 1 for a fade all the way from the axis.</summary>
    public float SpotSoftness { get => spotSoftness; set { spotSoftness = value; Wake(); } }
    [SerializeField, ShowIf(nameof(IsSpot)), Range(0f, 1f)] private float spotSoftness = 1f / 3f;

    /// <summary>A rect's width or a disk's diameter.</summary>
    public float Width { get => width; set { width = value; Wake(); } }
    [SerializeField, ShowIf(nameof(HasWidth))] private float width = 1.0f;

    /// <summary>A rect's height.</summary>
    public float Height { get => height; set { height = value; Wake(); } }
    [SerializeField, ShowIf(nameof(IsRect))] private float height = 1.0f;

    /// <summary>A tube's length along Forward.</summary>
    public float Length { get => length; set { length = value; Wake(); } }
    [SerializeField, ShowIf(nameof(IsTube))] private float length = 1.0f;

    /// <summary>The radius of a point or spot light's sphere, or of a tube, which softens highlights and contact hardening shadows.</summary>
    public float SourceRadius { get => sourceRadius; set { sourceRadius = value; Wake(); } }
    [SerializeField, ShowIf(nameof(HasSourceRadius))] private float sourceRadius = 0f;

    /// <summary>The angular radius of the light's disk in degrees, the sun's about 0.27, which softens contact hardening shadows.</summary>
    public float AngularRadius { get => angularRadius; set { angularRadius = value; Wake(); } }
    [SerializeField, ShowIf(nameof(IsDirectional))] private float angularRadius = 0.27f;

    /// <summary>A texture the light shines through: a 2D texture across a spot's cone, a cubemap around a point.</summary>
    public Texture? Cookie { get => cookie; set { cookie = value; Wake(); } }
    [SerializeField, ShowIf(nameof(HasProfile))] private Texture? cookie;

    /// <summary>A photometric profile as a 2D texture: the angle from the axis across U, around it across V, intensity in R.</summary>
    public Texture2D? IesProfile { get => iesProfile; set { iesProfile = value; Wake(); } }
    [SerializeField, ShowIf(nameof(HasProfile))] private Texture2D? iesProfile;

    /// <summary>Which layers the light reaches.</summary>
    public LayerMask LayerMask { get => layerMask; set { layerMask = value; Wake(); } }
    [SerializeField] private LayerMask layerMask = LayerMask.Everything;

    public bool CastShadows { get => castShadows; set { castShadows = value; Wake(); } }
    [SerializeField, ShowIf(nameof(CanCastShadows))] private bool castShadows = true;

    public float ShadowStrength { get => shadowStrength; set { shadowStrength = value; Wake(); } }
    [SerializeField, ShowIf(nameof(ShowsShadows)), Range(0f, 1f)] private float shadowStrength = 1.0f;

    /// <summary>The light's largest shadow tile, capped to the quality level's.</summary>
    public ShadowResolution ShadowResolution { get => shadowResolution; set { shadowResolution = value; Wake(); } }
    [SerializeField, ShowIf(nameof(ShowsShadows))] private ShadowResolution shadowResolution = ShadowResolution._1024;

    public ShadowFilter ShadowFilter { get => shadowFilter; set { shadowFilter = value; Wake(); } }
    [SerializeField, ShowIf(nameof(ShowsShadows))] private ShadowFilter shadowFilter = ShadowFilter.Tent;

    /// <summary>How far receivers are pushed toward the light before the shadow test, in shadow map texels.</summary>
    public float DepthBias { get => depthBias; set { depthBias = value; Wake(); } }
    [SerializeField, ShowIf(nameof(ShowsShadows))] private float depthBias = 1.0f;

    /// <summary>How far receivers are pushed out along their surface normal before the shadow test, in shadow map texels.</summary>
    public float NormalBias { get => normalBias; set { normalBias = value; Wake(); } }
    [SerializeField, ShowIf(nameof(ShowsShadows))] private float normalBias = 1.0f;

    /// <summary>The fraction of each cascade blended into the next, 0 for a hard switch. The cascades and how far they reach are the quality level's.</summary>
    public float CascadeBlend { get => cascadeBlend; set { cascadeBlend = value; Wake(); } }
    [SerializeField, ShowIf(nameof(ShowsCascades)), Range(0f, 1f)] private float cascadeBlend = 0.1f;

    /// <summary>Baked lights live only in the lightmaps and probes, Mixed and Realtime ones light in realtime.</summary>
    public LightBakeMode BakeMode { get => bakeMode; set { bakeMode = value; Wake(); } }
    [SerializeField] private LightBakeMode bakeMode = LightBakeMode.Realtime;

    [System.NonSerialized, NotHeld] private LightHandle _handle;
    [System.NonSerialized, NotHeld] private LightDesc _last;
    [System.NonSerialized, NotHeld] private uint _poseVersion;
    [System.NonSerialized, NotHeld] private (Double3 Position, Float3 Forward, Float3 Up) _pose;

    // The FogLight beside this light, which registers itself when it joins the scene
    [System.NonSerialized, NotHeld] internal FogLight? Fog;
    [System.NonSerialized, NotHeld] private RenderWorld? _world;
    [System.NonSerialized, NotHeld] private Scene? _sunScene;

    private bool IsDirectional => Type == LightType.Directional;
    private bool IsLocal => Type != LightType.Directional;
    private bool ShowsRangeOverride => IsLocal && OverrideRange;
    private bool IsSpot => Type == LightType.Spot;
    private bool IsRect => Type == LightType.Rect;
    private bool IsTube => Type == LightType.Tube;
    private bool HasWidth => Type is LightType.Rect or LightType.Disk;
    private bool HasSourceRadius => Type is LightType.Point or LightType.Spot or LightType.Tube;
    private bool HasProfile => Type is LightType.Point or LightType.Spot;
    private bool ShowsShadows => CanCastShadows && CastShadows;
    private bool ShowsCascades => ShowsShadows && IsDirectional;

    private static readonly LightPreset[] s_directionalPresets =
    [
        new("Midday sun", LightUnit.Lux, 100000f, 5600f),
        new("Low sun", LightUnit.Lux, 20000f, 3500f),
        new("Sunset", LightUnit.Lux, 2000f, 2500f),
        new("Full moon", LightUnit.Lux, 0.25f, 4100f),
    ];

    private static readonly LightPreset[] s_pointPresets =
    [
        new("Candle", LightUnit.Lumen, 12f, 1900f),
        new("40 W bulb", LightUnit.Lumen, 450f, BulbKelvin),
        new("60 W bulb", LightUnit.Lumen, BulbLumens, BulbKelvin),
        new("100 W bulb", LightUnit.Lumen, 1600f, BulbKelvin),
        new("Street lamp", LightUnit.Lumen, 15000f, 2000f),
    ];

    private static readonly LightPreset[] s_spotPresets =
    [
        new("Flashlight", LightUnit.Lumen, 300f, 6000f, SpotAngle: 20f),
        new("Downlight", LightUnit.Lumen, 800f, 3000f, SpotAngle: 60f),
        new("Car headlight", LightUnit.Lumen, 1500f, 4300f, SpotAngle: 40f),
        new("Stage spot", LightUnit.Lumen, 10000f, 3200f, SpotAngle: 25f),
    ];

    private static readonly LightPreset[] s_rectPresets =
    [
        new("Monitor", LightUnit.Nit, 250f, 6500f, Width: 0.6f, Height: 0.34f),
        new("Ceiling panel", LightUnit.Lumen, 3600f, 4000f, Width: 0.6f, Height: 0.6f),
        new("Softbox", LightUnit.Nit, 5000f, 5600f, Width: 1f, Height: 1f),
    ];

    private static readonly LightPreset[] s_diskPresets =
    [
        new("Ceiling disk", LightUnit.Lumen, 1000f, 3000f, Width: 0.3f),
        new("Round panel", LightUnit.Lumen, 2000f, 4000f, Width: 0.45f),
    ];

    private static readonly LightPreset[] s_tubePresets =
    [
        new("Fluorescent tube", LightUnit.Lumen, 2800f, 4000f, Length: 1.2f, Radius: 0.0125f),
        new("LED strip, 1 m", LightUnit.Lumen, 1000f, 3000f, Length: 1f, Radius: 0.005f),
    ];

    /// <summary>The real lights a type of light can start from.</summary>
    public static IReadOnlyList<LightPreset> Presets(LightType type) => type switch
    {
        LightType.Directional => s_directionalPresets,
        LightType.Point => s_pointPresets,
        LightType.Spot => s_spotPresets,
        LightType.Rect => s_rectPresets,
        LightType.Disk => s_diskPresets,
        _ => s_tubePresets,
    };

    /// <summary>The preset a new light of a type starts as: the midday sun, a 60 W bulb, a downlight, a ceiling panel, a ceiling disk or a fluorescent tube.</summary>
    public static LightPreset DefaultPreset(LightType type) => Presets(type)[type switch
    {
        LightType.Point => 2,
        LightType.Spot or LightType.Rect => 1,
        _ => 0,
    }];

    /// <summary>The units a type of light can be given in, the one it keeps last: lux and EV100, lumens and candela, or lumens and nits.</summary>
    public static IReadOnlyList<LightUnit> Units(LightType type) => type switch
    {
        LightType.Directional => [LightUnit.Lux, LightUnit.Ev100],
        LightType.Point or LightType.Spot => [LightUnit.Lumen, LightUnit.Candela],
        _ => [LightUnit.Lumen, LightUnit.Nit],
    };

    /// <summary>Sets the preset's shape, its colour temperature with <see cref="UseTemperature"/> on, then its intensity for that shape.</summary>
    public void ApplyPreset(LightPreset preset)
    {
        if (preset.SpotAngle is float angle) SpotAngle = angle;
        if (preset.Width is float presetWidth) Width = presetWidth;
        if (preset.Height is float presetHeight) Height = presetHeight;
        if (preset.Length is float presetLength) Length = presetLength;
        if (preset.Radius is float radius) SourceRadius = radius;
        UseTemperature = true;
        Temperature = preset.Kelvin;
        Intensity = ToStored(preset.Unit, preset.Amount);
    }

    /// <summary>A brightness in a unit as the light keeps it, for its type and shape: lux for a directional light, candela for a point or spot, nits for an area light.</summary>
    public float ToStored(LightUnit unit, float amount) => unit switch
    {
        LightUnit.Ev100 => 2.5f * Maths.Pow(2f, amount),
        LightUnit.Lumen => amount / Maths.Max(LumensPerStored(), 1e-6f),
        _ => amount,
    };

    /// <summary>A brightness as the light keeps it, in another unit for its type and shape.</summary>
    public float FromStored(LightUnit unit, float stored) => unit switch
    {
        LightUnit.Ev100 => MathF.Log2(Maths.Max(stored, 1e-6f) / 2.5f),
        LightUnit.Lumen => stored * LumensPerStored(),
        _ => stored,
    };

    // Lumens put out for each candela of a point or spot, or each nit of an area light
    private float LumensPerStored() => Type switch
    {
        LightType.Point => 4f * Maths.PI,
        LightType.Spot => 2f * Maths.PI * (1f - Maths.Cos(Maths.Clamp(SpotAngle, 1f, 179f) * 0.5f * Maths.Deg2Rad)),
        _ => Maths.PI * SurfaceArea(),
    };

    // The surface a rect, disk or tube shines from, in square metres
    private float SurfaceArea() => Type switch
    {
        LightType.Rect => Width * Height,
        LightType.Disk => Maths.PI * 0.25f * Width * Width,
        LightType.Tube => 2f * Maths.PI * SourceRadius * Length + 4f * Maths.PI * SourceRadius * SourceRadius,
        _ => 1f,
    };

    /// <summary>The linear colour the light shines: <see cref="Color"/>, times the temperature's colour while <see cref="UseTemperature"/> is on.</summary>
    public Color FinalColor
    {
        get
        {
            Color tint = Color;
            if (!UseTemperature) return tint;
            Color blackbody = ColorTemperature.ToLinear(Temperature);
            return new Color(tint.R * blackbody.R, tint.G * blackbody.G, tint.B * blackbody.B, tint.A);
        }
    }

    /// <summary>The luminous intensity in candela along the light's brightest direction, a far field estimate for area lights. Zero for a directional light.</summary>
    public float AxisCandela
    {
        get
        {
            float width = Maths.Max(Width, 0f), radius = Maths.Max(SourceRadius, 0f);
            return Type switch
            {
                LightType.Point or LightType.Spot => Intensity,
                LightType.Rect => Intensity * width * Maths.Max(Height, 0f),
                LightType.Disk => Intensity * Maths.PI * 0.25f * width * width,
                LightType.Tube => Intensity * (2f * radius * Maths.Max(Length, 0f) + Maths.PI * radius * radius),
                _ => 0f,
            };
        }
    }

    /// <summary>Where the light's brightest channel falls below the quality level's <see cref="QualityLevel.LightCutoff"/>. Zero for a directional light.</summary>
    public float AutomaticRange
    {
        get
        {
            if (Type == LightType.Directional) return 0f;
            Color color = FinalColor;
            float peak = Maths.Max(color.R, Maths.Max(color.G, color.B));
            return Maths.Sqrt(Maths.Max(AxisCandela * peak, 0f) / Maths.Max(QualitySettings.Active.LightCutoff, 1e-4f));
        }
    }

    /// <summary>Whether this type of light can cast shadows. Area lights do not yet.</summary>
    public bool CanCastShadows => Type is LightType.Directional or LightType.Point or LightType.Spot;

    /// <summary>The light in the scene's world, invalid while it is not there.</summary>
    public LightHandle Handle => _handle;

    /// <summary>The light as Graphite describes it, from the settings and the transform.</summary>
    public LightDesc Describe()
    {
        (Double3 position, Float3 forward, Float3 up) = Pose();
        Color color = FinalColor;
        var desc = new LightDesc
        {
            Type = (GLightType)Type,
            Position = position,
            Direction = forward,
            Up = up,
            Color = new Float3(color.R, color.G, color.B),
            Intensity = Intensity,
            Range = Maths.Max(Range, 0.01f),
            LayerMask = LayerMask.Mask,
            Static = GameObject.IsStatic,
            HostData = FogData(color),
            Shadow = new ShadowDesc
            {
                Enabled = CanCastShadows && CastShadows && ShadowStrength > 0,
                Resolution = (uint)Maths.Min((int)ShadowResolution, (int)QualitySettings.Active.MaxShadowResolution),
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
                desc.Shadow.Cascades = (byte)Maths.Clamp((int)QualitySettings.Active.ShadowCascades, 1, 4);
                desc.Shadow.Distance = Maths.Max(QualitySettings.Active.ShadowDistance, 0.01f);
                desc.Shadow.CascadeBlend = CascadeBlend;
                break;
            case LightType.Point:
                desc.SourceRadius = SourceRadius;
                break;
            case LightType.Spot:
                desc.SourceRadius = SourceRadius;
                desc.OuterAngle = Maths.Clamp(SpotAngle, 1f, 179f) * 0.5f;
                desc.InnerAngle = desc.OuterAngle * (1f - Maths.Clamp(SpotSoftness, 0f, 1f));
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

    // The transform's world position and axes, read again only when its world matrix changed
    private (Double3 Position, Float3 Forward, Float3 Up) Pose()
    {
        Transform transform = Transform;
        _ = transform.LocalToWorldMatrix;
        if (MainThreadContext.OnMainThread && _poseVersion == transform.WorldVersion && _poseVersion != 0)
            return _pose;

        Float3 position = transform.Position;
        (Double3, Float3, Float3) pose = (new Double3(position.X, position.Y, position.Z), Float3.Normalize(transform.Forward), Float3.Normalize(transform.Up));
        if (MainThreadContext.OnMainThread)
        {
            _pose = pose;
            _poseVersion = transform.WorldVersion;
        }
        return pose;
    }

    // What volumetric fog reads as the light's HostData: its scattering colour times intensity, then its scattering bias
    // with 4 added when it casts no fog shadows
    private Float4 FogData(Color color)
    {
        float intensity = Intensity, bias = 0f;
        bool shadows = true;
        FogLight? fog = Fog;
        if (fog.IsValid() && fog.GameObject == GameObject && fog.EnabledInHierarchy)
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
            return true;
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
        return !HasProfile || (Ready(Cookie) && Ready(IesProfile));
    }

    // Syncs it again next frame, for a setting that changed
    private void Wake() => SceneRendering.Wake(this, GameObject);


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

    public override void OnAddedToScene() => Fog = GetComponent<FogLight>();

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
                Debug.DrawWireConeAngle(position, forward, Maths.Clamp(SpotAngle, 1f, 179f) * 0.5f, Range, Color.Yellow);
                break;
            default:
                Debug.DrawWireSphere(position, Range, Color.Yellow);
                break;
        }
    }
}
