// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using ImageMagick;

using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Projects;
using Prowl.Photonic;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.Lightmapping;

/// <summary>
/// Drives a lightmap bake for the open scene with Prowl.Photonic: gathers lightmap-static renderers
/// + lights + probes, runs the CPU baker on its background thread, then (on the main thread) writes
/// the atlas pages as RGBM PNGs into a <c>&lt;SceneName&gt;_lightmaps/</c> folder, assigns per-renderer
/// lightmap index/scale-offset, bakes + tetrahedralizes light probes, and saves the scene.
/// </summary>
public sealed class LightmapBakeService
{
    public sealed class Settings
    {
        // Atlas / resolution
        public int AtlasSize = 1024;
        public float TexelsPerUnit = 20f;
        public int DilatePixels = 2;       // edge dilation to stop bilinear bleed at seams

        // Quality
        public int Bounces = 2;
        public int Samples = 64;           // progressive indirect iterations before finalize
        public int ProbeSamples = 256;
        public PathTracerKind PathTracer = PathTracerKind.PerTexel;
        public bool JitterRayOrigin = true;
        public float RussianRoulette = 0f; // 0 = off

        // Edge-avoiding denoiser (runs once at finalize). Strength maps to the radiance edge-stop:
        // higher removes more noise but softens shadow edges.
        public bool Denoise = false;
        public float DenoiseStrength = 0.5f;

        // Environment as a GI source: feed the scene's ambient colour in as ray-miss (sky) radiance.
        // Off keeps the previous behaviour (black sky, lights-only GI).
        public bool BakeSkyLighting = false;

        // Debug: bake every surface as white Lambertian (isolates light/GI from albedo issues).
        public bool IgnoreAlbedo = false;
    }

    private LightmapBaker? _baker;
    private Job? _job;
    private AutoAtlasResult? _atlas;
    private readonly List<object> _renderers = new();    // MeshRenderer / SkinnedMeshRenderer, parallel to _atlas placements
    private readonly Dictionary<Guid, BakeTexture?> _texCache = new(); // diffuse albedo, deduped per texture asset
    private List<Float3> _probePositions = new();
    private Scene? _scene;
    private Settings _settings = new();
    private int _targetIterations;

    public bool IsBaking => _job != null;
    public float Progress { get; private set; }
    public string Status { get; private set; } = "Idle";

    /// <summary>Begin a bake of <paramref name="scene"/>. Returns false (with a logged reason) if there's nothing to bake.</summary>
    public bool Start(Scene scene, Settings settings)
    {
        if (IsBaking) return false;
        _scene = scene;
        _settings = settings;
        _renderers.Clear();
        _texCache.Clear();
        _probePositions = new();

        var baker = new LightmapBaker();
        baker.Options.Bounces = settings.Bounces;
        baker.Options.SamplesPerIteration = 1;
        baker.Options.IncludeDirectLighting = true;
        baker.Options.PathTracer = settings.PathTracer;
        baker.Options.DilatePixels = settings.DilatePixels;
        baker.Options.JitterRayOrigin = settings.JitterRayOrigin;
        baker.Options.RussianRoulette = settings.RussianRoulette;
        baker.Options.IgnoreAlbedo = settings.IgnoreAlbedo;
        baker.Options.Denoise = settings.Denoise;
        baker.Options.DenoiseColorPhi = settings.DenoiseStrength;
        if (settings.BakeSkyLighting)
            baker.Options.SkyColor = SceneSkyRadiance(scene);

        var bake = baker.BeginScene(scene.Name ?? "Scene");

        // --- Gather lightmap-static renderers -> BakeMesh + world transform. ---
        var meshes = new List<(BakeMesh mesh, Float4x4 transform)>();
        int matCounter = 0;
        foreach (var go in scene.AllObjects)
        {
            if (!go.IsStatic) continue;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && TryBuildBakeMesh(bake, mr.Mesh.Res, mr.Materials, $"r{_renderers.Count}", ref matCounter, out var bm))
            {
                meshes.Add((bm, go.Transform.LocalToWorldMatrix));
                _renderers.Add(mr);
                continue;
            }

            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr != null && TryBuildBakeMesh(bake, smr.SharedMesh.Res, smr.Materials, $"r{_renderers.Count}", ref matCounter, out var bm2))
            {
                meshes.Add((bm2, go.Transform.LocalToWorldMatrix));
                _renderers.Add(smr);
            }
        }

        if (meshes.Count == 0)
        {
            Runtime.Debug.LogWarning("[Lightmap] No lightmap-static renderers with UV2 to bake. Mark objects Static and enable 'Generate Lightmap UVs' on their models.");
            return false;
        }

        // --- Lights (by bake mode). ---
        foreach (var go in scene.AllObjects)
        {
            var light = go.GetComponent<Light>();
            if (light == null || light.BakeMode == LightBakeMode.Realtime) continue;
            AddLight(bake, light);
        }

        // --- Pack atlases. ---
        _atlas = AutoAtlasPacker.Pack(baker, meshes, settings.AtlasSize, settings.AtlasSize, settings.TexelsPerUnit, padding: 2, bakeUVLayer: "UV1");

        // --- Probes. ---
        foreach (var go in scene.AllObjects)
        {
            var grp = go.GetComponent<LightProbeGroup>();
            if (grp != null) _probePositions.AddRange(grp.GetWorldPositions());
        }

        bake.End();
        _baker = baker;
        _targetIterations = Math.Max(1, settings.Samples);
        _job = baker.Start();
        Progress = 0f;
        Status = "Baking…";
        return true;
    }

    /// <summary>Call once per editor frame while <see cref="IsBaking"/>. Finalizes when the target iterations are reached.</summary>
    public void Poll()
    {
        if (_job == null) return;

        if (_job.Status == JobStatus.Failed)
        {
            Runtime.Debug.LogError($"[Lightmap] Bake failed: {_job.Error?.Message}");
            Cleanup();
            Status = "Failed";
            return;
        }

        Progress = Math.Clamp(_job.IterationCount / (float)_targetIterations, 0f, 1f);
        Status = $"Baking… {_job.IterationCount}/{_targetIterations}";

        if (_job.IterationCount >= _targetIterations)
            FinalizeBake();
    }

    public void Cancel()
    {
        if (_job == null) return;
        _baker?.Cancel();
        Cleanup();
        Status = "Cancelled";
    }

    /// <summary>Returns true if the scene currently has any baked lightmap pages or probes to clear.</summary>
    public static bool HasBakedData(Scene scene) => scene.BakedLighting.HasLightmaps || scene.BakedLighting.HasProbes;

    /// <summary>
    /// Remove all baked lighting from the scene: delete the <c>&lt;SceneName&gt;_lightmaps/</c> folder,
    /// clear the scene's baked data + probes, and reset every renderer's lightmap index. Saves the scene.
    /// </summary>
    public void Clear(Scene scene)
    {
        if (IsBaking) return;

        string scenePath = EditorSceneManager.CurrentScenePath ?? "";
        if (!string.IsNullOrEmpty(scenePath) && Project.Current != null)
        {
            string assetsRoot = Project.Current.AssetsPath;
            string sceneDir = Path.GetDirectoryName(scenePath)?.Replace('\\', '/') ?? "";
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            string lmFolderAbs = Path.Combine(assetsRoot, (string.IsNullOrEmpty(sceneDir) ? "" : sceneDir + "/") + sceneName + "_lightmaps");
            try { if (Directory.Exists(lmFolderAbs)) Directory.Delete(lmFolderAbs, true); } catch { }
        }

        scene.BakedLighting = new Scene.BakedLightingData();
        scene.InvalidateProbeVolume();

        foreach (var go in scene.AllObjects)
        {
            if (go.GetComponent<MeshRenderer>() is { } mr) { mr.LightmapIndex = -1; mr.LightmapScaleOffset = new Float4(1, 1, 0, 0); }
            if (go.GetComponent<SkinnedMeshRenderer>() is { } smr) { smr.LightmapIndex = -1; smr.LightmapScaleOffset = new Float4(1, 1, 0, 0); }
        }

        EditorSceneManager.Save();
        Status = "Cleared";
        Runtime.Debug.Log("[Lightmap] Cleared baked lighting.");
    }

    /// <summary>Scene ambient colour as a flat sky radiance (used as ray-miss GI when sky baking is on).</summary>
    private static Float3 SceneSkyRadiance(Scene scene)
    {
        var a = scene.Ambient;
        Float4 c = a.Mode == Scene.AmbientLightParams.AmbientMode.Hemisphere ? a.SkyColor : a.Color;
        return new Float3((float)c.X, (float)c.Y, (float)c.Z) * a.Strength;
    }

    private void FinalizeBake()
    {
        Status = "Writing lightmaps…";
        var baker = _baker!;
        var atlas = _atlas!;
        var scene = _scene!;
        baker.Cancel();        // stop the progressive job
        baker.Job?.Wait();     // ensure the worker stopped writing before we post-process
        if (_settings.Denoise) Status = "Denoising…";
        baker.Job?.Denoise();  // edge-avoiding denoise + re-dilate (no-op unless enabled); atlas buffers are now final

        var db = EditorAssetDatabase.Instance;
        string scenePath = EditorSceneManager.CurrentScenePath ?? "";
        if (db == null || string.IsNullOrEmpty(scenePath))
        {
            Runtime.Debug.LogError("[Lightmap] Can't save baked data: no saved scene.");
            Cleanup();
            return;
        }

        string assetsRoot = Project.Current!.AssetsPath;
        string sceneDir = Path.GetDirectoryName(scenePath)?.Replace('\\', '/') ?? "";
        string sceneName = Path.GetFileNameWithoutExtension(scenePath);
        string lmFolderRel = (string.IsNullOrEmpty(sceneDir) ? "" : sceneDir + "/") + sceneName + "_lightmaps";
        string lmFolderAbs = Path.Combine(assetsRoot, lmFolderRel);

        // Rebake replaces: clear the folder.
        try { if (Directory.Exists(lmFolderAbs)) Directory.Delete(lmFolderAbs, true); } catch { }
        Directory.CreateDirectory(lmFolderAbs);

        // Write + import each atlas page (RGBM PNG).
        var lightmaps = new List<AssetRef<Texture2D>>();
        for (int i = 0; i < atlas.Targets.Length; i++)
        {
            var t = atlas.Targets[i];
            byte[] rgba = t.ReadRGBM(8f);
            string rel = $"{lmFolderRel}/Lightmap-{i}.png";
            WritePng(Path.Combine(lmFolderAbs, $"Lightmap-{i}.png"), rgba, t.Width, t.Height);
            Guid guid = db.ImportFile(rel);
            lightmaps.Add(new AssetRef<Texture2D>(guid));
        }
        scene.BakedLighting.Lightmaps = lightmaps;

        // Assign per-renderer index + scale/offset.
        for (int i = 0; i < _renderers.Count && i < atlas.Instances.Length; i++)
        {
            var inst = atlas.Instances[i];
            int atlasIndex = atlas.Placements[i].AtlasIndex;
            var so = new Float4(inst.UVScale.X, inst.UVScale.Y, inst.UVOffset.X, inst.UVOffset.Y);
            if (_renderers[i] is MeshRenderer mr) { mr.LightmapIndex = atlasIndex; mr.LightmapScaleOffset = so; }
            else if (_renderers[i] is SkinnedMeshRenderer smr) { smr.LightmapIndex = atlasIndex; smr.LightmapScaleOffset = so; }
        }

        // Probes: bake SH + tetrahedralize.
        if (_probePositions.Count > 0)
        {
            Status = "Baking probes…";
            Sh9Rgb[] sh = baker.BakeProbes(_probePositions, _settings.ProbeSamples, _settings.Bounces);
            var runtimeSH = new SphericalHarmonicsL2[sh.Length];
            for (int i = 0; i < sh.Length; i++) runtimeSH[i] = ConvertSH(sh[i]);
            var tet = ProbeTetrahedralizer.Build(_probePositions);

            scene.BakedLighting.ProbePositions = _probePositions.ToArray();
            scene.BakedLighting.ProbeSH = runtimeSH;
            scene.BakedLighting.ProbeTetrahedra = tet.Tetrahedra;
            scene.BakedLighting.ProbeTetNeighbours = tet.Neighbours;
            scene.InvalidateProbeVolume();
        }
        else
        {
            scene.BakedLighting.ProbePositions = [];
            scene.BakedLighting.ProbeSH = [];
            scene.BakedLighting.ProbeTetrahedra = [];
            scene.BakedLighting.ProbeTetNeighbours = [];
            scene.InvalidateProbeVolume();
        }

        EditorSceneManager.Save();
        Runtime.Debug.Log($"[Lightmap] Baked {lightmaps.Count} lightmap page(s), {_renderers.Count} renderer(s), {_probePositions.Count} probe(s).");
        Cleanup();
        Status = "Done";
    }

    private void Cleanup()
    {
        _baker?.Dispose();
        _baker = null;
        _job = null;
        _atlas = null;
        _renderers.Clear();
        _texCache.Clear();
        Progress = 0f;
    }

    // ---- helpers ----

    private bool TryBuildBakeMesh(BakeScene bake, Mesh? mesh, List<AssetRef<Material>> materials,
                                  string nameKey, ref int matCounter, out BakeMesh result)
    {
        result = null!;
        if (mesh == null || !mesh.HasUV2 || mesh.VertexCount == 0)
        {
            if (mesh != null && !mesh.HasUV2)
                Runtime.Debug.LogWarning($"[Lightmap] Mesh '{mesh.Name}' has no UV2; skipped. Enable 'Generate Lightmap UVs' on its model.");
            return false;
        }

        var builder = bake.BeginMesh($"{nameKey}_{mesh.Name}")
            .AddVertices(mesh.Vertices, mesh.Normals)
            .AddUVLayer("UV0", mesh.HasUV ? mesh.UV : mesh.UV2)
            .AddUVLayer("UV1", mesh.UV2);

        uint[] indices = mesh.Indices;
        int subCount = mesh.SubMeshCount;
        for (int s = 0; s < subCount; s++)
        {
            var sub = mesh.GetSubMesh(s);
            int start = sub.IndexStart, count = sub.IndexCount;
            if (count <= 0) continue;
            var groupIdx = new int[count];
            for (int k = 0; k < count; k++) groupIdx[k] = (int)indices[start + k];

            // One BakeMaterial per (renderer, submesh) with the Prowl material's base colour.
            string matName = $"m{matCounter++}";
            var bmat = bake.CreateMaterial(matName);
            var pm = (s < materials.Count ? materials[s] : (materials.Count > 0 ? materials[^1] : default)).Res;
            if (pm != null)
            {
                var c = pm._properties.GetColor("_MainColor");
                bmat.DiffuseColor = new Float3((float)c.R, (float)c.G, (float)c.B);

                // Feed the diffuse albedo texture so bounced light picks up its colour.
                var texRef = pm._properties.GetTextureRef("_MainTex");
                texRef.EnsureLoaded();
                var tex = texRef.Res;
                if (tex != null)
                {
                    var bt = GetOrCreateDiffuse(bake, tex);
                    if (bt != null)
                    {
                        bmat.DiffuseTexture = bt;
                        bmat.DiffuseUVLayer = "UV0";
                    }
                }
            }
            builder.AddMaterialGroup(matName, groupIdx);
        }

        result = builder.End();
        return true;
    }

    /// <summary>Get (or build + cache) a Photonic albedo texture for a Prowl diffuse texture.</summary>
    private BakeTexture? GetOrCreateDiffuse(BakeScene bake, Texture2D tex)
    {
        Guid key = tex.AssetID;
        if (key != Guid.Empty && _texCache.TryGetValue(key, out var cached)) return cached;

        BakeTexture? result = TryLoadFromFile(bake, tex) ?? TryReadback(bake, tex);
        if (key != Guid.Empty) _texCache[key] = result;
        return result;
    }

    // Exact path: load the source image file (sRGB, gamma 2.2). Flipped to match the runtime's
    // Texture2D.FromImage flip so the bake samples the same texels the runtime does.
    private static BakeTexture? TryLoadFromFile(BakeScene bake, Texture2D tex)
    {
        string? rel = tex.AssetPath;
        if (string.IsNullOrEmpty(rel) || rel.Contains('#') || Project.Current == null) return null; // embedded sub-asset -> no file
        string abs = Path.Combine(Project.Current.AssetsPath, rel);
        if (!File.Exists(abs)) return null;
        try
        {
            using var img = new MagickImage(abs);
            img.ColorSpace = ColorSpace.sRGB;
            img.ColorType = ColorType.TrueColorAlpha;
            img.Flip();
            byte[]? rgba = img.GetPixels().ToByteArray(PixelMapping.RGBA);
            if (rgba == null) return null;
            return bake.CreateTextureRGBA(tex.Name ?? rel, (int)img.Width, (int)img.Height, rgba, 2.2f);
        }
        catch { return null; }
    }

    // Fallback for embedded/generated textures: read back the GPU pixels (RGBA8 or RGBA16).
    private static BakeTexture? TryReadback(BakeScene bake, Texture2D tex)
    {
        int w = (int)tex.Width, h = (int)tex.Height;
        if (w <= 0 || h <= 0) return null;
        int n = w * h;
        var rgba = new byte[n * 4];
        try
        {
            if (tex.ImageFormat == TextureImageFormat.Color4b)
            {
                tex.GetData<byte>(rgba);
            }
            else if (tex.ImageFormat is TextureImageFormat.UnsignedShort4 or TextureImageFormat.Short4)
            {
                var tmp = new ushort[n * 4];
                tex.GetData<ushort>(tmp);
                for (int i = 0; i < n * 4; i++) rgba[i] = (byte)(tmp[i] >> 8);
            }
            else return null; // unsupported albedo format -> flat colour
        }
        catch { return null; }
        return bake.CreateTextureRGBA(tex.Name ?? "tex", w, h, rgba, 2.2f);
    }

    private static void AddLight(BakeScene bake, Light light)
    {
        var c = light.Color;
        // Match the realtime shader's radiance convention (Lighting.glsl): radiance = Color * (Intensity * 8).
        Float3 color = new Float3((float)c.R, (float)c.G, (float)c.B) * (light.Intensity * 8.0f);
        Float4x4 xform = light.Transform.LocalToWorldMatrix;
        bool bakeDirect = light.BakeMode == LightBakeMode.Baked; // Mixed bakes indirect only

        Photonic.Scene.Lights.Light bl;
        switch (light)
        {
            case PointLight pl: bl = bake.CreatePointLight(light.GameObject.Name, xform, color, pl.Range); break;
            case SpotLight sl:
            {
                // Prowl's SpotAngle/InnerSpotAngle are OUTER/INNER half-angles in degrees; Photonic's
                // ConeAngle/InnerConeAngle are FULL cone angles in radians (it halves them internally).
                var spot = bake.CreateSpotLight(light.GameObject.Name, xform, color, sl.Range, 2.0f * sl.SpotAngle * Maths.Deg2Rad);
                spot.InnerConeAngle = 2.0f * sl.InnerSpotAngle * Maths.Deg2Rad;
                bl = spot;
                break;
            }
            default:
            {
                // Prowl's realtime directional light uses Transform.Forward as the TO-LIGHT
                // direction (the sun shines along -Forward), whereas Photonic treats the transform's
                // +Z column as the light's TRAVEL direction. Negate +Z so the baked sun matches realtime.
                var dirX = xform;
                dirX.c2 = new Float4(-xform.c2.X, -xform.c2.Y, -xform.c2.Z, xform.c2.W);
                bl = bake.CreateDirectionalLight(light.GameObject.Name, dirX, color);
                break;
            }
        }
        bl.BakeDirect = bakeDirect;
    }

    private static SphericalHarmonicsL2 ConvertSH(in Sh9Rgb s) => new SphericalHarmonicsL2
    {
        C0 = s.C0, C1 = s.C1, C2 = s.C2, C3 = s.C3, C4 = s.C4, C5 = s.C5, C6 = s.C6, C7 = s.C7, C8 = s.C8,
    };

    private static void WritePng(string absolutePath, byte[] rgba, int width, int height)
    {
        var settings = new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.RGBA);
        using var img = new MagickImage();
        img.ReadPixels(rgba, settings);
        // Photonic's atlas buffer is row-0-first (UV1 v=0 == row 0), but Texture2D.FromImage calls
        // image.Flip() on import. Pre-flip here so the two cancel and the sampled v matches the
        // packed UV1. Store raw bytes (linear RGBM); sampled linear and decoded in-shader.
        img.Flip();
        img.Write(absolutePath, MagickFormat.Png32);
    }
}
