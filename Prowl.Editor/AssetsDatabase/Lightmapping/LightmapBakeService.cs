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
    private LightmapBaker? _baker;
    private Job? _job;
    private AutoAtlasResult? _atlas;
    private readonly List<object> _renderers = new();    // MeshRenderer / SkinnedMeshRenderer, parallel to _atlas placements
    private readonly Dictionary<Guid, BakeTexture?> _texCache = new(); // diffuse albedo, deduped per texture asset
    private int _bakeTexCounter;                                       // unique names for Photonic textures (its registry is name-keyed)
    private List<Float3> _probePositions = new();
    private Scene? _scene;
    private Scene.LightmapBakeSettings _settings = new();
    private int _targetIterations;

    // TEMP perf diagnostics
    private System.Diagnostics.Stopwatch? _iterTimer;
    private double _lastIterMs;
    private int _lastIterCount;
    private bool _statsReported;

    public bool IsBaking => _job != null;
    public float Progress { get; private set; }
    public string Status { get; private set; } = "Idle";

    /// <summary>Begin a bake of <paramref name="scene"/>. Returns false (with a logged reason) if there's nothing to bake.</summary>
    public bool Start(Scene scene, Scene.LightmapBakeSettings settings)
    {
        if (IsBaking) return false;
        _scene = scene;
        _settings = settings;
        _renderers.Clear();
        _texCache.Clear();
        _bakeTexCounter = 0;
        _probePositions = new();

        var baker = new LightmapBaker();
        baker.Options.Bounces = settings.Bounces;
        baker.Options.SamplesPerIteration = 1;
        baker.Options.IncludeDirectLighting = true;
        baker.Options.DoBackfaceCull = settings.DoBackfaceCull;
        baker.Options.DilatePixels = settings.DilatePixels;
        baker.Options.RussianRoulette = settings.RussianRoulette;
        baker.Options.IgnoreAlbedo = settings.IgnoreAlbedo;
        baker.Options.Denoise = settings.Denoise;
        baker.Options.DenoiseIterations = Math.Max(1, settings.DenoiseRadius);
        if (settings.BakeSkyLighting)
            baker.Options.SkyColor = SceneSkyRadiance(scene);

        var bake = baker.BeginScene(scene.Name ?? "Scene");

        // --- Gather static renderers. Meshes with a usable lightmap UV (UV2, or UV0 as a fallback)
        // get their own lightmap; the rest are added as occluders so they still cast shadows and
        // bounce colour into lightmaps + probes (all static geometry contributes to the bake). ---
        var meshes = new List<(BakeMesh mesh, Float4x4 transform)>();
        var occluders = new List<(BakeMesh mesh, Float4x4 transform)>();
        int matCounter = 0;
        int meshKey = 0;
        foreach (var go in scene.AllObjects)
        {
            if (!go.IsStatic || !go.EnabledInHierarchy) continue;

            Mesh? mesh;
            List<AssetRef<Material>> mats;
            object renderer;
            if (go.GetComponent<MeshRenderer>() is { } mr) { mesh = mr.Mesh.Res; mats = mr.Materials; renderer = mr; }
            else if (go.GetComponent<SkinnedMeshRenderer>() is { } smr) { mesh = smr.SharedMesh.Res; mats = smr.Materials; renderer = smr; }
            else continue;

            if (!TryBuildBakeMesh(bake, mesh, mats, $"r{meshKey++}", ref matCounter, out var bm, out bool hasLightmapUV))
                continue;

            var xform = go.Transform.LocalToWorldMatrix;
            if (hasLightmapUV) { meshes.Add((bm, xform)); _renderers.Add(renderer); }
            else occluders.Add((bm, xform));
        }

        if (meshes.Count == 0)
        {
            Runtime.Debug.LogWarning("[Lightmap] No static renderers with UVs to lightmap. Mark objects Static (UV2 is used if present, otherwise the primary UVs).");
            return false;
        }

        // --- Lights (by bake mode). ---
        foreach (var go in scene.AllObjects)
        {
            var light = go.GetComponent<Light>();
            if (light == null || !light.EnabledInHierarchy || light.BakeMode == LightBakeMode.Realtime) continue;
            AddLight(bake, light);
        }

        // --- Pack atlases. ---
        _atlas = AutoAtlasPacker.Pack(baker, meshes, settings.AtlasSize, settings.AtlasSize, settings.TexelsPerUnit, padding: 2, bakeUVLayer: "UV1");

        // --- Occluders: present in the ray-traced scene (shadows + colour bounce) but never written
        // into an atlas page. ReceivesLighting=false keeps them out of rasterization. ---
        if (_atlas.Targets.Length > 0)
            foreach (var (om, ox) in occluders)
                _atlas.Targets[0].AddBakeInstance(om, ox).ReceivesLighting = false;

        // --- Probes. ---
        foreach (var go in scene.AllObjects)
        {
            var grp = go.GetComponent<LightProbeGroup>();
            if (grp != null && grp.EnabledInHierarchy) _probePositions.AddRange(grp.GetWorldPositions());
        }

        bake.End();
        _baker = baker;
        _targetIterations = Math.Max(1, settings.Samples);
        _job = baker.Start();
        Progress = 0f;
        Status = "Baking…";

        // TEMP perf diagnostics: scene/atlas shape so we can compare against the demo.
        _iterTimer = System.Diagnostics.Stopwatch.StartNew();
        _lastIterMs = 0; _lastIterCount = 0; _statsReported = false;
        Runtime.Debug.Log($"[LM perf] lightmapped={meshes.Count} occluders={occluders.Count} atlasPages={_atlas.Targets.Length} " +
            $"atlasSize={settings.AtlasSize} texels/unit={settings.TexelsPerUnit} bounces={settings.Bounces} targetSamples={settings.Samples} threads={System.Environment.ProcessorCount}");
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

        // TEMP perf diagnostics: covered-texel count (once) + ms per iteration.
        if (_atlas != null && _iterTimer != null && _job.IterationCount > _lastIterCount)
        {
            if (!_statsReported)
            {
                long covered = 0, total = 0;
                foreach (var t in _atlas.Targets)
                {
                    var cov = t.Coverage;
                    for (int i = 0; i < cov.Length; i++) if (cov[i] != 0) covered++;
                    total += (long)t.Width * t.Height;
                }
                if (total > 0)
                {
                    Runtime.Debug.Log($"[LM perf] coveredTexels={covered:N0}/{total:N0} ({100.0 * covered / total:F0}% over {_atlas.Targets.Length} page(s)) — each iteration shoots ~{covered:N0} primary rays x {_settings.Bounces} bounces");
                    _statsReported = true;
                }
            }
            double now = _iterTimer.Elapsed.TotalMilliseconds;
            int it = _job.IterationCount;
            double msPerIter = (now - _lastIterMs) / Math.Max(1, it - _lastIterCount);
            Runtime.Debug.Log($"[LM perf] iter {it}: {msPerIter:F0} ms/iter");
            _lastIterMs = now; _lastIterCount = it;
        }

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
                                  string nameKey, ref int matCounter, out BakeMesh result, out bool hasLightmapUV)
    {
        result = null!;
        hasLightmapUV = false;
        if (mesh == null || mesh.VertexCount == 0) return false;

        // Lightmap UV: prefer the dedicated UV2 set (generated at import), fall back to the primary
        // UVs as a fallback. A mesh with no UVs at all still builds (as an occluder); zeros fill the
        // unused layers.
        Float2[]? lmUV = mesh.HasUV2 ? mesh.UV2 : (mesh.HasUV ? mesh.UV : null);
        hasLightmapUV = lmUV != null;
        Float2[] uv0 = mesh.HasUV ? mesh.UV : (lmUV ?? new Float2[mesh.VertexCount]);
        Float2[] uv1 = lmUV ?? new Float2[mesh.VertexCount];

        var builder = bake.BeginMesh($"{nameKey}_{mesh.Name}")
            .AddVertices(mesh.Vertices, mesh.Normals)
            .AddUVLayer("UV0", uv0)
            .AddUVLayer("UV1", uv1);

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

        // Photonic keys textures by name and rejects duplicates, so give each one a unique name
        // (Texture2D names aren't unique — many default to "New Texture").
        string name = $"albedo_{_bakeTexCounter++}";
        BakeTexture? result = TryLoadFromFile(bake, tex, name) ?? TryReadback(bake, tex, name);
        if (key != Guid.Empty) _texCache[key] = result;
        return result;
    }

    // Exact path: load the source image file (sRGB, gamma 2.2). Flipped to match the runtime's
    // Texture2D.FromImage flip so the bake samples the same texels the runtime does.
    private static BakeTexture? TryLoadFromFile(BakeScene bake, Texture2D tex, string name)
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
            return bake.CreateTextureRGBA(name, (int)img.Width, (int)img.Height, rgba, 2.2f);
        }
        catch { return null; }
    }

    // Fallback for embedded/generated textures: read back the GPU pixels (RGBA8 or RGBA16).
    private static BakeTexture? TryReadback(BakeScene bake, Texture2D tex, string name)
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
        return bake.CreateTextureRGBA(name, w, h, rgba, 2.2f);
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
