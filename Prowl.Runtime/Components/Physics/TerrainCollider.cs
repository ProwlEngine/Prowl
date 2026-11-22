// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Jitter2.LinearMath;

using Prowl.Runtime.Resources;
using Prowl.Runtime.Terrain;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Provides physics collision for terrain using heightmap-based collision detection.
/// Samples heights directly from the TerrainData's package, the finest samples the terrain draws.
/// </summary>
[RequireComponent(typeof(TerrainComponent))]
[AddComponentMenu("Physics/Colliders/Terrain Collider")]
[ComponentIcon("\uf6fc")] // Mountain
public class TerrainCollider : Component, ITerrainHeightProvider
{
    private TerrainComponent _terrain;
    private TerrainHeightmapProxy _heightmapProxy;
    private TerrainCollisionFilter _collisionFilter;
    private bool _isRegistered;
    private uint _lastTransformVersion;

    // What the registration was built from. The triangle ID range is sized from the resolution, so a new
    // resolution or asset needs a fresh registration, while size and height only move the bounds
    private TerrainData? _registeredData;
    private int _registeredResolution;
    private float _registeredSize;
    private float _registeredHeight;

    #region ITerrainHeightProvider samples directly from TerrainData

    public int Width => _terrain.IsValid() && _terrain.Data.IsValid() ? _terrain.Data.HeightmapResolution : 0;
    public int Height => _terrain.IsValid() && _terrain.Data.IsValid() ? _terrain.Data.HeightmapResolution : 0;

    /// <summary>
    /// The grid placement, refreshed from the transform once per frame rather than read per query. The
    /// broad phase samples this from Jitter's worker threads, and Transform's cached world matrix is not
    /// safe to touch concurrently; caching also keeps the per-sample height lookup off it entirely.
    /// <para/>
    /// The grid is defined in terrain-local space and placed at the terrain's position, which like the
    /// terrain it draws is never turned or scaled.
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
        Float3 position = Transform.Position;
        _localToWorld = Float4x4.CreateTranslation(position);
        _worldToLocal = Float4x4.CreateTranslation(-position);

        TerrainData? data = _terrain.IsValid() ? _terrain.Data : null;
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
            worldMin.ToJitter(),
            worldMax.ToJitter());
    }

    /// <summary>Height in terrain-local units. Placement and scale come from <see cref="LocalToWorld"/>.</summary>
    public bool TryGetHeight(int x, int z, out float height)
    {
        height = 0;
        TerrainData? data = _terrain.IsValid() ? _terrain.Data : null;
        if (data.IsNotValid()) return false;

        int res = data.HeightmapResolution;
        if (x < 0 || x >= res || z < 0 || z >= res) return false;

        height = data.GetHeight(x, z);
        return true;
    }

    public bool IsValidCell(int x, int z)
    {
        int res = _terrain.IsValid() && _terrain.Data.IsValid() ? _terrain.Data.HeightmapResolution : 0;
        return x >= 0 && x < res - 1 && z >= 0 && z < res - 1;
    }

    public bool IsCellHole(int x, int z)
    {
        TerrainData? data = _terrain.IsValid() ? _terrain.Data : null;
        return data.IsValid() && data.IsCellHole(x, z);
    }

    #endregion

    public override void OnEnable()
    {
        base.OnEnable();

        _terrain = GetComponent<TerrainComponent>();
        if (_terrain.IsNotValid())
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
        SceneInstance? scene = GameObject.IsValid() ? GameObject.Scene : null;
        if (_isRegistered || scene.IsNotValid() || scene.Physics == null || _terrain.IsNotValid())
            return;

        // Collider registration happens once: block-load the terrain data (prioritized) so a
        // transient null from async streaming doesn't leave the terrain without collision.
        if (_terrain.Data is { } stored) stored.Load();
        TerrainData? terrainData = _terrain.Data;
        if (terrainData.IsNotValid()) return;

        PhysicsWorld physics = GameObject.Scene.Physics;

        RefreshPlacement();

        _heightmapProxy = new TerrainHeightmapProxy(this);
        _collisionFilter = new TerrainCollisionFilter(physics.World, _heightmapProxy, this);

        physics.RegisterTerrain(_heightmapProxy, _collisionFilter, this);
        _lastTransformVersion = ComputeWorldTransformVersion();
        _registeredData = terrainData;
        _registeredResolution = terrainData.HeightmapResolution;
        _registeredSize = terrainData.Size;
        _registeredHeight = terrainData.Height;
        _isRegistered = true;
    }

    public override void Update()
    {
        if (!_isRegistered)
        {
            // Picks the collider up once its terrain has data to collide with
            RegisterWithPhysics();
            return;
        }

        TerrainData? data = _terrain.IsValid() ? _terrain.Data : null;
        if (data != _registeredData || data.IsNotValid() || data.HeightmapResolution != _registeredResolution)
        {
            UnregisterFromPhysics();
            RegisterWithPhysics();
            return;
        }

        // Re-read the placement and re-fit the broad-phase bounds when the terrain (or an ancestor)
        // moves or the data is resized. Doing it here, on the main thread, is what lets the filter
        // sample it from workers.
        uint version = ComputeWorldTransformVersion();
        if (version == _lastTransformVersion && data.Size == _registeredSize && data.Height == _registeredHeight) return;

        _lastTransformVersion = version;
        _registeredSize = data.Size;
        _registeredHeight = data.Height;
        RefreshPlacement();

        SceneInstance? scene = GameObject.IsValid() ? GameObject.Scene : null;
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
        SceneInstance? scene = GameObject.IsValid() ? GameObject.Scene : null;
        if (!_isRegistered || scene.IsNotValid() || scene.Physics == null)
            return;

        PhysicsWorld physics = GameObject.Scene.Physics;
        physics.UnregisterTerrain(_heightmapProxy, _collisionFilter);

        _heightmapProxy = null;
        _collisionFilter = null;
        _registeredData = null;
        _isRegistered = false;
    }

    /// <summary>
    /// Gets the world-space height at the specified world position.
    /// </summary>
    public float GetWorldHeight(float worldX, float worldZ)
    {
        TerrainData? data = _terrain.IsValid() ? _terrain.Data : null;
        if (data.IsNotValid()) return 0;

        Float3 position = Transform.Position;
        return position.Y + data.GetInterpolatedHeight((worldX - position.X) / data.Size, (worldZ - position.Z) / data.Size);
    }
}
