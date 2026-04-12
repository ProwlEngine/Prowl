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
/// Reads height data from the TerrainData asset on the sibling TerrainComponent.
/// </summary>
[RequireComponent(typeof(TerrainComponent))]
[AddComponentMenu("Physics/Colliders/Terrain Collider")]
public class TerrainCollider : MonoBehaviour, ITerrainHeightProvider
{
    private TerrainComponent _terrain;
    private TerrainHeightmapProxy _heightmapProxy;
    private TerrainCollisionFilter _collisionFilter;
    private float[] _heightCache;
    private int _cacheWidth;
    private int _cacheHeight;
    private bool _isRegistered;

    /// <summary>
    /// Number of height samples for the physics cache.
    /// Higher values provide more accurate collision at the cost of memory and performance.
    /// </summary>
    public int HeightmapResolution = 128;

    #region ITerrainHeightProvider Implementation

    public int Width => _cacheWidth;
    public int Height => _cacheHeight;

    public bool TryGetHeight(int x, int z, out float height)
    {
        if (x < 0 || x >= _cacheWidth || z < 0 || z >= _cacheHeight || _heightCache == null || _terrain == null)
        {
            height = 0;
            return false;
        }

        float localHeight = _heightCache[z * _cacheWidth + x];
        height = localHeight + (float)Transform.Position.Y;
        return true;
    }

    public bool IsValidCell(int x, int z)
    {
        return x >= 0 && x < _cacheWidth - 1 && z >= 0 && z < _cacheHeight - 1;
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

        UpdateHeightmapCache();
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
        HeightmapResolution = Maths.Clamp(HeightmapResolution, 32, 4096);

        if (Enabled)
        {
            UpdateHeightmapCache();
            UnregisterFromPhysics();
            RegisterWithPhysics();
        }
    }

    /// <summary>
    /// Updates the heightmap cache by sampling heights from the TerrainData asset.
    /// </summary>
    public void UpdateHeightmapCache()
    {
        if (_terrain == null) return;

        var terrainData = _terrain.Data.Res;
        if (terrainData == null || terrainData.Heights == null)
            return;

        _cacheWidth = HeightmapResolution;
        _cacheHeight = HeightmapResolution;
        _heightCache = new float[_cacheWidth * _cacheHeight];

        // Sample heights from TerrainData using interpolation
        for (int z = 0; z < _cacheHeight; z++)
        {
            for (int x = 0; x < _cacheWidth; x++)
            {
                float u = x / (float)(_cacheWidth - 1);
                float v = z / (float)(_cacheHeight - 1);
                _heightCache[z * _cacheWidth + x] = terrainData.GetInterpolatedHeight(u, v);
            }
        }
    }

    private void RegisterWithPhysics()
    {
        if (_isRegistered || GameObject?.Scene?.Physics == null || _terrain == null)
            return;

        var terrainData = _terrain.Data.Res;
        if (terrainData == null) return;

        var physics = GameObject.Scene.Physics;

        Float3 terrainPos = Transform.Position;
        float terrainSize = terrainData.Size;
        float terrainHeight = terrainData.Height;

        float cellSize = (float)(terrainSize / (_cacheWidth - 1));

        JVector terrainOrigin = new JVector(
            (float)terrainPos.X,
            (float)terrainPos.Y,
            (float)terrainPos.Z);

        // Compute world-space AABB by transforming local corners
        Float3 localMin = new(0, -terrainHeight * 0.1f, 0);
        Float3 localMax = new(terrainSize, terrainHeight, terrainSize);
        Float3 wMin = new(float.MaxValue), wMax = new(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            Float3 corner = new(
                (i & 1) == 0 ? localMin.X : localMax.X,
                (i & 2) == 0 ? localMin.Y : localMax.Y,
                (i & 4) == 0 ? localMin.Z : localMax.Z);
            Float3 world = _terrain.TerrainToWorld(corner);
            wMin = new Float3(MathF.Min(wMin.X, world.X), MathF.Min(wMin.Y, world.Y), MathF.Min(wMin.Z, world.Z));
            wMax = new Float3(MathF.Max(wMax.X, world.X), MathF.Max(wMax.Y, world.Y), MathF.Max(wMax.Z, world.Z));
        }

        JBoundingBox boundingBox = new JBoundingBox(
            new JVector((float)wMin.X, (float)wMin.Y, (float)wMin.Z),
            new JVector((float)wMax.X, (float)wMax.Y, (float)wMax.Z));

        _heightmapProxy = new TerrainHeightmapProxy(this, boundingBox, terrainOrigin, cellSize);
        _collisionFilter = new TerrainCollisionFilter(physics.World, _heightmapProxy, this, terrainOrigin, cellSize);

        physics.RegisterTerrain(_heightmapProxy, _collisionFilter);
        _isRegistered = true;
    }

    private void UnregisterFromPhysics()
    {
        if (!_isRegistered || GameObject?.Scene?.Physics == null)
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
        if (_terrain == null) return 0;

        var terrainData = _terrain.Data.Res;
        if (terrainData == null) return 0;

        Float3 localPos = Transform.InverseTransformPoint(new Float3(worldX, 0, worldZ));
        float u = (float)(localPos.X / terrainData.Size);
        float v = (float)(localPos.Z / terrainData.Size);

        return terrainData.GetInterpolatedHeight(u, v) + (float)Transform.Position.Y;
    }
}
