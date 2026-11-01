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

using GShadowFilter = Prowl.Graphite.Rendering.ShadowFilter;

namespace Prowl.Runtime;

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

/// <summary>
/// A light in the scene's world, kept up to date from its settings and transform. It shines along Forward. A new light
/// starts as its <see cref="DefaultPreset"/>.
/// </summary>
public abstract class Light : Component, IRenderSync
{
    /// <summary>Linear colour. With <see cref="UseTemperature"/> on it tints the temperature's colour, like a filter in front of the light.</summary>
    public Color Color { get => color; set { color = value; Wake(); } }
    [SerializeField] private Color color = Color.White;

    /// <summary>Whether the light takes the colour of a black body at <see cref="Temperature"/>, times <see cref="Color"/>.</summary>
    public bool UseTemperature { get => useTemperature; set { useTemperature = value; Wake(); } }
    [SerializeField] private bool useTemperature = true;

    /// <summary>The colour temperature in Kelvin: a candle about 1900, a bulb 2700, daylight 5600, overcast sky 6500.</summary>
    public float Temperature { get => temperature; set { temperature = value; Wake(); } }
    [SerializeField, ShowIf(nameof(UseTemperature)), Range(ColorTemperature.Min, ColorTemperature.Max)] private float temperature;

    /// <summary>The brightness in the unit the light keeps, the first of its <see cref="Units"/> other than EV100.</summary>
    public float Intensity { get => intensity; set { intensity = value; Wake(); } }
    [SerializeField] private float intensity;

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

    private bool ShowsShadows => CanCastShadows && CastShadows;

    protected Light() => ApplyPreset(DefaultPreset);

    /// <summary>The real lights this kind of light can start from.</summary>
    public abstract IReadOnlyList<LightPreset> Presets { get; }

    /// <summary>The preset a new light of this kind starts as.</summary>
    public virtual LightPreset DefaultPreset => Presets[0];

    /// <summary>The units this kind of light can be given in, the one it keeps first.</summary>
    public abstract IReadOnlyList<LightUnit> Units { get; }

    /// <summary>Whether this kind of light can cast shadows.</summary>
    public virtual bool CanCastShadows => false;

    /// <summary>Sets the preset's shape, its colour temperature with <see cref="UseTemperature"/> on, then its intensity for that shape.</summary>
    public void ApplyPreset(LightPreset preset)
    {
        ApplyShape(preset);
        useTemperature = true;
        temperature = preset.Kelvin;
        intensity = ToStored(preset.Unit, preset.Amount);
        Wake();
    }

    /// <summary>Whether the light is set as the preset: its colour temperature, shape and brightness.</summary>
    public bool IsPreset(LightPreset preset)
    {
        if (!UseTemperature || Temperature != preset.Kelvin || !HasShape(preset)) return false;
        float stored = ToStored(preset.Unit, preset.Amount);
        return MathF.Abs(Intensity - stored) <= stored * 1e-4f;
    }

    /// <summary>Sets the shape fields the preset gives, without waking the light.</summary>
    protected virtual void ApplyShape(LightPreset preset) { }

    /// <summary>Whether every shape field the preset gives matches the light's.</summary>
    protected virtual bool HasShape(LightPreset preset) => true;

    /// <summary>A brightness in a unit as the light keeps it, for its shape.</summary>
    public float ToStored(LightUnit unit, float amount) => unit switch
    {
        LightUnit.Ev100 => 2.5f * Maths.Pow(2f, amount),
        LightUnit.Lumen => amount / Maths.Max(LumensPerStored, 1e-6f),
        _ => amount,
    };

    /// <summary>A brightness as the light keeps it, in another unit for its shape.</summary>
    public float FromStored(LightUnit unit, float stored) => unit switch
    {
        LightUnit.Ev100 => MathF.Log2(Maths.Max(stored, 1e-6f) / 2.5f),
        LightUnit.Lumen => stored * LumensPerStored,
        _ => stored,
    };

    /// <summary>Lumens put out for each unit of <see cref="Intensity"/>.</summary>
    protected virtual float LumensPerStored => 1f;

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

    /// <summary>The light in the scene's world, invalid while it is not there.</summary>
    public LightHandle Handle => _handle;

    /// <summary>The light as Graphite describes it, from the settings and the transform.</summary>
    public LightDesc Describe()
    {
        (Double3 position, Float3 forward, Float3 up) = Pose();
        Color color = FinalColor;
        var desc = new LightDesc
        {
            Position = position,
            Direction = forward,
            Up = up,
            Color = new Float3(color.R, color.G, color.B),
            Intensity = Intensity,
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
        DescribeShape(ref desc);
        return desc;
    }

    /// <summary>Sets the Graphite type and the shape fields of the light's kind.</summary>
    protected abstract void DescribeShape(ref LightDesc desc);

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

        OnSynced();
        return IsReady;
    }

    /// <summary>Runs after the light was added to or updated in the world.</summary>
    private protected virtual void OnSynced() { }

    /// <summary>Runs when the light leaves the world.</summary>
    private protected virtual void OnRemoved() { }

    /// <summary>Whether every texture the light was given is in the world, so it draws as set.</summary>
    private protected virtual bool IsReady => true;

    // Syncs it again next frame, for a setting that changed
    protected void Wake() => SceneRendering.Wake(this, GameObject);

    void IRenderSync.Detach(RenderWorld world) => Remove();

    private void Remove()
    {
        OnRemoved();
        if (_world != null && _handle.IsValid && _world.IsAlive(_handle))
            _world.RemoveLight(_handle);
        _handle = default;
        _world = null;
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
    }
}
