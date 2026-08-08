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

    public JVector Origin => _origin;

    public float CellSize => _cellSize;

    public JBoundingBox WorldBounds => _worldBounds;

    private JVector _origin;
    private float _cellSize;
    private float _heightScale = 1.0f;
    private JBoundingBox _worldBounds;

    private void RefreshPlacement()
    {
        Float3 p = Transform.Position;
        Float3 scale = Transform.LossyScale;
        _origin = new JVector((float)p.X, (float)p.Y, (float)p.Z);
        _heightScale = MathF.Abs((float)scale.Y);

        var data = _terrain.IsValid() ? _terrain.Data.Res : null;
        if (data.IsNotValid() || data.HeightmapResolution < 2)
        {
            _cellSize = 0.0f;
            _worldBounds = new JBoundingBox(_origin, _origin);
            return;
        }

        float span = data.Size * MathF.Abs((float)scale.X);
        float tall = data.Height * _heightScale;
        _cellSize = span / (data.HeightmapResolution - 1);

        // The stored heights span the full 0..Height range, so sculpting can never leave these bounds.
        _worldBounds = new JBoundingBox(
            new JVector(_origin.X, _origin.Y - tall * 0.1f, _origin.Z),
            new JVector(_origin.X + span, _origin.Y + tall, _origin.Z + span));
    }

    public bool TryGetHeight(int x, int z, out float height)
    {
        height = 0;
        var data = _terrain.IsValid() ? _terrain.Data.Res : null;
        if (data == null || data.Heights == null) return false;

        int res = data.HeightmapResolution;
        if (x < 0 || x >= res || z < 0 || z >= res) return false;

        // Height in terrain-local space, scaled by terrain height (16-bit storage)
        float normalizedHeight = (float)data.Heights[z * res + x] / TerrainData.kMaxHeight;
        height = normalizedHeight * data.Height * _heightScale + _origin.Y;
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

        WarnOnUnsupportedTransform();
        RefreshPlacement();

        _heightmapProxy = new TerrainHeightmapProxy(this);
        _collisionFilter = new TerrainCollisionFilter(physics.World, _heightmapProxy, this);

        physics.RegisterTerrain(_heightmapProxy, _collisionFilter, this);
        _lastTransformVersion = ComputeWorldTransformVersion();
        _isRegistered = true;
    }

    private void WarnOnUnsupportedTransform()
    {
        Float3 scale = Transform.LossyScale;
        bool squareCells = MathF.Abs(MathF.Abs((float)scale.X) - MathF.Abs((float)scale.Z)) <= 1e-4f;

        if (Transform.Rotation != Quaternion.Identity || !squareCells)
            Debug.LogError($"TerrainCollider on '{GameObject.Name}' needs an unrotated transform with equal X and Z scale. Collision will not line up with the rendered terrain.");
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

        return data.GetInterpolatedHeight(u, v) + (float)Transform.Position.Y;
    }
}
