// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Threading;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.Rendering;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using GMaterial = Prowl.Graphite.Rendering.Material;
using GShader = Prowl.Graphite.Rendering.Shader;
using GTexture = Prowl.Graphite.Texture;
using SamplerDescription = Prowl.Graphite.SamplerDescription;
using Material = Prowl.Runtime.Resources.Material;
using Mesh = Prowl.Runtime.Resources.Mesh;

namespace Prowl.Runtime.Terrain;

/// <summary>
/// Quadtree LOD terrain drawn through a render provider. Every view picks its own chunk LODs, grass cascades and
/// details around its camera on the render thread, and the heightmap is sampled in the vertex shader for displacement.
/// References a TerrainData asset for height/splat/layer configuration.
/// </summary>
[ExecuteAlways]
[AddComponentMenu("Terrain/Terrain")]
[ComponentIcon("\uf6fc")] // Mountain
public class TerrainComponent : Component, IRenderSync
{
    #region Configuration

    /// <summary>The terrain data asset containing heightmap, splatmap, and layer configuration.</summary>
    public TerrainData? Data;

    /// <summary>Base material. Terrain draws with a child of it holding its own properties.</summary>
    public Material? Material;

    /// <summary>Maximum LOD subdivision levels for the quadtree.</summary>
    public int MaxLODLevel = 4;

    /// <summary>Resolution of the base mesh grid (vertices per side).</summary>
    public int MeshResolution = 32;

    /// <summary>
    /// LOD quality multiplier. Controls the distance at which the quadtree subdivides.
    /// 1.0 = default, higher = more detail at distance, lower = less detail.
    /// </summary>
    public float LODQuality = 1f;

    /// <summary>Detail material override. If null, uses the built-in Grass material.</summary>
    public Material? DetailMaterial;

    /// <summary>How far details are drawn, in world units.</summary>
    public float DetailDistance = 150f;

    /// <summary>
    /// Detail cells per metre. 1 gives one cell every metre, so one blade per square metre where
    /// the detail map is painted solid; 2 gives four. Cost scales with the square of this.
    /// </summary>
    public float DetailDensity = 2f;

    /// <summary>
    /// Quality bands between the camera and <see cref="DetailDistance"/>, split evenly. Each band
    /// out doubles its cell size, drawing a quarter as many blades as the one before it.
    /// </summary>
    public int DetailCascades = 4;

    /// <summary>Maximum render distance for trees.</summary>
    public float TreeDistance = 500f;

    #endregion

    /// <summary>Shadow casting of the terrain, its details and its trees.</summary>
    public ShadowCasting ShadowCasting = ShadowCasting.On;
    public bool ReceiveShadows = true;

    /// <summary>The renderable flags the terrain, its details and its trees draw with.</summary>
    internal RenderableFlags RenderFlags => RenderableState.FlagsFor(GameObject, false, ReceiveShadows) | ShadowCasting.ToFlags();

    #region State

    [NonSerialized, NotHeld] private Mesh? _baseMesh;
    [NonSerialized, NotHeld] private int _baseMeshResolution;
    [NonSerialized, NotHeld] private TerrainProvider? _provider;
    [NonSerialized, NotHeld] private TerrainMaterial? _material;
    [NonSerialized, NotHeld] private TerrainMeshDetailRenderer? _meshDetailRenderer;
    [NonSerialized, NotHeld] private TerrainDetailRenderer? _detailRenderer;
    [NonSerialized, NotHeld] private TerrainTreeRenderer? _treeRenderer;
    [NonSerialized, NotHeld] private readonly List<(int ViewId, Double3 Position)> _cameras = [];

    // Wind zones nearest the camera, refreshed each frame
    [NonSerialized, NotHeld] private readonly WindZone?[] _windZones = new WindZone?[WindZone.kMaxShaderZones];
    [NonSerialized] private int _windZoneCount;

    [NonSerialized] private static Material? s_defaultTerrainMat;
    [NonSerialized] private static Material? s_defaultDetailMat;

    private static readonly string[] s_splatNames = ["_Splatmap0", "_Splatmap1"];
    private static readonly string[] s_layerNames = LayerNames("");
    private static readonly string[] s_layerNormalNames = LayerNames("Normal");
    private static readonly string[] s_layerTilingNames = LayerNames("Tiling");
    private static readonly string[] s_layerRoughnessNames = LayerNames("Roughness");
    private static readonly string[] s_layerMetallicNames = LayerNames("Metallic");

    private static string[] LayerNames(string suffix)
    {
        string[] names = new string[8];
        for (int i = 0; i < names.Length; i++) names[i] = $"_Layer{i}{suffix}";
        return names;
    }

    #endregion

    #region Brush Preview (set by editor each frame)

    [NonSerialized] public Float2 BrushPosition;
    [NonSerialized] public float BrushRadius;
    [NonSerialized] public float BrushFalloff;
    [NonSerialized] public bool BrushVisible;

    #endregion

    #region Public Accessors

    /// <summary>Invalidate cached mesh-detail patches (call after painting detail density).</summary>
    public void InvalidateDetailCache() => _meshDetailRenderer?.InvalidateCache();

    /// <summary>Shortcut to terrain size from the data asset.</summary>
    public float TerrainSize { get { TerrainData? d = Data; return d.IsValid() ? d.Size : 1024f; } }

    /// <summary>Shortcut to terrain height from the data asset.</summary>
    public float TerrainHeight { get { TerrainData? d = Data; return d.IsValid() ? d.Height : 100f; } }

    /// <summary>Transform a point from terrain-local space to world space.</summary>
    public Float3 TerrainToWorld(Float3 localPoint) =>
        Float4x4.TransformPoint(localPoint, Transform.LocalToWorldMatrix);

    /// <summary>Transform a point from world space to terrain-local space.</summary>
    public Float3 WorldToTerrain(Float3 worldPoint) =>
        Float4x4.TransformPoint(worldPoint, Transform.WorldToLocalMatrix);

    #endregion

    #region Rendering

    [System.NonSerialized, NotHeld] private ProbeAnchorHandle _probeAnchor;

    /// <summary>Where the light probes light the terrain's draws, the middle of its bounds.</summary>
    internal ProbeAnchorHandle ProbeAnchor => _probeAnchor;

    private void SyncProbeAnchor(RenderWorld world)
    {
        TerrainData? data = Data;
        float size = data.IsValid() ? data.Size : 0f, height = data.IsValid() ? data.Height : 0f;
        Float3 centre = Float4x4.TransformPoint(new Float3(size * 0.5f, height * 0.5f, size * 0.5f), Transform.LocalToWorldMatrix);
        Double3 position = new(centre.X, centre.Y, centre.Z);
        if (!_probeAnchor.IsValid || !world.IsAlive(_probeAnchor)) _probeAnchor = world.AddProbeAnchor(position);
        else world.SetProbeAnchor(_probeAnchor, position);
    }

    bool IRenderSync.Sync(RenderWorld world)
    {
        SyncProbeAnchor(world);
        _provider ??= new TerrainProvider();
        world.Providers.Add(_provider);
        _provider.GetCameras(_cameras);

        TerrainFrame? frame = null;
        try { frame = BuildFrame(); }
        finally { _provider.Publish(frame); }
        return false;
    }

    void IRenderSync.Detach(RenderWorld world)
    {
        if (_probeAnchor.IsValid && world.IsAlive(_probeAnchor)) world.RemoveProbeAnchor(_probeAnchor);
        _probeAnchor = default;
        if (_provider != null)
        {
            world.Providers.Remove(_provider);
            _provider.Publish(null);
            _provider = null;
        }
        _material?.Release();
        _material = null;
        _detailRenderer?.Dispose();
        _detailRenderer = null;
        _meshDetailRenderer?.Dispose();
        _meshDetailRenderer = null;
        _treeRenderer = null;
        if (_baseMesh.IsValid()) _baseMesh.Dispose();
        _baseMesh = null;
    }

    private TerrainFrame? BuildFrame()
    {
        TerrainData? data = Data;
        if (data.IsNotValid()) return null;

        Material? source = Material;
        if (source.IsNotValid())
        {
            if (s_defaultTerrainMat.IsNotValid()) s_defaultTerrainMat = Resources.Material.LoadDefault(DefaultMaterial.Terrain);
            source = s_defaultTerrainMat;
        }

        _material ??= new TerrainMaterial();
        GMaterial? gpu = _material.Bind(source);
        GpuMesh? baseMesh = BaseMesh();
        if (gpu == null || baseMesh == null) return null;

        Float4x4 terrainToWorld = Transform.LocalToWorldMatrix;
        GameObject go = GameObject;
        var frame = new TerrainFrame
        {
            Frame = Graphics.FrameIndex,
            TerrainToWorld = (Double4x4)terrainToWorld,
            WorldToTerrain = Transform.WorldToLocalMatrix,
            MaxScale = MaxScale(terrainToWorld),
            Size = data.Size,
            MinHeight = -0.05f * data.Height,
            MaxHeight = 1.05f * data.Height,
            MaxLOD = MaxLODLevel,
            LODQuality = LODQuality,
            BaseMesh = baseMesh,
            Material = gpu,
            Flags = RenderFlags,
            LayerMask = RenderableState.LayerBit(go.LayerIndex),
            ObjectId = (uint)InstanceID,
            Probe = _probeAnchor,
        };

        ApplyTerrainProperties(_material, data);
        PickWindZones();

        Material? detailSource = DetailMaterial;
        if (detailSource.IsNotValid())
        {
            if (s_defaultDetailMat.IsNotValid()) s_defaultDetailMat = Resources.Material.LoadDefault(DefaultMaterial.Grass);
            detailSource = s_defaultDetailMat;
        }

        _detailRenderer ??= new TerrainDetailRenderer();
        frame.Grass = _detailRenderer.Prepare(data, this, detailSource);

        _meshDetailRenderer ??= new TerrainMeshDetailRenderer();
        frame.MeshDetails = _meshDetailRenderer.Prepare(data, this, _cameras);

        _treeRenderer ??= new TerrainTreeRenderer();
        frame.Trees = _treeRenderer.Prepare(data, this);
        return frame;
    }

    private void ApplyTerrainProperties(TerrainMaterial material, TerrainData data)
    {
        material.SetKeyword("TERRAIN_8_LAYERS", data.LayerCount > 4);
        ApplyHeightfield(material, data);

        IReadOnlyList<Texture2D> splatmaps = data.GetSplatmapTextures();
        for (int i = 0; i < splatmaps.Count && i < s_splatNames.Length; i++)
            material.SetTexture(s_splatNames[i], splatmaps[i]);

        int layerCount = Math.Min(data.LayerCount, s_layerNames.Length);
        material.SetInt("_LayerCount", layerCount);
        for (int i = 0; i < layerCount; i++)
        {
            TerrainLayer layer = data.Layers[i];
            material.SetTexture(s_layerNames[i], layer.Albedo);
            material.SetTexture(s_layerNormalNames[i], layer.NormalMap);
            material.SetFloat(s_layerTilingNames[i], layer.Tiling);
            material.SetFloat(s_layerRoughnessNames[i], layer.Roughness);
            material.SetFloat(s_layerMetallicNames[i], layer.Metallic);
        }

        material.SetVector("_BrushPosition", new Float4(BrushPosition.X, BrushPosition.Y, 0, 0), 2);
        material.SetFloat("_BrushRadius", BrushRadius);
        material.SetFloat("_BrushFalloff", BrushFalloff);
        material.SetFloat("_BrushVisible", BrushVisible ? 1f : 0f);
    }

    /// <summary>The height field every terrain shader samples: heightmap, holes, size and interpolation.</summary>
    internal static void ApplyHeightfield(TerrainMaterial material, TerrainData data)
    {
        material.SetKeyword("TERRAIN_BICUBIC", data.Interpolation == TerrainInterpolation.Bicubic);
        material.SetTexture("_Heightmap", data.GetHeightmapTexture());
        Texture2D? holes = data.GetHolesTexture();
        material.SetTexture("_HolesMap", holes);
        material.SetInt("_HasHoles", holes.IsValid() ? 1 : 0);
        material.SetFloat("_TerrainSize", data.Size);
        material.SetFloat("_TerrainHeight", data.Height);
    }

    // Wind zones nearest the camera that drew most recently, or the terrain when none has
    private void PickWindZones()
    {
        Float3 near = _cameras.Count > 0 ? (Float3)_cameras[0].Position : Transform.Position;
        _windZoneCount = WindZone.GetNearest(near, _windZones);
    }

    /// <summary>Gives a grass material the wind zones picked this frame.</summary>
    internal void ApplyWindZones(TerrainMaterial material)
    {
        material.SetInt("_WindZoneCount", _windZoneCount);
        for (int i = 0; i < WindZone.kMaxShaderZones; i++)
        {
            Float4 sphere = Float4.Zero, parameters = Float4.Zero;
            WindZone? zone = i < _windZoneCount ? _windZones[i] : null;
            if (zone.IsValid())
            {
                Float3 centre = zone.Transform.Position;
                sphere = new Float4(centre.X, centre.Y, centre.Z, MathF.Max(zone.Radius, 1e-4f));
                parameters = new Float4(zone.WindMain, zone.Turbulence, zone.PulseMagnitude, zone.PulseFrequency);
            }
            material.SetVector(TerrainMaterial.WindSphereNames[i], sphere, 4);
            material.SetVector(TerrainMaterial.WindParamNames[i], parameters, 4);
        }
    }

    /// <summary>The Graphite material of a Prowl material ready to draw, loading it when it is not. Null while it cannot.</summary>
    internal static GMaterial? GpuMaterialOf(Material? material)
    {
        if (material.IsNotValid()) return null;
        if (!material.IsLoaded)
        {
            if (material.State == AssetState.Unloaded) _ = material.LoadAsync();
            return null;
        }
        return material.GpuMaterial;
    }

    /// <summary>The Graphite mesh of a Prowl mesh, uploaded and ready to draw, loading it when it is not.</summary>
    internal static GpuMesh? GpuMeshOf(Mesh? mesh)
    {
        if (mesh.IsNotValid()) return null;
        if (!mesh.IsLoaded)
        {
            if (mesh.State == AssetState.Unloaded) _ = mesh.LoadAsync();
            return null;
        }
        mesh.Upload();
        return mesh.GpuIfLoaded;
    }

    /// <summary>The largest scale a matrix gives any axis, for bounding spheres.</summary>
    internal static float MaxScale(Float4x4 m)
    {
        float x = Float3.Length(new Float3(m[0, 0], m[1, 0], m[2, 0]));
        float y = Float3.Length(new Float3(m[0, 1], m[1, 1], m[2, 1]));
        float z = Float3.Length(new Float3(m[0, 2], m[1, 2], m[2, 2]));
        return MathF.Max(x, MathF.Max(y, z));
    }

    private GpuMesh? BaseMesh()
    {
        if (_baseMesh.IsNotValid() || _baseMeshResolution != MeshResolution)
        {
            if (_baseMesh.IsValid()) _baseMesh.Dispose();
            _baseMesh = CreateBaseMesh(Math.Max(1, MeshResolution));
            _baseMeshResolution = MeshResolution;
        }
        _baseMesh.Upload();
        return _baseMesh.GpuIfLoaded;
    }

    #endregion

    #region Raycast

    /// <summary>
    /// Raycast against the terrain heightmap surface.
    /// Returns true if the ray hits the terrain, with the world-space hit point and terrain UV.
    /// </summary>
    public bool Raycast(Ray ray, out Float3 hitPoint, out Float2 terrainUV)
    {
        hitPoint = Float3.Zero;
        terrainUV = Float2.Zero;

        TerrainData? terrainData = Data;
        if (terrainData.IsNotValid() || terrainData.Heights == null) return false;

        float size = terrainData.Size;
        Float4x4 worldToLocal = Transform.WorldToLocalMatrix;

        // Transform ray into terrain-local space
        Float3 localOrigin = Float4x4.TransformPoint(ray.Origin, worldToLocal);
        Float3 localDir = Float3.Normalize(Float4x4.TransformPoint(ray.Origin + ray.Direction, worldToLocal) - localOrigin);

        float stepSize = size / terrainData.HeightmapResolution * 0.5f;
        float maxDist = size * 2f;
        float t = 0f;

        float prevDiff = GetHeightDiffLocal(localOrigin, terrainData);
        bool prevAbove = prevDiff > 0;

        while (t < maxDist)
        {
            t += stepSize;
            Float3 localPoint = localOrigin + localDir * t;
            float diff = GetHeightDiffLocal(localPoint, terrainData);
            bool above = diff > 0;

            if (!above && prevAbove)
            {
                float tLo = t - stepSize;
                float tHi = t;
                for (int i = 0; i < 8; i++)
                {
                    float tMid = (tLo + tHi) * 0.5f;
                    Float3 mid = localOrigin + localDir * tMid;
                    if (GetHeightDiffLocal(mid, terrainData) > 0)
                        tLo = tMid;
                    else
                        tHi = tMid;
                }

                Float3 localHit = localOrigin + localDir * ((tLo + tHi) * 0.5f);
                terrainUV = new Float2((float)(localHit.X / size), (float)(localHit.Z / size));
                hitPoint = TerrainToWorld(localHit);
                return true;
            }

            prevDiff = diff;
            prevAbove = above;
        }

        return false;
    }

    private static float GetHeightDiffLocal(Float3 localPoint, TerrainData data)
    {
        float u = (float)(localPoint.X / data.Size);
        float v = (float)(localPoint.Z / data.Size);
        if (u < 0 || u > 1 || v < 0 || v > 1) return 1f;
        float terrainY = data.GetInterpolatedHeight(u, v);
        return (float)localPoint.Y - terrainY;
    }

    #endregion

    #region Mesh Creation

    /// <summary>A unit grid in XZ the shader places as each chunk: (0, 0) to (1, 1), resolution quads a side.</summary>
    private static Mesh CreateBaseMesh(int resolution)
    {
        int vertexCount = (resolution + 1) * (resolution + 1);
        int indexCount = resolution * resolution * 6;

        Float3[] vertices = new Float3[vertexCount];
        Float2[] uvs = new Float2[vertexCount];
        uint[] indices = new uint[indexCount];

        for (int z = 0; z <= resolution; z++)
        {
            for (int x = 0; x <= resolution; x++)
            {
                int index = z * (resolution + 1) + x;
                float u = x / (float)resolution;
                float v = z / (float)resolution;
                vertices[index] = new Float3(u, 0, v);
                uvs[index] = new Float2(u, v);
            }
        }

        int triIndex = 0;
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                // Split along the (x, z) to (x+1, z+1) diagonal, the same one physics and the navmesh use
                uint a = (uint)(z * (resolution + 1) + x);
                uint b = a + 1;
                uint d = a + (uint)resolution + 1;
                uint c = d + 1;
                indices[triIndex++] = a;
                indices[triIndex++] = d;
                indices[triIndex++] = c;
                indices[triIndex++] = a;
                indices[triIndex++] = c;
                indices[triIndex++] = b;
            }
        }

        var mesh = new Mesh
        {
            Vertices = vertices,
            UV = uvs,
            Indices = indices,
        };
        mesh.RecalculateBounds();
        mesh.Upload();
        return mesh;
    }

    #endregion
}

/// <summary>What the render thread draws a terrain with for a frame, copied on the host and not changed after.</summary>
internal sealed class TerrainFrame
{
    public ulong Frame;
    public Double4x4 TerrainToWorld;
    public Float4x4 WorldToTerrain;
    public float MaxScale;
    public float Size, MinHeight, MaxHeight;
    public int MaxLOD;
    public float LODQuality;
    public GpuMesh BaseMesh = null!;
    public GMaterial Material = null!;
    public RenderableFlags Flags;
    public uint LayerMask;
    public uint ObjectId;
    public ProbeAnchorHandle Probe;
    public TerrainDetailRenderer.Frame? Grass;
    public TerrainMeshDetailRenderer.Frame? MeshDetails;
    public TerrainTreeRenderer.Frame? Trees;

    public DrawItem Item(Float4 custom0 = default, Float4 custom1 = default) => new()
    {
        Transform = TerrainToWorld,
        Custom0 = custom0,
        Custom1 = custom1,
        LayerMask = LayerMask,
        ObjectId = ObjectId,
        Flags = Flags,
        Probe = Probe,
    };

    /// <summary>A terrain local box as a world bounding sphere.</summary>
    public (Double3 Center, float Radius) Sphere(Float3 localMin, Float3 localMax)
    {
        Float3 centre = (localMin + localMax) * 0.5f;
        float radius = Float3.Length(localMax - localMin) * 0.5f * MaxScale;
        return (Double4x4.TransformPoint((Double3)centre, TerrainToWorld), radius);
    }

    public Float3 ToLocal(Double3 world) => Float4x4.TransformPoint((Float3)world, WorldToTerrain);
}

/// <summary>
/// Draws one terrain into each view on the render thread: the chunks its camera's quadtree picks, the grass cascades
/// around it, and the mesh details and trees it can see. Shadow views draw for the cameras that drew recently.
/// </summary>
internal sealed class TerrainProvider : RenderProvider
{
    // Views not drawn for this many frames lose their state
    private const ulong kForgetFrames = 120;

    private TerrainFrame? _pending;
    private TerrainFrame? _current;

    private readonly object _cameraLock = new();
    private readonly Dictionary<int, (Double3 Position, ulong Frame)> _cameras = [];

    // Render thread
    private readonly Dictionary<int, (TerrainQuadtree Tree, ulong Frame)> _trees = [];
    private readonly List<Float3> _positions = [];
    private readonly List<int> _stale = [];
    private ulong _prunedFrame = ulong.MaxValue;

    /// <summary>The frame the next SubmitFrame hands the render thread. Host thread.</summary>
    public void Publish(TerrainFrame? frame) => _pending = frame;

    public override void Extract() => Volatile.Write(ref _current, _pending);

    /// <summary>The cameras that drew the terrain recently, most recent first. Host thread.</summary>
    public void GetCameras(List<(int ViewId, Double3 Position)> into)
    {
        into.Clear();
        int newest = 0;
        ulong newestFrame = 0;
        lock (_cameraLock)
        {
            foreach (KeyValuePair<int, (Double3 Position, ulong Frame)> camera in _cameras)
            {
                if (camera.Value.Frame > newestFrame)
                {
                    newestFrame = camera.Value.Frame;
                    newest = into.Count;
                }
                into.Add((camera.Key, camera.Value.Position));
            }
        }
        if (newest > 0) (into[0], into[newest]) = (into[newest], into[0]);
    }

    public override void Cull(in ProviderView view, DrawItemSink sink)
    {
        TerrainFrame? frame = Volatile.Read(ref _current);
        if (frame == null) return;
        Prune(frame.Frame);

        FeatureView info = view.View;
        bool shadow = info.Kind == ViewKind.Shadow;
        bool camera = ((ViewKinds)(1 << (int)info.Kind) & ViewKinds.Cameras) != 0;

        _positions.Clear();
        lock (_cameraLock)
        {
            if (camera) _cameras[info.ViewId] = (view.Camera, frame.Frame);
            if (shadow)
                foreach ((Double3 position, ulong _) in _cameras.Values) _positions.Add(frame.ToLocal(position));
        }
        if (_positions.Count == 0) _positions.Add(frame.ToLocal(view.Camera));

        CullChunks(frame, info.ViewId, view, sink);
        frame.Grass?.Cull(frame, _positions, view, sink);
        frame.MeshDetails?.Cull(shadow ? -1 : info.ViewId, view, sink);
        frame.Trees?.Cull(_positions, view, sink);
    }

    private void CullChunks(TerrainFrame frame, int viewId, in ProviderView view, DrawItemSink sink)
    {
        if (!_trees.TryGetValue(viewId, out (TerrainQuadtree Tree, ulong Frame) entry)
            || MathF.Abs(entry.Tree.ChunkSize - frame.Size) > 0.01f || entry.Tree.MaxLODLevel != frame.MaxLOD)
        {
            entry.Tree = new TerrainQuadtree(Float3.Zero, frame.Size, frame.MaxLOD);
        }
        _trees[viewId] = (entry.Tree, frame.Frame);

        Span<Float3> flat = _positions.Count <= 16 ? stackalloc Float3[_positions.Count] : new Float3[_positions.Count];
        for (int i = 0; i < flat.Length; i++) flat[i] = new Float3(_positions[i].X, 0, _positions[i].Z);
        entry.Tree.Update(flat, frame.LODQuality);

        foreach (TerrainChunk chunk in entry.Tree.VisibleChunks)
        {
            Float3 min = new(chunk.Position.X, frame.MinHeight, chunk.Position.Z);
            Float3 max = new(chunk.Position.X + chunk.Size, frame.MaxHeight, chunk.Position.Z + chunk.Size);
            (Double3 centre, float radius) = frame.Sphere(min, max);
            if (!view.Sees(centre, radius)) continue;
            sink.Add(frame.BaseMesh, 0, frame.Material, frame.Item(new Float4(chunk.Position.X, chunk.Position.Z, chunk.Size, 0)));
        }
    }

    private void Prune(ulong frame)
    {
        if (frame == _prunedFrame) return;
        _prunedFrame = frame;

        _stale.Clear();
        foreach (KeyValuePair<int, (TerrainQuadtree Tree, ulong Frame)> tree in _trees)
            if (tree.Value.Frame + kForgetFrames < frame) _stale.Add(tree.Key);
        foreach (int key in _stale) _trees.Remove(key);

        _stale.Clear();
        lock (_cameraLock)
        {
            foreach (KeyValuePair<int, (Double3 Position, ulong Frame)> camera in _cameras)
                if (camera.Value.Frame + kForgetFrames < frame) _stale.Add(camera.Key);
            foreach (int key in _stale) _cameras.Remove(key);
        }
    }
}

/// <summary>
/// A Graphite child of a Prowl material that a terrain renderer sets its own values on, each given again only when it
/// changes. Values it never sets come from the source material, so edits to the asset show at once.
/// </summary>
internal sealed class TerrainMaterial
{
    public static readonly string[] WindSphereNames = ["_WindZoneSphere0", "_WindZoneSphere1", "_WindZoneSphere2", "_WindZoneSphere3"];
    public static readonly string[] WindParamNames = ["_WindZoneParams0", "_WindZoneParams1", "_WindZoneParams2", "_WindZoneParams3"];

    private GMaterial? _parent;
    private GShader? _shader;
    private readonly Dictionary<string, Float4> _vectors = [];
    private readonly Dictionary<string, int> _ints = [];
    private readonly Dictionary<string, (GTexture? Texture, SamplerDescription Sampler)> _textures = [];
    private readonly Dictionary<string, bool> _keywords = [];

    public GMaterial? Gpu { get; private set; }

    /// <summary>Follows a source material, making the child again when its Graphite material or shader changed. Null while it cannot draw.</summary>
    public GMaterial? Bind(Material? source)
    {
        GMaterial? parent = TerrainComponent.GpuMaterialOf(source);
        // A material still waiting for its shader draws the Invalid stand in, which has none of the terrain's properties
        if (parent == null || parent.IsDisposed || parent.Shader == Prowl.Runtime.Resources.Shader.GetDefault(DefaultShader.Invalid).GpuIfLoaded)
        {
            Release();
            return null;
        }
        if (Gpu == null || _parent != parent || _shader != parent.Shader)
        {
            Release();
            Gpu = parent.CreateChild();
            _parent = parent;
            _shader = parent.Shader;
        }
        return Gpu;
    }

    public void Release()
    {
        Graphics.DeferDispose(Gpu);
        Gpu = null;
        _parent = null;
        _shader = null;
        _vectors.Clear();
        _ints.Clear();
        _textures.Clear();
        _keywords.Clear();
    }

    public void SetFloat(string name, float value)
    {
        if (Gpu == null || (_vectors.TryGetValue(name, out Float4 old) && old.X == value)) return;
        _vectors[name] = new Float4(value, 0, 0, 0);
        Gpu.SetFloat(name, value);
    }

    /// <summary>Sets a vector property of 2 or 4 components.</summary>
    public void SetVector(string name, Float4 value, int components)
    {
        if (Gpu == null || (_vectors.TryGetValue(name, out Float4 old) && old == value)) return;
        _vectors[name] = value;
        if (components == 2) Gpu.SetFloat2(name, new Float2(value.X, value.Y));
        else Gpu.SetFloat4(name, value);
    }

    public void SetColor(string name, Color value)
    {
        Float4 v = new(value.R, value.G, value.B, value.A);
        if (Gpu == null || (_vectors.TryGetValue(name, out Float4 old) && old == v)) return;
        _vectors[name] = v;
        Gpu.SetColor(name, v);
    }

    public void SetInt(string name, int value)
    {
        if (Gpu == null || (_ints.TryGetValue(name, out int old) && old == value)) return;
        _ints[name] = value;
        Gpu.SetInt(name, value);
    }

    /// <summary>Sets a texture, the shader's default while it is null or not loaded.</summary>
    public void SetTexture(string name, Texture2D? texture)
    {
        if (Gpu == null) return;
        GTexture? gpu = null;
        SamplerDescription sampler = SamplerDescription.Linear;
        if (texture.IsValid())
        {
            if (texture.IsLoaded)
            {
                gpu = texture.GpuIfLoaded;
                sampler = texture.SamplerUnchecked;
            }
            else if (texture.State == AssetState.Unloaded)
            {
                _ = texture.LoadAsync();
            }
        }
        if (_textures.TryGetValue(name, out (GTexture? Texture, SamplerDescription Sampler) old) && old.Texture == gpu && old.Sampler.Equals(sampler)) return;
        _textures[name] = (gpu, sampler);
        Gpu.SetTexture(name, gpu, sampler);
    }

    public void SetKeyword(string axis, bool value)
    {
        if (Gpu == null || (_keywords.TryGetValue(axis, out bool old) && old == value)) return;
        _keywords[axis] = value;
        Gpu.SetKeyword(axis, value ? VariantSpace.ToggleValues[1] : VariantSpace.ToggleValues[0]);
    }
}
