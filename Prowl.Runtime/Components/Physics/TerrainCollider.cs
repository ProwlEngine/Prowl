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
/// This component should be added to a GameObject with a TerrainComponent.
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
    /// Number of height samples per world unit.
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

        // Get local height from cache
        float localHeight = _heightCache[z * _cacheWidth + x];

        // Convert to world space height by adding terrain's Y position
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

        // Get the TerrainComponent
        _terrain = GetComponent<TerrainComponent>();
        if (_terrain == null)
        {
            Debug.LogError("TerrainCollider requires a TerrainComponent on the same GameObject.");
            Enabled = false;
            return;
        }

        // Initialize the heightmap cache
        UpdateHeightmapCache();

        // Register with the physics world
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

        // Clamp resolution to reasonable values
        HeightmapResolution = Maths.Clamp(HeightmapResolution, 32, 4096);

        if (Enabled)
        {
            // Rebuild cache and re-register
            UpdateHeightmapCache();
            UnregisterFromPhysics();
            RegisterWithPhysics();
        }
    }

    /// <summary>
    /// Updates the heightmap cache by sampling the terrain's heightmap texture.
    /// This should be called when the terrain's heightmap changes.
    /// </summary>
    public void UpdateHeightmapCache()
    {
        if (_terrain == null || _terrain.Heightmap == null)
            return;

        // Use the specified resolution
        _cacheWidth = HeightmapResolution;
        _cacheHeight = HeightmapResolution;
        _heightCache = new float[_cacheWidth * _cacheHeight];

        // Sample the heightmap texture
        if (_terrain.Heightmap is Texture2D heightmapTexture)
        {
            // Get the heightmap data
            // Assuming RGBA format with height stored in the red channel
            int texWidth = (int)heightmapTexture.Width;
            int texHeight = (int)heightmapTexture.Height;
            var pixels = new byte[texWidth * texHeight * 4]; // RGBA

            unsafe
            {
                fixed (byte* ptr = pixels)
                {
                    heightmapTexture.GetDataPtr(ptr);
                }
            }

            // Sample the heightmap with bilinear filtering
            for (int z = 0; z < _cacheHeight; z++)
            {
                for (int x = 0; x < _cacheWidth; x++)
                {
                    // Convert grid coordinates to texture coordinates
                    float u = x / (float)(_cacheWidth - 1);
                    float v = z / (float)(_cacheHeight - 1);

                    // Convert UV to pixel coordinates
                    float px = u * (texWidth - 1);
                    float py = v * (texHeight - 1);

                    // Bilinear interpolation
                    int x0 = (int)Maths.Floor(px);
                    int y0 = (int)Maths.Floor(py);
                    int x1 = Maths.Min(x0 + 1, texWidth - 1);
                    int y1 = Maths.Min(y0 + 1, texHeight - 1);

                    float fx = px - x0;
                    float fy = py - y0;

                    // Sample four pixels (using red channel for height)
                    float h00 = pixels[(y0 * texWidth + x0) * 4] / 255.0f;
                    float h10 = pixels[(y0 * texWidth + x1) * 4] / 255.0f;
                    float h01 = pixels[(y1 * texWidth + x0) * 4] / 255.0f;
                    float h11 = pixels[(y1 * texWidth + x1) * 4] / 255.0f;

                    // Bilinear interpolation
                    float h0 = h00 * (1 - fx) + h10 * fx;
                    float h1 = h01 * (1 - fx) + h11 * fx;
                    float normalizedHeight = h0 * (1 - fy) + h1 * fy;

                    // Scale by terrain height
                    float worldHeight = normalizedHeight * _terrain.TerrainHeight;

                    _heightCache[z * _cacheWidth + x] = worldHeight;
                }
            }
        }
        else
        {
            // No valid heightmap, fill with zeros
            Array.Fill(_heightCache, 0f);
        }
    }

    /// <summary>
    /// Registers this terrain collider with the physics world.
    /// </summary>
    private void RegisterWithPhysics()
    {
        if (_isRegistered || GameObject?.Scene?.Physics == null)
            return;

        var physics = GameObject.Scene.Physics;

        // Calculate bounding box in world space
        Float3 terrainPos = Transform.Position;
        float terrainSize = _terrain.TerrainSize;
        float terrainHeight = _terrain.TerrainHeight;

        // Calculate cell size in world units
        float cellSize = (float)(terrainSize / (_cacheWidth - 1));

        // Terrain origin is the world position of the terrain
        JVector terrainOrigin = new JVector(
            (float)terrainPos.X,
            (float)terrainPos.Y,
            (float)terrainPos.Z
        );

        // Bounding box: terrain spans from origin to origin + size
        JVector min = new JVector(
            (float)terrainPos.X,
            (float)terrainPos.Y - terrainHeight * 0.1f, // Add small margin below
            (float)terrainPos.Z
        );

        JVector max = new JVector(
            (float)(terrainPos.X + terrainSize),
            (float)(terrainPos.Y + terrainHeight),
            (float)(terrainPos.Z + terrainSize)
        );

        JBoundingBox boundingBox = new JBoundingBox(min, max);

        // Create the heightmap proxy
        _heightmapProxy = new TerrainHeightmapProxy(this, boundingBox, terrainOrigin, cellSize);

        // Create the collision filter
        _collisionFilter = new TerrainCollisionFilter(physics.World, _heightmapProxy, this, terrainOrigin, cellSize);

        // Register the terrain with the physics system
        physics.RegisterTerrain(_heightmapProxy, _collisionFilter);

        _isRegistered = true;
    }

    /// <summary>
    /// Unregisters this terrain collider from the physics world.
    /// </summary>
    private void UnregisterFromPhysics()
    {
        if (!_isRegistered || GameObject?.Scene?.Physics == null)
            return;

        var physics = GameObject.Scene.Physics;

        // Unregister from the physics system
        physics.UnregisterTerrain(_heightmapProxy, _collisionFilter);

        _heightmapProxy = null;
        _collisionFilter = null;
        _isRegistered = false;
    }

    /// <summary>
    /// Gets the world-space height at the specified world position.
    /// </summary>
    /// <param name="worldX">World X coordinate.</param>
    /// <param name="worldZ">World Z coordinate.</param>
    /// <returns>The world-space height at this position, or 0 if outside terrain bounds.</returns>
    public float GetWorldHeight(float worldX, float worldZ)
    {
        if (_terrain == null || _heightCache == null)
            return 0;

        // Convert world position to local terrain coordinates
        Float3 localPos = Transform.InverseTransformPoint(new Float3(worldX, 0, worldZ));

        // Convert to grid coordinates
        float terrainSize = _terrain.TerrainSize;
        float gridX = (float)(localPos.X / terrainSize * (_cacheWidth - 1));
        float gridZ = (float)(localPos.Z / terrainSize * (_cacheHeight - 1));

        // Bilinear interpolation
        int x0 = (int)Maths.Floor(gridX);
        int z0 = (int)Maths.Floor(gridZ);
        int x1 = x0 + 1;
        int z1 = z0 + 1;

        float tx = gridX - x0;
        float tz = gridZ - z0;

        if (!TryGetHeight(x0, z0, out float h00)) return 0;
        if (!TryGetHeight(x1, z0, out float h10)) return 0;
        if (!TryGetHeight(x0, z1, out float h01)) return 0;
        if (!TryGetHeight(x1, z1, out float h11)) return 0;

        // Bilinear interpolation
        float h0 = h00 * (1 - tx) + h10 * tx;
        float h1 = h01 * (1 - tx) + h11 * tx;
        float localHeight = h0 * (1 - tz) + h1 * tz;

        // Convert to world space
        return (float)(localHeight + Transform.Position.Y);
    }
}
