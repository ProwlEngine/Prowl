using System;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// The view from under the ocean: the surface is drawn again into a mask, +1
/// where its front faces show and -1 where its back faces do, then the view is fogged wherever it looks out from under
/// the water, toward the water's scatter colour by its absorption over the distance to what it sees, the surface from
/// below included. Where no ring of the surface reaches, a pixel is under water when its ray ends below the sea far
/// out. A band a few pixels wide darkens where the mask changes, the meniscus. It runs on cameras near or under the
/// water, after the transparents. The water scatters the world's sky and its sun as far as the sun's shadow at the
/// camera lets it in. What the view sees under the water loses the light the water above it absorbs on
/// the way down from the sun, and gets caustics where the sun's shadow lets them, before it is fogged.
/// </summary>
public sealed class OceanUnderwater : RenderFeature, IDisposable
{
    private const string Fragment = """
        #include "Graphite/Internal/PostView.glsl"
        #include "Graphite/Ocean/OceanCaustics.glsl"
        #include "Graphite/Lights.glsl"
        uniform GRAPHITE_SCREEN_TEXTURE _OceanMask;
        uniform GRAPHITE_SCREEN_TEXTURE _OceanMaskDepth;
        uniform GRAPHITE_SCREEN_TEXTURE _OceanSceneDepth;
        uniform vec4 _OceanAbsorption;
        uniform vec3 _OceanScatterSky;   // the scatter colour's radiance from the sky before exposure
        uniform vec3 _OceanScatterSun;   // and from the sun, which its shadow at the camera scales
        uniform vec4 _OceanLightAbsorption; // what the water absorbs per metre of the light coming down to what lies in it
        uniform float _OceanAbove;       // the camera's height above the sea
        uniform vec4 _OceanToSun;        // the direction to the sun, w 1 when there is one

        float Mask(ivec2 pixel, ivec2 size) { return Graphite_Fetch(_OceanMask, clamp(pixel, ivec2(0), size - 1), 0).r; }

        void main()
        {
            Graphite_ViewData view = Graphite_PostView();
            vec4 source = Graphite_SampleSource(vUV);
            ivec2 size = Graphite_Size(_OceanMask, 0);
            ivec2 pixel = clamp(ivec2(vUV * vec2(size)), ivec2(0), size - 1);
            float mask = Mask(pixel, size);
            vec3 start = Graphite_RelativeFromDepth(view, vUV, 1.0);
            vec3 ray = normalize(Graphite_RelativeFromDepth(view, vUV, 0.5) - start);
            if (mask == 0.0)
                mask = _OceanAbove + start.y + ray.y * 0.68 * view.depth.y < 0.0 ? -1.0 : 1.0;

            vec2 up = (view.view * vec4(0.0, 1.0, 0.0, 0.0)).xy;
            up = length(up) > 1e-4 ? normalize(vec2(up.x, -up.y)) : vec2(0.0, -1.0);
            float meniscus = 1.0;
            for (int k = 1; k <= 3; k++)
            {
                if (Mask(pixel + ivec2(round(up * float(k) * -sign(mask))), size) * mask < 0.0)
                    meniscus *= 0.9;
            }

            vec3 color = source.rgb;
            if (mask < 0.0)
            {
                float sceneDepth = Graphite_Fetch(_OceanSceneDepth, pixel, 0).r;
                if (sceneDepth > 0.0 && sceneDepth >= Graphite_Fetch(_OceanMaskDepth, pixel, 0).r)
                {
                    vec3 scene = Graphite_RelativeFromDepth(view, vUV, sceneDepth);
                    float waterAbove = -_OceanAbove - scene.y;
                    if (waterAbove > 0.0 && _OceanToSun.w > 0.5)
                    {
                        float shadow = Graphite_MainDirectionalShadowFactor(scene, vec3(0.0, 1.0, 0.0));
                        color *= mix(vec3(1.0), Ocean_Caustics(view, scene, waterAbove, _OceanToSun.xyz), shadow);
                    }
                    if (waterAbove > 0.0)
                        color *= exp(-_OceanLightAbsorption.rgb * waterAbove / max(_OceanToSun.w > 0.5 ? _OceanToSun.y : 1.0, 0.2));
                }
                float depth = max(sceneDepth, Graphite_Fetch(_OceanMaskDepth, pixel, 0).r);
                float travel = depth > 0.0 ? length(Graphite_RelativeFromDepth(view, vUV, depth)) : 1e6;
                vec3 fog = 1.0 - exp(-_OceanAbsorption.rgb * travel);
                float sunlit = _OceanToSun.w > 0.5 ? Graphite_MainDirectionalShadowFactor(vec3(0.0), vec3(0.0, 1.0, 0.0)) : 0.0;
                color = mix(color, (_OceanScatterSky + _OceanScatterSun * sunlit) * view.exposure.x, fog);
            }
            outColor = vec4(color * meniscus, source.a);
        }
        """;

    private readonly FullscreenProgram _program;
    private readonly Sampler _point, _linear;
    private readonly PropertySet _properties = new();

    private sealed class ViewState
    {
        public TextureHandle Mask;
    }

    private Float4 _absorption;
    private Float3 _scatterSky, _scatterSun;
    private Float4 _lightAbsorption;
    private Float4 _toSun;
    private double _seaLevel;
    private float _reach;

    /// <summary>The pass type the surface draws its mask through.</summary>
    public static readonly PassType MaskPass = new("OceanMask");

    public OceanUnderwater(Renderer renderer, OceanSurface surface) : base(RenderSlot.AfterTransparents, passes: [MaskPass])
    {
        Surface = surface;
        _program = renderer.CreateFullscreenProgram(Fragment, "Graphite/OceanUnderwater", lit: true);
        _point = renderer.Resources.PointClamp;
        _linear = renderer.Resources.LinearRepeat;
    }

    public OceanSurface Surface { get; }

    /// <summary>Scales the water's absorption under water only, so the view through it can reach further than the surface's tint suggests.</summary>
    public float DensityFactor { get; set; } = 0.3f;

    /// <summary>The world whose sun and sky light the water, set when the feature joins a world's features.</summary>
    public RenderWorld? World { get; set; }

    internal override void Attach(object set)
    {
        World = ((ExtensionSet<RenderFeature>)set).World ?? World;
        if (World != null)
            World.Underwater = this;
    }

    internal override void Detach(object set)
    {
        if (World?.Underwater == this)
            World.Underwater = null;
    }

    /// <summary>Whether a camera is under the sea, where this pass fogs the view and the volumetric fog stands aside.</summary>
    internal bool Below(Double3 camera) => camera.Y < _seaLevel;

    public override void Extract()
    {
        OceanFeature ocean = Surface.Ocean;
        _seaLevel = ocean.SeaLevel;
        _reach = Surface.MaxVerticalDisplacement + 2f;
        _absorption = Surface.Absorption * MathF.Max(DensityFactor, 0f);

        Float3 sun = Float3.Zero, sky = Float3.Zero;
        float sunHeight = 1f;
        _toSun = Float4.Zero;
        if (World is { } world)
        {
            LightHandle handle = world.Environment.Sun;
            if (handle.IsValid && world.IsAlive(handle))
            {
                LightDesc light = world.GetLight(handle);
                float length = Float3.Length(light.Direction);
                sunHeight = length > 1e-6f ? MathF.Max(-light.Direction.Y / length, 0f) : 1f;
                sun = light.Color * light.Intensity;
                if (length > 1e-6f)
                    _toSun = new Float4(-light.Direction / length, 1f);
            }
            sky = world.SkyAmbientVersion > 0 ? world.SkyAmbient.Evaluate(Float3.UnitY) : world.EvaluateAmbient(Float3.UnitY);
        }
        Float4 scatter = Surface.ScatterColor;
        Float3 colour = new(scatter.X, scatter.Y, scatter.Z);
        _scatterSky = colour * sky / MathF.PI;
        _scatterSun = colour * sun * sunHeight / MathF.PI;
        _lightAbsorption = Surface.Absorption * MathF.Max(Surface.DepthLightScale, 0f);
    }

    public override bool IsActive(in FeatureView view) => view.Position.Y < _seaLevel + _reach;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.DrawsRenderables();
        builder.ReadScene();
        builder.WriteColor();
        GraphTextureDesc mask = GraphTextureDesc.Sized((int)view.Width, (int)view.Height, true, PixelFormat.R16_Float) with
        {
            ArrayLayers = view.LayerCount > 1 ? (uint)view.LayerCount : 0,
        };
        builder.GetViewState<ViewState>().Mask = builder.Temporary("Mask", mask);
    }

    public override void Render(FeatureContext context)
    {
        RenderTexture mask = context.Get(context.GetViewState<ViewState>().Mask);
        context.Command.SetFramebuffer(mask.Framebuffer, TargetLoadStoreOps.Clear(new Color(0f, 0f, 0f, 0f), 0f));
        context.Command.SetFullViewport();
        context.DrawPass(MaskPass);

        _properties.SetTexture("_OceanMask", mask.ColorTextures[0], _point);
        _properties.SetTexture("_OceanMaskDepth", mask.DepthTexture!, _point);
        _properties.SetTexture("_OceanSceneDepth", context.Depth!, _point);
        _properties.SetFloat4("_OceanAbsorption", _absorption);
        _properties.SetFloat3("_OceanScatterSky", _scatterSky);
        _properties.SetFloat3("_OceanScatterSun", _scatterSun);
        _properties.SetFloat4("_OceanLightAbsorption", _lightAbsorption);
        _properties.SetFloat("_OceanAbove", (float)(context.View.Position.Y - _seaLevel));
        _properties.SetFloat4("_OceanToSun", _toSun);
        _properties.SetTexture("_OceanCaustics", Surface.Caustics, _linear);
        _properties.SetTexture("_OceanNormals", Surface.Normals, _linear);
        Surface.SetCaustics((name, value) => _properties.SetFloat(name, value));
        context.Fullscreen(_program, _properties);
    }

    public void Dispose() => _program.Dispose();
}

public sealed partial class RenderWorld
{
    /// <summary>The ocean's view from under the water, which the volumetric fog stands aside for while a camera is under it.</summary>
    internal OceanUnderwater? Underwater { get; set; }
}
