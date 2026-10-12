// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Numerics;

using Prowl.Graphite.Rendering;
using Prowl.Graphite.ShaderDef;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Rendering;

/// <summary>The skies Graphite ships, each a shader a sky material can use.</summary>
public enum BuiltInSky
{
    /// <summary>Scattered sunlight lit by the scene's sun: sun size and glow, exposure, tint and ground colour.</summary>
    Procedural,

    /// <summary>Two colours blended from the ground up.</summary>
    Gradient,

    /// <summary>A cubemap texture, tinted, exposed and turned about the up axis.</summary>
    Cubemap,

    /// <summary>An equirectangular panorama texture, tinted, exposed and turned about the up axis.</summary>
    Panorama,

    /// <summary>Six separate images, one per side.</summary>
    SixSided,
}

/// <summary>
/// A scene's sky, ambient light and fog. The scene's rendering mirrors it onto its world's environment every frame, one
/// field to one, so what is set here is what Graphite draws.
/// </summary>
public sealed class SceneEnvironment
{
    /// <summary>
    /// The sky drawn behind everything and captured into the sky probe for reflections and sky ambient: any material
    /// whose shader has a Sky pass, usually one of <see cref="BuiltInSky"/>. Null shows <see cref="SkyColor"/>.
    /// </summary>
    public Material? SkyMaterial = DefaultSky();

    /// <summary>The background, and what the sky probe sees, where no sky material draws.</summary>
    public Color SkyColor = new(0.2f, 0.3f, 0.5f, 1f);

    public AmbientSettings Ambient = new();
    public FogSettings Fog = new();
    public SkyCaptureSettings SkyCapture = new();

    public struct AmbientSettings
    {
        /// <summary>Uniform lights every surface with Color, Hemisphere blends GroundColor up to SkyColor, Sky takes the sky's own light.</summary>
        public AmbientMode Mode = AmbientMode.Uniform;
        public float Intensity = 1f;
        public Color Color = new(0.43f, 0.55f, 0.65f, 1f);
        public Color SkyColor = new(0.3f, 0.3f, 0.4f, 1f);
        public Color GroundColor = new(0.2f, 0.2f, 0.2f, 1f);

        public AmbientSettings() { }
    }

    public struct FogSettings
    {
        public FogMode Mode = FogMode.ExponentialSquared;
        public Color Color = new(0.5f, 0.5f, 0.5f, 1f);
        public float Density = 0.01f;

        /// <summary>Where linear fog starts and where it is full.</summary>
        public float Start = 20f;
        public float End = 100f;

        /// <summary>Colours the fog with the sky behind it, from the sky probe, instead of Color.</summary>
        public bool UseSky = false;

        public FogSettings() { }
    }

    public struct SkyCaptureSettings
    {
        /// <summary>When the sky is captured into the sky probe that reflections, sky ambient and sky fog read.</summary>
        public SkyCaptureMode Mode = SkyCaptureMode.OnChange;

        /// <summary>Edge length of the sky probe's cube, rounded to a power of two from 16 to 2048.</summary>
        public int Resolution = 128;

        /// <summary>Frames at least between two captures when capturing on change.</summary>
        public int Interval = 2;

        public SkyCaptureSettings() { }
    }

    /// <summary>The shader of one of Graphite's skies, shared by every material of it.</summary>
    public static Shader SkyShader(BuiltInSky sky) => Shader.LoadDefault(sky switch
    {
        BuiltInSky.Gradient => DefaultShader.GradientSky,
        BuiltInSky.Cubemap => DefaultShader.CubemapSky,
        BuiltInSky.Panorama => DefaultShader.PanoramaSky,
        BuiltInSky.SixSided => DefaultShader.SixSidedSky,
        _ => DefaultShader.ProceduralSky,
    });

    /// <summary>A new material of one of Graphite's skies, at the sky's defaults.</summary>
    public static Material CreateSkyMaterial(BuiltInSky sky) => new(SkyShader(sky)) { Name = $"{sky} Sky" };

    private static Material? DefaultSky()
    {
        try { return CreateSkyMaterial(BuiltInSky.Procedural); }
        catch (Exception e)
        {
            Debug.LogErrorOnce("SceneEnvironment.DefaultSky", $"The procedural sky could not be made: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// The ambient light a surface facing <paramref name="normal"/> receives, intensity included, the way the shaders
    /// light it without a lightmap or probe. Sky reads the sky's light as last read back from <paramref name="world"/>,
    /// and the sky colour until the first read back arrives or when there is no world.
    /// </summary>
    public Float3 EvaluateAmbient(Float3 normal, RenderWorld? world)
    {
        Float3 light = Ambient.Mode switch
        {
            AmbientMode.Sky when world != null && world.SkyAmbientVersion > 0 => world.SkyAmbient.Evaluate(normal),
            AmbientMode.Sky => Rgb(SkyColor),
            AmbientMode.Hemisphere => Rgb(Ambient.GroundColor) + (Rgb(Ambient.SkyColor) - Rgb(Ambient.GroundColor)) * (normal.Y * 0.5f + 0.5f),
            _ => Rgb(Ambient.Color),
        };
        return light * Ambient.Intensity;
    }

    /// <summary>Copies every setting onto a world's environment. The sky material's Graphite material is null headless.</summary>
    internal void ApplyTo(EnvironmentDesc env)
    {
        env.Ambient = (Graphite.Rendering.AmbientMode)Ambient.Mode;
        env.AmbientColor = Ambient.Color.ToFloat4();
        env.AmbientSky = Ambient.SkyColor.ToFloat4();
        env.AmbientGround = Ambient.GroundColor.ToFloat4();
        env.AmbientIntensity = Ambient.Intensity;
        env.Fog = new FogDesc
        {
            Mode = (Graphite.Rendering.FogMode)Fog.Mode,
            Color = Fog.Color.ToFloat4(),
            Density = Fog.Density,
            Start = Fog.Start,
            End = Fog.End,
            UseSky = Fog.UseSky,
        };
        env.SkyColor = SkyColor.ToFloat4();
        env.Sky = SkyMaterial.IsValid() ? SkyMaterial.GpuMaterial : null;
        env.SkyCapture = (Graphite.Rendering.SkyCaptureMode)SkyCapture.Mode;
        env.SkyProbeResolution = BitOperations.RoundUpToPowerOf2((uint)Math.Clamp(SkyCapture.Resolution, 16, 2048));
        env.SkyCaptureInterval = Math.Max(SkyCapture.Interval, 1);
    }

    private static Float3 Rgb(Color c) => new(c.R, c.G, c.B);
}
