// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Jitter2.LinearMath;

using Prowl.Runtime.Terrain;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Provides physics collision for terrain using heightmap-based collision detection.
/// Samples height data directly from the TerrainData asset (no separate cache).
/// </summary>
[RequireComponent(typeof(TerrainComponent))]
[AddComponentMenu("Physics/Colliders/Terrain Collider")]
[ComponentIcon("\uf6fc")] // Mountain
public class TerrainCollider : MonoBehaviour, ITerrainHeightProvider
{
    private TerrainComponent _terrain;
    private TerrainHeightmapProxy _heightmapProxy;
    private TerrainCollisionFilter _collisionFilter;
    private bool _isRegistered;
    private uint _lastTransformVersion;

    #region ITerrainHeightProvider samples directly from TerrainData

    public int Width => _terrain.IsValid() && _terrain.Data.Res.IsValid() ? _terrain.Data.Res.HeightmapResolution : 0;
    public int Height => _terrain.IsValid() && _terrain.Data.Res.IsValid() ? _terrain.Data.Res.HeightmapResolution : 0;

    /// <summary>
    /// The grid placement, refreshed from the transform once per frame rather than read per query. The
    /// broad phase samples this from Jitter's worker threads, and Transform's cached world matrix is not
    /// safe to touch concurrently; caching also keeps the per-sample height lookup off it entirely.
    /// <para/>
    /// The grid is defined in terrain-local space, so position, rotation and scale all live in these
    /// matrices rather than being baked into the samples. That is what lets terrain be turned to any
    /// orientation and still collide correctly.
    /// </summary>
    public Float4x4 LocalToWorld => _localToWorld;

    public Float4x4 WorldToLocal => _worldToLocal;

    public float CellSize => _cellSize;

    public JBoundingBox WorldBounds => _worldBounds;

    private Float4x4 _localToWorld = Float4x4.Identity;
    private Float4x4 _worldToLocal = Float4x4.Identity;
    private float _cellSize;
    private JBoundingBox _worldBounds;

    private void RefreshPlacement()
    {
        _localToWorld = Transform.LocalToWorldMatrix;
        _worldToLocal = Transform.WorldToLocalMatrix;

        var data = _terrain.IsValid() ? _terrain.Data.Res : null;
        if (data.IsNotValid() || data.HeightmapResolution < 2)
        {
            _cellSize = 0.0f;
            _worldBounds = new JBoundingBox(JVector.Zero, JVector.Zero);
            return;
        }

        _cellSize = data.Size / (data.HeightmapResolution - 1);

        // Local bounds spanning the full possible height range (sculpting can never leave them), then
        // every corner through the matrix, so a rotated terrain still gets bounds that enclose it.
        var localMin = new Float3(0, -data.Height * 0.1f, 0);
        var localMax = new Float3(data.Size, data.Height, data.Size);

        Float3 worldMin = new(float.MaxValue), worldMax = new(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Float3(
                (i & 1) == 0 ? localMin.X : localMax.X,
                (i & 2) == 0 ? localMin.Y : localMax.Y,
                (i & 4) == 0 ? localMin.Z : localMax.Z);

            Float3 world = Float4x4.TransformPoint(corner, _localToWorld);
            worldMin = Maths.Min(worldMin, world);
            worldMax = Maths.Max(worldMax, world);
        }

        _worldBounds = new JBoundingBox(
            new JVector(worldMin.X, worldMin.Y, worldMin.Z),
            new JVector(worldMax.X, worldMax.Y, worldMax.Z));
    }

    /// <summary>Height in terrain-local units. Placement and scale come from <see cref="LocalToWorld"/>.</summary>
    public bool TryGetHeight(int x, int z, out float height)
    {
        height = 0;
        var data = _terrain.IsValid() ? _terrain.Data.Res : null;
        if (data == null || data.Heights == null) return false;

        int res = data.HeightmapResolution;
        if (x < 0 || x >= res || z < 0 || z >= res) return false;

        height = (float)data.Heights[z * res + x] / TerrainData.kMaxHeight * data.Height;
        return true;
    }

    public bool IsValidCell(int x, int z)
    {
        int res = _terrain.IsValid() && _terrain.Data.Res.IsValid() ? _terrain.Data.Res.HeightmapResolution : 0;
        return x >= 0 && x < res - 1 && z >= 0 && z < res - 1;
    }

    public bool IsCellHole(int x, int z)
    {
        var data = _terrain.IsValid() ? _terrain.Data.Res : null;
        return data != null && data.IsCellHole(x, z);
    }

    #endregion

    public override void OnEnable()
    {
        base.OnEnable();

        _terrain = GetComponent<TerrainComponent>();
        if (_terrain == null)
        {
            Debug.LogError("TerrainCollider requires a TerrainComponent on the same GameObject.");
            Enabled = false;
            return;
        }

        RegisterWithPhysics();
    }

    public override void OnDisable()
    {
        base.OnDisable();
        UnregisterFromPhysics();
    }

    public override void OnValidate()
    {
        base.OnValidate();
        if (Enabled)
        {
            UnregisterFromPhysics();
            RegisterWithPhysics();
        }
    }

    private void RegisterWithPhysics()
    {
        var scene = GameObject.IsValid() ? GameObject.Scene : null;
        if (_isRegistered || scene.IsNotValid() || scene.Physics == null || _terrain == null)
            return;

        // Collider registration happens once: block-load the terrain data (prioritized) so a
        // transient null from async streaming doesn't leave the terrain without collision.
        _terrain.Data.EnsureLoaded();
        var terrainData = _terrain.Data.Res;
        if (terrainData == null) return;

        var physics = GameObject.Scene.Physics;

        RefreshPlacement();

        _heightmapProxy = new TerrainHeightmapProxy(this);
        _collisionFilter = new TerrainCollisionFilter(physics.World, _heightmapProxy, this);

        physics.RegisterTerrain(_heightmapProxy, _collisionFilter, this);
        _lastTransformVersion = ComputeWorldTransformVersion();
        _isRegistered = true;
    }

    public override void Update()
    {
        if (!_isRegistered) return;

        // Re-read the placement and re-fit the broad-phase bounds when the terrain (or an ancestor)
        // moves. Doing it here, on the main thread, is what lets the filter sample it from workers.
        uint version = ComputeWorldTransformVersion();
        if (version == _lastTransformVersion) return;

        _lastTransformVersion = version;
        RefreshPlacement();

        var scene = GameObject.IsValid() ? GameObject.Scene : null;
        if (scene.IsValid()) scene.Physics?.RefreshTerrain(_heightmapProxy);
    }

    private uint ComputeWorldTransformVersion()
    {
        uint v = 17;
        for (Transform t = Transform; t != null; t = t.Parent)
            v = v * 31 + t.Version;
        return v;
    }

    private void UnregisterFromPhysics()
    {
        var scene = GameObject.IsValid() ? GameObject.Scene : null;
        if (!_isRegistered || scene.IsNotValid() || scene.Physics == null)
            return;

        var physics = GameObject.Scene.Physics;
        physics.UnregisterTerrain(_heightmapProxy, _collisionFilter);

        _heightmapProxy = null;
        _collisionFilter = null;
        _isRegistered = false;
    }

    /// <summary>
    /// Gets the world-space height at the specified world position.
    /// </summary>
    public float GetWorldHeight(float worldX, float worldZ)
    {
        var data = _terrain.IsValid() ? _terrain.Data.Res : null;
        if (data == null) return 0;

        Float3 localPos = Transform.InverseTransformPoint(new Float3(worldX, 0, worldZ));
        float u = (float)(localPos.X / data.Size);
        float v = (float)(localPos.Z / data.Size);

        // Sampled in local space, so the result has to come back out through the transform.
        Float3 localHit = new(localPos.X, data.GetInterpolatedHeight(u, v), localPos.Z);
        return Float4x4.TransformPoint(localHit, Transform.LocalToWorldMatrix).Y;
    }
}
