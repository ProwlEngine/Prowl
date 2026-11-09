// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Aperture;
using Prowl.Echo;
using Prowl.Editor.Importers;

using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Projects;
using Prowl.Photonic;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.Lightmapping;

/// <summary>
/// Drives one lightmap bake over every loaded scene of the edit world with Prowl.Photonic, so shadows and bounced light
/// cross between scenes. Each scene gets its own atlas pages in a SceneName_lightmaps folder beside its file, its own
/// placements and its own light probes, and is marked dirty. A bake never saves a scene.
/// </summary>
public sealed class LightmapBakeService
{
    // One loaded scene's share of a bake: its renderers, its pages and its probes.
    private sealed class SceneBake(OpenScene scene, SceneSection section)
    {
        public readonly OpenScene Scene = scene;
        public readonly SceneSection Section = section;
        public readonly List<(BakeMesh Mesh, Float4x4 Transform)> Meshes = [];
        public readonly List<object> Renderers = [];   // MeshRenderer / SkinnedMeshRenderer, parallel to the atlas placements
        public readonly List<Float3> Probes = [];
        public AutoAtlasResult? Atlas;
    }

    private LightmapBaker? _baker;
    private Job? _job;
    private readonly List<SceneBake> _scenes = [];
    private readonly Dictionary<Guid, BakeTexture?> _texCache = []; // diffuse albedo, deduped per texture asset
    private int _bakeTexCounter;                                       // unique names for Photonic textures (its registry is name-keyed)
    private SceneInstance? _world;
    private LightmapBakeSettings _settings = new();
    private int _targetIterations;

    public bool IsBaking => _job != null;
    public float Progress { get; private set; }
    public string Status { get; private set; } = "Idle";

    /// <summary>The world the in-flight bake belongs to, or null when idle. Only one bake runs at a time.</summary>
    public SceneInstance? TargetScene => _world;

    /// <summary>The loaded scenes of <paramref name="world"/> a bake covers, in order. Empty outside the edit world.</summary>
    public static List<OpenScene> BakedScenes(SceneInstance world)
        => [.. EditorSceneManager.OpenScenes.Where(o => o.Section is { IsLoaded: true } s && ReferenceEquals(s.Instance, world))];

    /// <summary>A loaded scene with no file yet, which blocks a bake since its pages would have nowhere to go.</summary>
    public static OpenScene? UntitledScene(SceneInstance world) => BakedScenes(world).FirstOrDefault(o => o.Path == null);

    /// <summary>
    /// Begin one bake of every loaded scene of <paramref name="world"/> with the active scene's
    /// <paramref name="settings"/>. Returns false, with a logged reason, when there is nothing to bake or a scene is untitled.
    /// </summary>
    public bool Start(SceneInstance world, LightmapBakeSettings settings)
    {
        if (IsBaking) return false;
        List<OpenScene> baked = BakedScenes(world);
        if (baked.Count == 0)
        {
            Runtime.Debug.LogWarning("[Lightmap] Only the scenes open in the editor can be baked.");
            return false;
        }
        if (baked.FirstOrDefault(o => o.Path == null) is { } untitled)
        {
            Runtime.Debug.LogWarning($"[Lightmap] Save '{untitled.Name}' before baking, so its lightmaps have a folder next to its file.");
            return false;
        }

        _world = world;
        _settings = settings;
        _scenes.Clear();
        _texCache.Clear();
        _bakeTexCounter = 0;
        var bySection = new Dictionary<SceneSection, SceneBake>(ReferenceEqualityComparer.Instance);
        foreach (OpenScene open in baked)
        {
            var share = new SceneBake(open, open.Section!);
            _scenes.Add(share);
            bySection[share.Section] = share;
        }

        var baker = new LightmapBaker();
        baker.Options.Bounces = settings.Bounces;
        baker.Options.SamplesPerIteration = 1;
        baker.Options.IncludeDirectLighting = true;
        baker.Options.DoBackfaceCull = settings.DoBackfaceCull;
        baker.Options.DilatePixels = settings.DilatePixels;
        baker.Options.SparseStride = Math.Max(1, settings.SparseStride);
        baker.Options.Diagnostics.IgnoreAlbedo = settings.IgnoreAlbedo;
        if (settings.BakeSkyLighting)
            baker.Options.SkyColor = SceneSkyRadiance(world);

        BakeScene bake = baker.BeginScene(world.Name ?? "Scene");

        // Static renderers of every loaded scene. Meshes with a usable lightmap UV (UV2, or UV0 as a fallback) get a
        // lightmap in their own scene's pages, the rest are occluders that still cast shadows and bounce colour.
        var occluders = new List<(BakeMesh Mesh, Float4x4 Transform)>();
        int matCounter = 0;
        int meshKey = 0;
        foreach (GameObject go in world.Objects)
        {
            if (!go.IsStatic || !go.EnabledInHierarchy || go.Section is not { } section || !bySection.TryGetValue(section, out SceneBake? share)) continue;

            Mesh? mesh;
            IReadOnlyList<Material> mats;
            object renderer;
            if (go.GetComponent<MeshRenderer>() is { } mr) { mesh = mr.Mesh; mats = mr.Materials; renderer = mr; }
            else if (go.GetComponent<SkinnedMeshRenderer>() is { } smr) { mesh = smr.SharedMesh; mats = smr.Materials; renderer = smr; }
            else continue;

            if (!TryBuildBakeMesh(bake, mesh, mats, $"r{meshKey++}", ref matCounter, out BakeMesh? bm, out bool hasLightmapUV))
                continue;

            Float4x4 xform = go.Transform.LocalToWorldMatrix;
            if (hasLightmapUV) { share.Meshes.Add((bm, xform)); share.Renderers.Add(renderer); }
            else occluders.Add((bm, xform));
        }

        if (_scenes.All(share => share.Meshes.Count == 0))
        {
            Runtime.Debug.LogWarning("[Lightmap] No static renderers with UVs to lightmap. Mark objects Static (UV2 is used if present, otherwise the primary UVs).");
            Cleanup();
            return false;
        }

        foreach (GameObject go in world.Objects)
        {
            Light? light = go.GetComponent<Light>();
            if (light.IsNotValid() || !light.EnabledInHierarchy || light.BakeMode == LightBakeMode.Realtime) continue;
            if (go.Section is not { } section || !bySection.ContainsKey(section)) continue;
            AddLight(bake, light);
        }

        // Each scene packs into pages of its own. Photonic traces every page's instances together, so shadows and bounces
        // still cross between scenes.
        foreach (SceneBake share in _scenes)
            if (share.Meshes.Count > 0)
                share.Atlas = AutoAtlasPacker.Pack(baker, share.Meshes, settings.AtlasSize, settings.AtlasSize, settings.TexelsPerUnit, padding: 2, bakeUVLayer: "UV1");

        // Occluders: in the traced scene (shadows and colour bounce) but never written into a page. One page is enough,
        // since the trace sees every page's instances.
        if (baker.Targets.Count > 0)
            foreach ((BakeMesh om, Float4x4 ox) in occluders)
                baker.Targets[0].AddBakeInstance(om, ox).ReceivesLighting = false;

        foreach (GameObject go in world.Objects)
        {
            LightProbeGroup? grp = go.GetComponent<LightProbeGroup>();
            if (grp.IsNotValid() || !grp.EnabledInHierarchy || go.Section is not { } section || !bySection.TryGetValue(section, out SceneBake? share)) continue;
            share.Probes.AddRange(grp.GetWorldPositions());
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

    /// <summary> Cancels the current bake, if any, and resets the service to idle. </summary>
    public void Cancel()
    {
        if (_job == null) return;
        _baker?.Cancel();
        Cleanup();
        Status = "Cancelled";
    }

    /// <summary>Whether any loaded scene of the world has baked lightmap pages or probes to clear.</summary>
    public static bool HasBakedData(SceneInstance world)
        => world.Sections.Any(s => !s.IsPersistent && (s.Lighting.HasLightmaps || s.Lighting.HasProbes));

    // The folder a scene's pages live in, beside its file.
    private static string LightmapFolderOf(string scenePath)
    {
        string sceneDir = Path.GetDirectoryName(scenePath)?.Replace('\\', '/') ?? "";
        string sceneName = Path.GetFileNameWithoutExtension(scenePath);
        return (string.IsNullOrEmpty(sceneDir) ? "" : sceneDir + "/") + sceneName + "_lightmaps";
    }

    /// <summary>
    /// Removes every loaded scene's baked lighting: deletes each one's lightmaps folder and drops its pages, placements
    /// and probes, marking each dirty. Unloaded scenes keep theirs. Saves nothing.
    /// </summary>
    public void Clear(SceneInstance world)
    {
        if (IsBaking) return;
        EditorAssetBackend? db = EditorAssetBackend.Instance;

        foreach (OpenScene open in BakedScenes(world))
        {
            if (open.Path is { } path && db != null && Project.Current != null && Directory.Exists(Path.Combine(Project.Current.AssetsPath, LightmapFolderOf(path))))
                db.DeleteAssetFolder(LightmapFolderOf(path));
            open.Section!.Lighting = new SceneLighting();
            EditorSceneManager.MarkDirty(open);
        }
        world.InvalidateProbeVolume();

        Status = "Cleared";
        Runtime.Debug.Log("[Lightmap] Cleared baked lighting.");
    }

    /// <summary>The scene's ambient light reaching an upward surface, as a flat sky radiance for rays that miss.</summary>
    private static Float3 SceneSkyRadiance(SceneInstance scene) => scene.Rendering.Ambient(Float3.UnitY);

    private void FinalizeBake()
    {
        Status = "Writing lightmaps…";
        LightmapBaker baker = _baker!;
        SceneInstance world = _world!;
        baker.Cancel();        // stop the progressive job
        baker.Job?.Wait();     // ensure the worker stopped writing before we read the atlas buffers

        EditorAssetBackend? db = EditorAssetBackend.Instance;
        if (db == null || Project.Current == null)
        {
            Runtime.Debug.LogError("[Lightmap] Can't save baked data: no project is open.");
            Cleanup();
            return;
        }

        // Every scene's new pages go to a staging folder first, so the previous bake stays intact until all of them import.
        var written = new List<(SceneBake Share, string Folder, string Staging, List<Texture2D> Pages, float Scale)>();
        foreach (SceneBake share in _scenes)
        {
            if (!share.Section.IsLoaded || share.Scene.Path is not { } path) continue;
            string folder = LightmapFolderOf(path);
            string staging = folder + "_baking";
            var pages = new List<Texture2D>();
            float scale = 1f;
            if (share.Atlas is { Targets.Length: > 0 } atlas)
            {
                string stagingAbs = Path.Combine(Project.Current.AssetsPath, staging);
                if (Directory.Exists(stagingAbs)) db.DeleteAssetFolder(staging);
                Directory.CreateDirectory(stagingAbs);

                (byte[][] encoded, scale) = EncodePages(atlas.Targets);
                for (int i = 0; i < atlas.Targets.Length; i++)
                {
                    LightmapTarget t = atlas.Targets[i];
                    string pageAbs = Path.Combine(stagingAbs, $"Lightmap-{i}.png");
                    WritePng(pageAbs, encoded[i], t.Width, t.Height);
                    WriteLightmapMeta(pageAbs);
                    if (AssetDatabase.Get<Texture2D>(db.ImportFile($"{staging}/Lightmap-{i}.png")) is not { } page)
                    {
                        // Placements address pages by index, so a missing page cannot be skipped.
                        Runtime.Debug.LogError($"[Lightmap] Could not import '{staging}/Lightmap-{i}.png', so the bake was not applied.");
                        foreach ((SceneBake _, string _, string done, List<Texture2D> _, float _) in written) db.DeleteAssetFolder(done);
                        db.DeleteAssetFolder(staging);
                        Cleanup();
                        return;
                    }
                    pages.Add(page);
                }
            }
            written.Add((share, folder, staging, pages, scale));
        }

        // Every probe bakes in one pass against the whole traced scene, then each scene keeps and tetrahedralizes its own.
        Status = "Baking probes…";
        List<Float3> allProbes = [.. written.SelectMany(w => w.Share.Probes)];
        Sh9Rgb[] sh = allProbes.Count > 0 ? baker.BakeProbes(allProbes, _settings.ProbeSamples, _settings.Bounces) : [];

        int probeStart = 0;
        foreach ((SceneBake share, string folder, string staging, List<Texture2D> pages, float scale) in written)
        {
            if (Directory.Exists(Path.Combine(Project.Current.AssetsPath, folder))) db.DeleteAssetFolder(folder);
            if (pages.Count > 0 && !db.MoveFolder(staging, folder))
                Runtime.Debug.LogWarning($"[Lightmap] Could not move the new pages to '{folder}', they stay in '{staging}'.");

            SceneLighting lighting = share.Section.Lighting;
            lighting.Lightmaps = pages;
            lighting.LightmapScale = scale;

            // Where each baked surface landed, against the object it is on. A rebake replaces the whole set, so a renderer
            // that is no longer baked simply has no placement.
            lighting.Placements.Clear();
            if (share.Atlas is { } atlas)
            {
                for (int i = 0; i < share.Renderers.Count && i < atlas.Instances.Length; i++)
                {
                    if (share.Renderers[i] is not Component renderer || renderer.IsNotValid() || renderer.GameObject.IsNotValid()) continue;
                    BakeInstance inst = atlas.Instances[i];
                    lighting.Placements[renderer.GameObject.Identifier] = new LightmapPlacement
                    {
                        Index = atlas.Placements[i].AtlasIndex,
                        ScaleOffset = new Float4(inst.UVScale.X, inst.UVScale.Y, inst.UVOffset.X, inst.UVOffset.Y),
                    };
                }
            }

            int count = share.Probes.Count;
            var probeSH = new SphericalHarmonicsL2[count];
            for (int i = 0; i < count; i++) probeSH[i] = ConvertSH(sh[probeStart + i]);
            probeStart += count;
            ProbeTetrahedralizer.Result tet = count > 0 ? ProbeTetrahedralizer.Build(share.Probes) : new ProbeTetrahedralizer.Result { Tetrahedra = [], Neighbours = [] };
            lighting.ProbePositions = [.. share.Probes];
            lighting.ProbeSH = probeSH;
            lighting.ProbeTetrahedra = tet.Tetrahedra;
            lighting.ProbeTetNeighbours = tet.Neighbours;

            EditorSceneManager.MarkDirty(share.Scene);
        }
        world.InvalidateProbeVolume();

        Runtime.Debug.Log($"[Lightmap] Baked {written.Count} scene(s): {written.Sum(w => w.Pages.Count)} lightmap page(s), "
            + $"{_scenes.Sum(share => share.Renderers.Count)} renderer(s), {allProbes.Count} probe(s). Save the scenes to keep the result.");
        Cleanup();
        Status = "Done";
    }

    private void Cleanup()
    {
        _baker?.Dispose();
        _baker = null;
        _job = null;
        _world = null;
        _scenes.Clear();
        _texCache.Clear();
        Progress = 0f;
    }

    // ---- helpers ----

    private bool TryBuildBakeMesh(BakeScene bake, Mesh? mesh, IReadOnlyList<Material> materials,
                                  string nameKey, ref int matCounter, out BakeMesh result, out bool hasLightmapUV)
    {
        result = null!;
        hasLightmapUV = false;
        if (mesh.IsNotValid() || mesh.VertexCount == 0) return false;

        // Lightmap UV: prefer the dedicated UV2 set (generated at import), fall back to the primary
        // UVs as a fallback. A mesh with no UVs at all still builds (as an occluder); zeros fill the
        // unused layers.
        Float2[]? lmUV = mesh.HasUV2 ? mesh.UV2 : (mesh.HasUV ? mesh.UV : null);
        hasLightmapUV = lmUV != null;
        Float2[] uv0 = mesh.HasUV ? mesh.UV : (lmUV ?? new Float2[mesh.VertexCount]);
        Float2[] uv1 = lmUV ?? new Float2[mesh.VertexCount];

        BakeMesh.Builder builder = bake.BeginMesh($"{nameKey}_{mesh.Name}")
            .AddVertices(mesh.Vertices, mesh.Normals)
            .AddUVLayer("UV0", uv0)
            .AddUVLayer("UV1", uv1);

        uint[] indices = mesh.Indices;
        int subCount = mesh.SubMeshCount;

        for (int s = 0; s < subCount; s++)
        {
            SubMeshDescriptor sub = mesh.GetSubMesh(s);
            int start = sub.IndexStart, count = sub.IndexCount;
            if (count <= 0) continue;
            int[] groupIdx = new int[count];
            for (int k = 0; k < count; k++) groupIdx[k] = (int)indices[start + k];

            // One BakeMaterial per (renderer, submesh) with the Prowl material's base colour.
            string matName = $"m{matCounter++}";
            BakeMaterial bmat = bake.CreateMaterial(matName);
            Material? pm = s < materials.Count ? materials[s] : materials.Count > 0 ? materials[^1] : default;
            if (pm.IsValid())
            {
                Color c = pm._properties.GetColor(pm._properties.HasColor("base_color") ? "base_color" : "_MainColor");
                bmat.DiffuseColor = new Float3((float)c.R, (float)c.G, (float)c.B);

                // Feed the diffuse albedo texture so bounced light picks up its colour.
                Texture2D? tex = pm._properties.GetTexture(pm._properties.HasTexture("base_color_map") ? "base_color_map" : "_MainTex");
                if (tex.IsValid()) tex.Load();
                if (tex is { IsLoaded: true })
                {
                    BakeTexture? bt = GetOrCreateDiffuse(bake, tex);
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
        if (key != Guid.Empty && _texCache.TryGetValue(key, out BakeTexture? cached)) return cached;

        // Photonic keys textures by name and rejects duplicates, so give each one a unique name
        // (Texture2D names aren't unique many default to "New Texture").
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
            using Aperture.Image img = Aperture.Image.Load(abs, new DecodeOptions
            {
                TargetPixelFormat = PixelFormat.Rgba8,
                FlipVertically = true,
            });

            byte[] rgba = img.RootFrame.Pixels.ToArray();
            return bake.CreateTextureRGBA(name, img.Width, img.Height, rgba, 2.2f);
        }
        catch { return null; }
    }

    // A page imports as a Lightmap texture, which keeps its RGBM texels exact rather than compressing them.
    private static void WriteLightmapMeta(string pageAbs)
    {
        EchoObject settings = new TextureImporter().DefaultSettings() ?? EchoObject.NewCompound();
        settings[TextureImportSettings.Keys.Type] = new EchoObject((int)TextureImportType.Lightmap);
        MetaFile.Write(MetaFile.GetMetaPath(pageAbs), MetaFile.CreateNew(nameof(TextureImporter), defaultSettings: settings));
    }

    // Fallback for embedded/generated textures: read back the GPU pixels (RGBA8 or RGBA16).
    private static BakeTexture? TryReadback(BakeScene bake, Texture2D tex, string name)
    {
        int w = (int)tex.Width, h = (int)tex.Height;
        if (w <= 0 || h <= 0) return null;
        int n = w * h;
        byte[] rgba = new byte[n * 4];
        try
        {
            if (tex.GetMipRgba8() is byte[] expanded)
            {
                expanded.AsSpan(0, rgba.Length).CopyTo(rgba);
            }
            else if (tex.Format == Runtime.TextureFormat.RGBA16)
            {
                ushort[] tmp = new ushort[n * 4];
                tex.GetData<ushort>(tmp);
                for (int i = 0; i < n * 4; i++) rgba[i] = (byte)(tmp[i] >> 8);
            }
            else return null; // unsupported albedo format -> flat colour
        }
        catch { return null; }
        return bake.CreateTextureRGBA(name, w, h, rgba, tex.Srgb ? 2.2f : 1f);
    }

    // Graphite decodes an RGBM texel as rgb times alpha times this, before the binding's scale
    private const float RgbmRange = 8f;

    /// <summary>
    /// Encodes each page as RGBM over the brightest texel of the whole bake, so physical light of any brightness fits,
    /// with the scale Graphite multiplies the decoded texels by to get that light back.
    /// </summary>
    internal static (byte[][] Pages, float Scale) EncodePages(IReadOnlyList<LightmapTarget> targets)
    {
        float peak = 0f;
        foreach (LightmapTarget target in targets)
        {
            foreach (float value in target.Pixels)
            {
                if (float.IsFinite(value)) peak = MathF.Max(peak, value);
            }
        }
        float range = peak > 0f ? peak : RgbmRange;

        var pages = new byte[targets.Count][];
        for (int i = 0; i < pages.Length; i++)
            pages[i] = targets[i].ReadRGBM(range);
        return (pages, range / RgbmRange);
    }

    /// <summary>
    /// The colour a light bakes with, from lux for a directional light and candela for the rest. Photonic lights a
    /// white surface with it times the cosine, which is what Graphite reads from a lightmap: irradiance over pi.
    /// </summary>
    internal static Float3 BakeColor(Light light)
    {
        Color c = light.FinalColor;
        float intensity = light is LocalLight local ? local.AxisCandela : light.Intensity;
        return new Float3((float)c.R, (float)c.G, (float)c.B) * (intensity / Maths.PI);
    }

    internal static void AddLight(BakeScene bake, Light light)
    {
        Float3 color = BakeColor(light);
        Float4x4 xform = light.Transform.LocalToWorldMatrix;
        bool bakeDirect = light.BakeMode == LightBakeMode.Baked; // Mixed bakes indirect only

        Photonic.Scene.Lights.Light bl;
        switch (light)
        {
            case DirectionalLight:
                // Both treat the transform's +Z as the direction the light travels.
                bl = bake.CreateDirectionalLight(light.GameObject.Name, xform, color);
                break;
            case SpotLight spotLight:
                {
                    // Both take the whole cone angle, Prowl's in degrees and Photonic's in radians
                    float cone = Maths.Clamp(spotLight.SpotAngle, 1f, 179f);
                    Photonic.Scene.Lights.SpotLight spot = bake.CreateSpotLight(light.GameObject.Name, xform, color, spotLight.Range, cone * Maths.Deg2Rad);
                    spot.InnerConeAngle = cone * (1f - Maths.Clamp(spotLight.SpotSoftness, 0f, 1f)) * Maths.Deg2Rad;
                    bl = spot;
                    break;
                }
            case LocalLight local:
                // Photonic has no area lights, so rect, disk and tube lights bake as a point at their centre with their axis candela
                if (local is AreaLight)
                    Runtime.Debug.LogWarningOnce($"lightmap.arealight.{light.InstanceID}", $"[Lightmap] {light.GameObject.Name}: {light.GetType().Name}s bake as point lights.");
                bl = bake.CreatePointLight(light.GameObject.Name, xform, color, local.Range);
                break;
            default:
                return;
        }
        bl.BakeDirect = bakeDirect;
    }

    private static SphericalHarmonicsL2 ConvertSH(in Sh9Rgb s) => new SphericalHarmonicsL2
    {
        C0 = s.C0, C1 = s.C1, C2 = s.C2, C3 = s.C3, C4 = s.C4, C5 = s.C5, C6 = s.C6, C7 = s.C7, C8 = s.C8,
    };

    private static void WritePng(string absolutePath, byte[] rgba, int width, int height)
    {
        using Aperture.Image img = Aperture.Image.FromPixels(rgba, width, height, PixelFormat.Rgba8);

        // Photonic's atlas buffer is row-0-first (UV1 v=0 == row 0), but the texture import flips
        // on the way in. Pre-flip here so the two cancel and the sampled v matches the packed UV1.
        // Store raw bytes (linear RGBM); sampled linear and decoded in-shader.
        img.Save(absolutePath, new EncodeOptions
        {
            Format = ImageFormat.Png,
            FlipVertically = true,
        });
    }
}
