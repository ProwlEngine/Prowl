// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Draws a scene's <see cref="VolumetricClouds"/> into a camera's color, after the opaques.
/// <para/>
/// Each frame a coverage map around the camera holds how much cloud there is at every point of the ground plane. The
/// flat layers and the far and near particle grids draw into a reduced resolution target in two passes that only add
/// up, so their order never matters, and the result is scaled up over the scene with depth aware weights so the clouds
/// stay behind geometry edges.
/// </summary>
internal static class CloudRenderer
{
    private const int PassCoverage = 0, PassDepth = 1, PassLayer = 2, PassParticles = 3, PassComposite = 4, PassSunDepth = 5, PassTemporal = 6, PassResolve = 7;

    private const int CoverageResolution = 1024;

    private static Material? s_material;
    private static readonly Dictionary<int, (Mesh Mesh, long UsedAt)> s_grids = [];
    private static readonly List<int> s_staleGrids = [];

    /// <summary>
    /// The coverage and sun maps of one <see cref="VolumetricClouds"/>, drawn once a frame around the main camera and
    /// shared by every camera that frame: split screen views, render texture cameras, both eyes and probe captures.
    /// </summary>
    internal sealed class CloudMaps : IDisposable
    {
        public RenderTexture? Coverage;
        public RenderTexture? SunDepth;

        // Which clock, frame and centre the maps were drawn for, since play mode swaps in a clock of its own and a
        // paused clock still lets the camera move
        public object? Clock;
        public long Frame = -1;
        public Float3 Centre;
        public bool HasSunDepth;
        public Float4 Rect;

        public void Dispose()
        {
            if (Coverage.IsValid()) Coverage!.Dispose();
            ReleaseSunDepth();
        }

        public void ReleaseSunDepth()
        {
            if (SunDepth.IsValid()) SunDepth!.Dispose();
            SunDepth = null;
            HasSunDepth = false;
        }
    }

    // The clouds built up over frames at their target resolution, kept per camera and per eye
    private sealed class CloudHistory : IDisposable
    {
        public sealed class Eye
        {
            // Color and kilometers times coverage
            public RenderTexture? Read, Write;

            // Scene distance at the history's resolution this frame and last, so pixels geometry uncovers are known
            public RenderTexture? Depth, PreviousDepth;
            public bool Valid;
            public int Frame;
            public double PreviousWindX, PreviousWindZ;
            public Float4x4 PreviousViewProjection, PreviousStillViewProjection;

            // The settings the history was built with, so changing them enough starts it again
            public float[]? Settings;

            public void Release()
            {
                if (Read.IsValid()) Read!.Dispose();
                if (Write.IsValid()) Write!.Dispose();
                if (Depth.IsValid()) Depth!.Dispose();
                if (PreviousDepth.IsValid()) PreviousDepth!.Dispose();
            }
        }

        public readonly Eye[] Eyes = [new(), new(), new()];

        public void Dispose()
        {
            foreach (Eye eye in Eyes) eye.Release();
        }
    }

    // The order each frame's samples visit the pixels of a 2x2 block, diagonals first so any two frames cover it evenly
    private static readonly Int2[] s_jitterCells = [new(0, 0), new(1, 1), new(1, 0), new(0, 1)];

    /// <summary>Lets go of what <paramref name="camera"/> kept for clouds, for when its scene has none.</summary>
    public static void Release(Camera camera) => camera.ReleaseRenderData<CloudHistory>();

    public static void Render(Camera camera, in RenderPipeline.CameraSnapshot css, VolumetricClouds clouds, RenderTexture colorRT, RenderTexture prepass)
    {
        if (s_material.IsNotValid()) s_material = new Material(Shader.LoadDefault(DefaultShader.VolumetricClouds));
        Material mat = s_material!;
        Mesh grid = GridFor(Math.Clamp(clouds.ParticleGrid, 8, 512));

        float thickness = MathF.Max(clouds.Thickness, 1f);
        float radius = MathF.Max(clouds.Radius, 100f);
        float farRadius = radius * Math.Clamp(clouds.FarRadiusMultiplier, 2f, 8f);
        Float2 wind = clouds.WindOffset;
        Float3 cam = css.CameraPosition;

        mat.SetTexture("_CloudNoise2D", clouds.Noise.IsValid() ? clouds.Noise! : Texture2D.LoadDefault(DefaultTexture.CloudNoise));
        Texture3D erosion = clouds.ErosionNoise.IsValid() ? clouds.ErosionNoise! : Texture3D.LoadDefault(DefaultTexture3D.CloudNoise);
        mat.SetTexture3D("_CloudNoise3D", erosion);
        mat.SetFloat("_CloudErosionSize", erosion.Width);
        mat.SetVector("_CloudShape", new Float4(clouds.Altitude, thickness, 1f / thickness, MathF.Max(clouds.PlanetRadius, 1000f)));
        mat.SetVector("_CloudPattern", new Float4(1f / MathF.Max(clouds.PatternScale, 1f), Math.Clamp(clouds.Coverage, 0f, 1f),
            1f / MathF.Max(clouds.DetailScale, 1f), Math.Clamp(clouds.Erosion, 0f, 1f)));
        mat.SetVector("_CloudWind", wind);
        mat.SetVector("_CloudToSun", SunDirection(css.Scene));
        mat.SetVector("_CloudLighting", new Float4(MathF.Max(clouds.Density, 0f) * 0.004f, MathF.Max(clouds.SunBrightness, 0f),
            MathF.Max(clouds.AmbientBrightness, 0f), Math.Clamp(clouds.SilverLining, 0f, 1f)));
        mat.SetColor("_CloudAlbedo", clouds.Albedo);
        mat.SetFloat("_CloudAmbientSaturation", Math.Clamp(clouds.AmbientSaturation, 0f, 1f));
        mat.SetFloat("_CloudHorizonFade", 1f / MathF.Max(clouds.HorizonFade, 100f));
        mat.SetInt("_CloudLightSamples", clouds.MarchToSun ? Math.Clamp(clouds.LightSamples, 1, 16) : 0);
        mat.SetVector("_CloudPuff", new Float2(Math.Clamp(clouds.PuffStrength, 0f, 1f), MathF.Max(clouds.PuffScale, 0.01f)));
        mat.SetVector("_CloudCameraForward", css.CameraForward);
        mat.SetVector("_CloudCameraRight", css.CameraRight);

        using CommandBuffer cmd = Graphics.GetCommandBuffer("VolumetricClouds");

        CloudMaps maps = clouds.Maps ??= new CloudMaps();
        Float3 centre = MapCentre(camera, css);
        if (clouds.MarchToSun) maps.ReleaseSunDepth();
        if (maps.Clock != Time.CurrentTime || maps.Frame != Time.FrameCount || maps.Centre != centre || (!clouds.MarchToSun && !maps.HasSunDepth))
            DrawMaps(cmd, mat, maps, clouds, centre, farRadius, wind, erosion.Width);
        mat.SetVector("_CloudCoverageRect", maps.Rect);
        mat.SetTexture("_CloudCoverage", maps.Coverage!.MainTexture);
        if (maps.HasSunDepth) mat.SetTexture("_CloudSunDepth", maps.SunDepth!.MainTexture);

        // With temporal upscaling the clouds render a quarter of their target's pixels a frame, one of each 2x2 block
        // in turn, and the history fills in the rest. The target is kept even so every block is whole
        int scale = Math.Max((int)clouds.Resolution, 1);
        int outWidth = Math.Max(1, (int)css.PixelWidth / scale), outHeight = Math.Max(1, (int)css.PixelHeight / scale);
        // A probe capture turns to a new face every render, so there is nothing to carry over between them
        bool temporal = clouds.TemporalUpscale && !ReflectionProbeCapture.Capturing;
        if (!clouds.TemporalUpscale) Release(camera);
        int block = temporal ? 2 : 1;
        if (temporal)
        {
            outWidth = (outWidth + 1) & ~1;
            outHeight = (outHeight + 1) & ~1;
        }
        int width = outWidth / block, height = outHeight / block;

        CloudHistory.Eye? eye = null;
        Int2 cell = default;
        Float2 jitter = Float2.Zero;
        if (temporal)
        {
            eye = camera.GetRenderData(() => new CloudHistory()).Eyes[(int)camera.ActiveEye];
            if (eye.Read.IsNotValid() || eye.Read!.Width != outWidth || eye.Read.Height != outHeight)
            {
                eye.Release();
                eye.Read = NewHistory(outWidth, outHeight, TextureImageFormat.Short4, TextureImageFormat.Short);
                eye.Write = NewHistory(outWidth, outHeight, TextureImageFormat.Short4, TextureImageFormat.Short);
                eye.Depth = NewHistory(outWidth, outHeight, TextureImageFormat.Float);
                eye.PreviousDepth = NewHistory(outWidth, outHeight, TextureImageFormat.Float);
                eye.Valid = false;
            }
            if (!camera.HasPreviousViewProjectionMatrix || SettingsChanged(clouds, css.Scene, ref eye.Settings)) eye.Valid = false;
            cell = s_jitterCells[eye.Frame++ & 3];
            jitter = new Float2((cell.X + 0.5f) / block - 0.5f, (cell.Y + 0.5f) / block - 0.5f);
        }
        mat.SetVector("_CloudJitter", jitter);
        // How wide one pixel of the target is, from the projection itself so it holds for off centre eye projections
        mat.SetFloat("_CloudPixelAngle", 2f / (MathF.Max(MathF.Abs(css.NonJitteredProjection.c1.Y), 1e-4f) * height));

        // Scene depth under each of this frame's samples, as distance from the camera
        RenderTexture depth = RenderTexture.GetTemporaryRT(width, height, false, [TextureImageFormat.Float]);
        RenderTexture target = RenderTexture.GetTemporaryRT(width, height, false, [TextureImageFormat.Short4, TextureImageFormat.Short]);
        try
        {
            Float4x4 viewProjection = css.Projection * css.View;
            SetViewCorners(mat, viewProjection);
            mat.SetTexture("_CameraDepthTexture", prepass.InternalDepth!);
            mat.SetVector("_CloudFullResolution", new Float2(css.PixelWidth, css.PixelHeight));
            mat.SetVector("_CloudLowResolution", new Float2(width, height));
            mat.SetInt("_CloudDownsample", scale);
            mat.SetInt("_CloudDepthMode", temporal ? 1 : 0);
            cmd.Blit(depth, mat, PassDepth);
            mat.SetTexture("_CloudSceneDepth", depth.MainTexture);

            DrawWithMoments(cmd, mat, grid, clouds, target, width, height, cam, wind, radius, farRadius, thickness);

            RenderTexture result = target;
            if (temporal)
            {
                // Scene depth at the full target resolution, which the history is checked against and the composite
                // matches the clouds to
                RenderTexture outDepth = eye!.Depth!;
                mat.SetVector("_CloudLowResolution", new Float2(outWidth, outHeight));
                mat.SetInt("_CloudDepthMode", 0);
                cmd.Blit(outDepth, mat, PassDepth);
                mat.SetTexture("_CloudTargetDepth", outDepth.MainTexture);
                mat.SetTexture("_CloudPreviousTargetDepth", eye.PreviousDepth!.MainTexture);
                mat.SetTexture("_CloudLowDepth", depth.MainTexture);

                mat.SetVector("_CloudLowResolution", new Float2(width, height));
                mat.SetTexture("_CloudCurrent", target.InternalTextures[0]);
                mat.SetTexture("_CloudCurrentDistance", target.InternalTextures[1]);
                mat.SetTexture("_CloudHistory", eye.Read!.InternalTextures[0]);
                mat.SetTexture("_CloudHistoryDistance", eye.Read.InternalTextures[1]);
                mat.SetVector("_CloudHistoryResolution", new Float2(outWidth, outHeight));
                mat.SetVector("_CloudJitterCell", new Float2(cell.X, cell.Y));
                mat.SetInt("_CloudBlock", block);
                mat.SetFloat("_CloudHistoryValid", eye.Valid ? 1f : 0f);
                mat.SetVector("_CloudWindStep", new Float2((float)(clouds.WindX - eye.PreviousWindX), (float)(clouds.WindZ - eye.PreviousWindZ)));
                mat.SetMatrix("_CloudPreviousViewProjection", eye.PreviousViewProjection);
                mat.SetMatrix("_CloudPreviousStillViewProjection", eye.PreviousStillViewProjection);
                Float4x4 stillViewProjection = css.NonJitteredProjection * css.View;
                mat.SetMatrix("_CloudStillViewProjection", stillViewProjection);
                cmd.Blit(eye.Write!, mat, PassTemporal);
                result = eye.Write!;
                mat.SetVector("_CloudLowResolution", new Float2(outWidth, outHeight));
                mat.SetTexture("_CloudSceneDepth", outDepth.MainTexture);

                (eye.Read, eye.Write) = (eye.Write, eye.Read);
                (eye.Depth, eye.PreviousDepth) = (eye.PreviousDepth, eye.Depth);
                eye.Valid = true;
                eye.PreviousWindX = clouds.WindX;
                eye.PreviousWindZ = clouds.WindZ;
                eye.PreviousViewProjection = viewProjection;
                eye.PreviousStillViewProjection = stillViewProjection;
            }

            // Scaled up over the scene
            mat.SetTexture("_CloudTex", result.InternalTextures[0]);
            mat.SetTexture("_CloudTexDistance", result.InternalTextures[1]);
            cmd.SetRenderTarget(colorRT.frameBuffer);
            cmd.SetViewport(0, 0, (uint)colorRT.Width, (uint)colorRT.Height);
            cmd.Blit(mat, PassComposite);
            Graphics.Submit(cmd);
        }
        finally
        {
            RenderTexture.ReleaseTemporaryRT(depth);
            RenderTexture.ReleaseTemporaryRT(target);
        }
    }

    // The clouds drawn twice without caring about order: first how much each sample hides at its distance is added up
    // as moments, then every sample is lit and added dimmed by what the moments say lies in front of it, and the sums
    // are resolved into the target
    private static void DrawWithMoments(CommandBuffer cmd, Material mat, Mesh grid, VolumetricClouds clouds, RenderTexture target,
        int width, int height, Float3 cam, Float2 wind, float radius, float farRadius, float thickness)
    {
        RenderTexture moments = RenderTexture.GetTemporaryRT(width, height, false, [TextureImageFormat.Float4, TextureImageFormat.Float4]);
        RenderTexture lit = RenderTexture.GetTemporaryRT(width, height, false, [TextureImageFormat.Float4, TextureImageFormat.Float4]);
        try
        {
            // The moments span from just in front of the nearest cloud to the far grid's edge, so their precision goes
            // where the clouds are
            float nearest = MathF.Max(MathF.Abs(cam.Y - (clouds.Altitude + thickness * 0.5f)) - thickness, 50f);
            float furthest = MathF.Max(farRadius * 1.5f, nearest * 2f);
            mat.SetVector("_CloudMomentRange", new Float2(MathF.Log(nearest), MathF.Log(furthest)));

            mat.SetInt("_CloudMomentPass", 1);
            cmd.SetRenderTarget(moments.frameBuffer);
            cmd.SetViewport(0, 0, (uint)width, (uint)height);
            cmd.ClearRenderTarget(ClearFlags.Color, new Color(0f, 0f, 0f, 0f));
            DrawClouds(cmd, mat, grid, clouds, cam, wind, radius, farRadius, thickness);

            mat.SetInt("_CloudMomentPass", 0);
            mat.SetTexture("_CloudMoments0", moments.InternalTextures[0]);
            mat.SetTexture("_CloudMoments1", moments.InternalTextures[1]);
            cmd.SetRenderTarget(lit.frameBuffer);
            cmd.SetViewport(0, 0, (uint)width, (uint)height);
            cmd.ClearRenderTarget(ClearFlags.Color, new Color(0f, 0f, 0f, 0f));
            DrawClouds(cmd, mat, grid, clouds, cam, wind, radius, farRadius, thickness);

            mat.SetTexture("_CloudAccumulated", lit.InternalTextures[0]);
            mat.SetTexture("_CloudAccumulatedExtra", lit.InternalTextures[1]);
            cmd.Blit(target, mat, PassResolve);
        }
        finally
        {
            RenderTexture.ReleaseTemporaryRT(moments);
            RenderTexture.ReleaseTemporaryRT(lit);
        }
    }

    // Toward the scene's sun, straight up when it has none so the shaders never normalize a zero vector
    private static Float3 SunDirection(Scene scene)
    {
        IRenderableLight? sun = DefaultRenderPipeline.GetOrCreateLightSystem(scene).Directional;
        if (sun == null) return Float3.UnitY;
        Float3 direction = sun.GetForwardLightData().Direction;
        float length = Float3.Length(direction);
        return length > 1e-6f && float.IsFinite(length) ? direction / length : Float3.UnitY;
    }

    // The maps cover the far grid's reach around the main camera when it looks at this scene, else around this one
    private static Float3 MapCentre(Camera camera, in RenderPipeline.CameraSnapshot css)
    {
        Camera? main = Camera.Main;
        return main.IsValid() && main!.GameObject.Scene == css.Scene ? main.Transform.Position : camera.Transform.Position;
    }

    // The flat layers and both particle grids. Everything only adds up, so the order they draw in does not matter
    private static void DrawClouds(CommandBuffer cmd, Material mat, Mesh grid, VolumetricClouds clouds, Float3 cam, Float2 wind,
        float radius, float farRadius, float thickness)
    {
        if (clouds.Layers != null)
            foreach (CloudLayer layer in clouds.Layers)
            {
                if (layer == null || !layer.Enabled || layer.Opacity <= 0f) continue;
                if (layer.Style == CloudLayerStyle.Custom && layer.Texture.IsNotValid()) continue;
                DrawLayer(cmd, mat, layer, clouds);
            }

        float middle = clouds.Altitude + thickness * 0.5f;
        int side = Math.Clamp(clouds.ParticleGrid, 8, 512);
        float cellSpan = radius * 2f / side;
        float farCellSpan = farRadius * 2f / side;
        float particleRadius = thickness * 0.5f;
        float farSize = Math.Clamp(clouds.FarParticleSize, 1f, 16f);
        DrawGrid(cmd, mat, grid, cam, wind, middle, farCellSpan, particleRadius * farSize, farRadius, radius, 1f / farSize);
        DrawGrid(cmd, mat, grid, cam, wind, middle, cellSpan, particleRadius, radius, 0f, 1f);
    }

    private static readonly List<float> s_settings = [], s_tolerances = [];

    private static void AddSetting(float value, float tolerance = 0.01f)
    {
        s_settings.Add(value);
        s_tolerances.Add(tolerance);
    }

    // Whether the settings that shape the clouds moved more than about one percent since the eye's history was started,
    // or the sun more than about ten, in which case it starts again from this frame. The sun moves slowly through a day,
    // and the history follows small changes in light by itself within a few frames
    private static bool SettingsChanged(VolumetricClouds clouds, Scene scene, ref float[]? baseline)
    {
        s_settings.Clear();
        s_tolerances.Clear();
        AddSetting(clouds.Altitude); AddSetting(clouds.Thickness); AddSetting(clouds.Coverage); AddSetting(clouds.Density);
        AddSetting(clouds.PatternScale); AddSetting(clouds.Erosion); AddSetting(clouds.DetailScale); AddSetting(clouds.PuffStrength);
        AddSetting(clouds.PuffScale); AddSetting(clouds.Albedo.R); AddSetting(clouds.Albedo.G); AddSetting(clouds.Albedo.B);
        AddSetting(clouds.SunBrightness); AddSetting(clouds.AmbientBrightness); AddSetting(clouds.AmbientSaturation);
        AddSetting(clouds.SilverLining); AddSetting(clouds.HorizonFade); AddSetting(clouds.Radius); AddSetting(clouds.ParticleGrid);
        AddSetting(clouds.FarRadiusMultiplier); AddSetting(clouds.FarParticleSize); AddSetting(clouds.MarchToSun ? 1f : 0f);
        AddSetting(clouds.LightSamples); AddSetting(clouds.PlanetRadius);
        AddSetting(clouds.Noise.IsValid() ? clouds.Noise!.InstanceID : 0, 0f);
        AddSetting(clouds.ErosionNoise.IsValid() ? clouds.ErosionNoise!.InstanceID : 0, 0f);

        IRenderableLight? sun = DefaultRenderPipeline.GetOrCreateLightSystem(scene).Directional;
        if (sun != null)
        {
            var light = sun.GetForwardLightData();
            AddSetting(light.Direction.X, 0.1f); AddSetting(light.Direction.Y, 0.1f); AddSetting(light.Direction.Z, 0.1f);
            AddSetting(light.Color.X * light.Intensity, 0.1f); AddSetting(light.Color.Y * light.Intensity, 0.1f);
            AddSetting(light.Color.Z * light.Intensity, 0.1f);
        }

        if (clouds.Layers != null)
            foreach (CloudLayer layer in clouds.Layers)
            {
                if (layer == null) continue;
                AddSetting(layer.Enabled ? 1f : 0f); AddSetting((int)layer.Style); AddSetting(layer.Altitude);
                AddSetting(layer.Coverage); AddSetting(layer.Opacity); AddSetting(layer.Scale); AddSetting(layer.WindMultiplier);
                AddSetting(layer.Tint.R); AddSetting(layer.Tint.G); AddSetting(layer.Tint.B);
                AddSetting(layer.Texture.IsValid() ? layer.Texture!.InstanceID : 0, 0f);
            }

        int count = s_settings.Count;
        bool changed = baseline == null || baseline.Length != count;
        for (int i = 0; !changed && i < count; i++)
        {
            float a = s_settings[i], b = baseline![i];
            changed = MathF.Abs(a - b) > s_tolerances[i] * MathF.Max(1f, MathF.Max(MathF.Abs(a), MathF.Abs(b)));
        }
        if (changed) baseline = [.. s_settings];
        return changed;
    }

    // The world points under the screen's corners on a near and a far plane, as a corner and the steps across and up,
    // through a double precision inverse of the view projection
    private static void SetViewCorners(Material mat, Float4x4 viewProjection)
    {
        Span<double> inverse = stackalloc double[16];
        Invert(viewProjection, inverse);

        Float3 nearOrigin = CornerPoint(inverse, -1, -1, 0), farOrigin = CornerPoint(inverse, -1, -1, 1);
        mat.SetVector("_CloudNearOrigin", nearOrigin);
        mat.SetVector("_CloudNearRight", CornerPoint(inverse, 1, -1, 0) - nearOrigin);
        mat.SetVector("_CloudNearUp", CornerPoint(inverse, -1, 1, 0) - nearOrigin);
        mat.SetVector("_CloudFarOrigin", farOrigin);
        mat.SetVector("_CloudFarRight", CornerPoint(inverse, 1, -1, 1) - farOrigin);
        mat.SetVector("_CloudFarUp", CornerPoint(inverse, -1, 1, 1) - farOrigin);
    }

    private static Float3 CornerPoint(ReadOnlySpan<double> inverse, double x, double y, double z)
    {
        double px = inverse[0] * x + inverse[4] * y + inverse[8] * z + inverse[12];
        double py = inverse[1] * x + inverse[5] * y + inverse[9] * z + inverse[13];
        double pz = inverse[2] * x + inverse[6] * y + inverse[10] * z + inverse[14];
        double pw = inverse[3] * x + inverse[7] * y + inverse[11] * z + inverse[15];
        return new Float3((float)(px / pw), (float)(py / pw), (float)(pz / pw));
    }

    // A 4x4 inverse in double precision, columns first as the matrix stores them
    private static void Invert(Float4x4 matrix, Span<double> inv)
    {
        ReadOnlySpan<double> m =
        [
            matrix.c0.X, matrix.c0.Y, matrix.c0.Z, matrix.c0.W,
            matrix.c1.X, matrix.c1.Y, matrix.c1.Z, matrix.c1.W,
            matrix.c2.X, matrix.c2.Y, matrix.c2.Z, matrix.c2.W,
            matrix.c3.X, matrix.c3.Y, matrix.c3.Z, matrix.c3.W,
        ];
        inv[0] = m[5] * m[10] * m[15] - m[5] * m[11] * m[14] - m[9] * m[6] * m[15] + m[9] * m[7] * m[14] + m[13] * m[6] * m[11] - m[13] * m[7] * m[10];
        inv[4] = -m[4] * m[10] * m[15] + m[4] * m[11] * m[14] + m[8] * m[6] * m[15] - m[8] * m[7] * m[14] - m[12] * m[6] * m[11] + m[12] * m[7] * m[10];
        inv[8] = m[4] * m[9] * m[15] - m[4] * m[11] * m[13] - m[8] * m[5] * m[15] + m[8] * m[7] * m[13] + m[12] * m[5] * m[11] - m[12] * m[7] * m[9];
        inv[12] = -m[4] * m[9] * m[14] + m[4] * m[10] * m[13] + m[8] * m[5] * m[14] - m[8] * m[6] * m[13] - m[12] * m[5] * m[10] + m[12] * m[6] * m[9];
        inv[1] = -m[1] * m[10] * m[15] + m[1] * m[11] * m[14] + m[9] * m[2] * m[15] - m[9] * m[3] * m[14] - m[13] * m[2] * m[11] + m[13] * m[3] * m[10];
        inv[5] = m[0] * m[10] * m[15] - m[0] * m[11] * m[14] - m[8] * m[2] * m[15] + m[8] * m[3] * m[14] + m[12] * m[2] * m[11] - m[12] * m[3] * m[10];
        inv[9] = -m[0] * m[9] * m[15] + m[0] * m[11] * m[13] + m[8] * m[1] * m[15] - m[8] * m[3] * m[13] - m[12] * m[1] * m[11] + m[12] * m[3] * m[9];
        inv[13] = m[0] * m[9] * m[14] - m[0] * m[10] * m[13] - m[8] * m[1] * m[14] + m[8] * m[2] * m[13] + m[12] * m[1] * m[10] - m[12] * m[2] * m[9];
        inv[2] = m[1] * m[6] * m[15] - m[1] * m[7] * m[14] - m[5] * m[2] * m[15] + m[5] * m[3] * m[14] + m[13] * m[2] * m[7] - m[13] * m[3] * m[6];
        inv[6] = -m[0] * m[6] * m[15] + m[0] * m[7] * m[14] + m[4] * m[2] * m[15] - m[4] * m[3] * m[14] - m[12] * m[2] * m[7] + m[12] * m[3] * m[6];
        inv[10] = m[0] * m[5] * m[15] - m[0] * m[7] * m[13] - m[4] * m[1] * m[15] + m[4] * m[3] * m[13] + m[12] * m[1] * m[7] - m[12] * m[3] * m[5];
        inv[14] = -m[0] * m[5] * m[14] + m[0] * m[6] * m[13] + m[4] * m[1] * m[14] - m[4] * m[2] * m[13] - m[12] * m[1] * m[6] + m[12] * m[2] * m[5];
        inv[3] = -m[1] * m[6] * m[11] + m[1] * m[7] * m[10] + m[5] * m[2] * m[11] - m[5] * m[3] * m[10] - m[9] * m[2] * m[7] + m[9] * m[3] * m[6];
        inv[7] = m[0] * m[6] * m[11] - m[0] * m[7] * m[10] - m[4] * m[2] * m[11] + m[4] * m[3] * m[10] + m[8] * m[2] * m[7] - m[8] * m[3] * m[6];
        inv[11] = -m[0] * m[5] * m[11] + m[0] * m[7] * m[9] + m[4] * m[1] * m[11] - m[4] * m[3] * m[9] - m[8] * m[1] * m[7] + m[8] * m[3] * m[5];
        inv[15] = m[0] * m[5] * m[10] - m[0] * m[6] * m[9] - m[4] * m[1] * m[10] + m[4] * m[2] * m[9] + m[8] * m[1] * m[6] - m[8] * m[2] * m[5];

        double det = m[0] * inv[0] + m[1] * inv[4] + m[2] * inv[8] + m[3] * inv[12];
        double scale = Math.Abs(det) > 1e-300 ? 1.0 / det : 0.0;
        for (int i = 0; i < 16; i++) inv[i] *= scale;
    }

    private static RenderTexture NewHistory(int width, int height, params TextureImageFormat[] formats)
    {
        var history = new RenderTexture(width, height, false, formats);
        foreach (Texture2D texture in history.InternalTextures)
        {
            texture.SetWrapModes(TextureWrap.ClampToEdge, TextureWrap.ClampToEdge);
            texture.SetTextureFilters(TextureMin.Linear, TextureMag.Linear);
        }
        return history;
    }

    // The maps' texels ride the wind like the particles do, so a particle reads the same coverage every frame rather
    // than one interpolated afresh as the clouds slide across fixed texels
    private static void DrawMaps(CommandBuffer cmd, Material mat, CloudMaps maps, VolumetricClouds clouds, Float3 centre,
        float farRadius, Float2 wind, uint erosionSize)
    {
        float span = farRadius * 2f;
        float texel = span / CoverageResolution;
        Float2 min = LatticeOrigin(new Float2(centre.X, centre.Z), wind, texel) - new Float2(span * 0.5f, span * 0.5f);
        maps.Rect = new Float4(min.X, min.Y, 1f / span, span);
        mat.SetVector("_CloudCoverageRect", maps.Rect);

        if (maps.Coverage.IsNotValid()) maps.Coverage = NewMap(TextureImageFormat.Short);
        cmd.Blit(maps.Coverage!, mat, PassCoverage);
        mat.SetTexture("_CloudCoverage", maps.Coverage!.MainTexture);

        // How much cloud the sunlight crosses, from four heights through the layer over the same square, so particles
        // read it once instead of marching toward the sun per pixel. Its noise is blurred to the map's texel size
        maps.HasSunDepth = !clouds.MarchToSun;
        if (maps.HasSunDepth)
        {
            if (maps.SunDepth.IsNotValid()) maps.SunDepth = NewMap(TextureImageFormat.Short4);
            float texelsPerMapTexel = texel / MathF.Max(clouds.DetailScale, 1f) * erosionSize;
            mat.SetFloat("_CloudSunMapLod", MathF.Max(MathF.Log2(texelsPerMapTexel), 0f));
            cmd.Blit(maps.SunDepth!, mat, PassSunDepth);
        }

        maps.Clock = Time.CurrentTime;
        maps.Frame = Time.FrameCount;
        maps.Centre = centre;
    }

    private static RenderTexture NewMap(TextureImageFormat format)
    {
        var map = new RenderTexture(CoverageResolution, CoverageResolution, false, [format]);
        map.MainTexture.SetWrapModes(TextureWrap.ClampToEdge, TextureWrap.ClampToEdge);
        map.MainTexture.SetTextureFilters(TextureMin.Linear, TextureMag.Linear);
        return map;
    }

    // One grid of particles, snapped in whole cells to the camera on a lattice the wind carries along, so each
    // particle keeps sampling the same piece of cloud as it drifts
    private static void DrawGrid(CommandBuffer cmd, Material mat, Mesh grid, Float3 cam, Float2 wind, float middle, float cellSpan,
        float particleRadius, float outerRadius, float innerRadius, float flatten)
    {
        Float2 origin = LatticeOrigin(new Float2(cam.X, cam.Z), wind, cellSpan);
        Float4x4 model = Float4x4.CreateTranslation(new Float3(origin.X, middle, origin.Y)) * Float4x4.CreateScale(new Float3(cellSpan, 1f, cellSpan));

        mat.SetVector("_CloudGrid", new Float4(cellSpan, particleRadius, outerRadius, innerRadius));
        mat.SetVector("_CloudLattice", new Float2(MathF.Round((origin.X - wind.X) / cellSpan), MathF.Round((origin.Y - wind.Y) / cellSpan)));
        mat.SetFloat("_CloudFlatten", flatten);
        cmd.DrawMesh(grid, mat, PassParticles, model);
    }

    /// <summary>The lattice point nearest the camera, on a lattice of <paramref name="cellSpan"/> the wind carries along.</summary>
    internal static Float2 LatticeOrigin(Float2 camera, Float2 wind, float cellSpan)
        => new(wind.X + MathF.Round((camera.X - wind.X) / cellSpan) * cellSpan, wind.Y + MathF.Round((camera.Y - wind.Y) / cellSpan) * cellSpan);

    private static void DrawLayer(CommandBuffer cmd, Material mat, CloudLayer layer, VolumetricClouds clouds)
    {
        Float2 wind = clouds.WindOffset * layer.WindMultiplier;
        Float2 windDir = clouds.WindVector;
        mat.SetVector("_LayerShape", new Float4(layer.Altitude, Math.Clamp(layer.Coverage, 0f, 1f), Math.Clamp(layer.Opacity, 0f, 1f),
            1f / MathF.Max(layer.Scale, 1f)));
        mat.SetVector("_LayerWind", new Float4(wind.X, wind.Y, windDir.X, windDir.Y));
        mat.SetInt("_LayerStyle", (int)layer.Style);
        mat.SetColor("_LayerTint", layer.Tint);
        bool custom = layer.Style == CloudLayerStyle.Custom;
        mat.SetInt("_LayerUseTexture", custom ? 1 : 0);
        if (custom) mat.SetTexture("_LayerTexture", layer.Texture!);
        cmd.Blit(mat, PassLayer);
    }

    // One grid per size in use, so components with different sizes never rebuild each other's. A size nothing has
    // drawn with for a second is let go, so dragging the size through many values does not keep a mesh for each.
    // Wall time rather than frames, since the editor and play mode count frames on clocks of their own
    private static Mesh GridFor(int size)
    {
        long now = Environment.TickCount64;
        if (!s_grids.TryGetValue(size, out var entry) || entry.Mesh.IsNotValid())
            entry.Mesh = BuildGrid(size);
        s_grids[size] = (entry.Mesh, now);

        s_staleGrids.Clear();
        foreach (var (key, grid) in s_grids)
            if (now - grid.UsedAt > 1000) s_staleGrids.Add(key);
        foreach (int key in s_staleGrids)
        {
            if (s_grids[key].Mesh.IsValid()) s_grids[key].Mesh.Dispose();
            s_grids.Remove(key);
        }
        return entry.Mesh;
    }

    /// <summary>
    /// A square of <paramref name="size"/> by <paramref name="size"/> particles one unit apart around the origin. All
    /// four corners of a particle sit at its centre and carry which corner they are in their UV, so the vertex shader
    /// opens each one into a camera facing disc.
    /// </summary>
    internal static Mesh BuildGrid(int size)
    {
        int count = size * size;
        var vertices = new Float3[count * 4];
        var uv = new Float2[count * 4];
        float half = size * 0.5f;

        for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                int p = z * size + x;
                var centre = new Float3(x + 0.5f - half, 0f, z + 0.5f - half);
                for (int c = 0; c < 4; c++)
                {
                    vertices[p * 4 + c] = centre;
                    uv[p * 4 + c] = new Float2(c & 1, c >> 1);
                }
            }

        var indices = new uint[count * 6];
        for (int i = 0; i < count; i++)
        {
            uint b = (uint)(i * 4);
            indices[i * 6 + 0] = b;
            indices[i * 6 + 1] = b + 1;
            indices[i * 6 + 2] = b + 2;
            indices[i * 6 + 3] = b + 1;
            indices[i * 6 + 4] = b + 3;
            indices[i * 6 + 5] = b + 2;
        }

        return new Mesh
        {
            Name = "Cloud Particles",
            IndexFormat = IndexFormat.UInt32,
            Vertices = vertices,
            UV = uv,
            Indices = indices,
        };
    }
}
