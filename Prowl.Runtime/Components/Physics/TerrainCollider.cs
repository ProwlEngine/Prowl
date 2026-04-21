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

    #region ITerrainHeightProvider — samples directly from TerrainData

    public int Width => _terrain?.Data.Res?.HeightmapResolution ?? 0;
    public int Height => _terrain?.Data.Res?.HeightmapResolution ?? 0;

    public bool TryGetHeight(int x, int z, out float height)
    {
        height = 0;
        var data = _terrain?.Data.Res;
        if (data == null || data.Heights == null) return false;

        int res = data.HeightmapResolution;
        if (x < 0 || x >= res || z < 0 || z >= res) return false;

        // Height in terrain-local space, scaled by terrain height
        float normalizedHeight = data.Heights[z * res + x];
        height = normalizedHeight * data.Height + (float)Transform.Position.Y;
        return true;
    }

    public bool IsValidCell(int x, int z)
    {
        int res = _terrain?.Data.Res?.HeightmapResolution ?? 0;
        return x >= 0 && x < res - 1 && z >= 0 && z < res - 1;
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
        if (_isRegistered || GameObject?.Scene?.Physics == null || _terrain == null)
            return;

        var terrainData = _terrain.Data.Res;
        if (terrainData == null) return;

        var physics = GameObject.Scene.Physics;

        Float3 terrainPos = Transform.Position;
        float terrainSize = terrainData.Size;
        float terrainHeight = terrainData.Height;
        int res = terrainData.HeightmapResolution;

        float cellSize = terrainSize / (res - 1);

        JVector terrainOrigin = new JVector(
            (float)terrainPos.X, (float)terrainPos.Y, (float)terrainPos.Z);

        // World-space AABB from transformed local corners
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

        JBoundingBox boundingBox = new(
            new JVector((float)wMin.X, (float)wMin.Y, (float)wMin.Z),
            new JVector((float)wMax.X, (float)wMax.Y, (float)wMax.Z));

        _heightmapProxy = new TerrainHeightmapProxy(this, boundingBox, terrainOrigin, cellSize);
        _collisionFilter = new TerrainCollisionFilter(physics.World, _heightmapProxy, this, terrainOrigin, cellSize);

        physics.RegisterTerrain(_heightmapProxy, _collisionFilter, this, terrainOrigin, cellSize);
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
        var data = _terrain?.Data.Res;
        if (data == null) return 0;

        Float3 localPos = Transform.InverseTransformPoint(new Float3(worldX, 0, worldZ));
        float u = (float)(localPos.X / data.Size);
        float v = (float)(localPos.Z / data.Size);

        return data.GetInterpolatedHeight(u, v) + (float)Transform.Position.Y;
    }
}
