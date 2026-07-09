// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using Prowl.Vector;

namespace Prowl.Runtime.Terrain;

/// <summary>
/// Manages the quadtree LOD system for terrain rendering.
/// Simple distance-based subdivision with a quality multiplier.
/// </summary>
public class TerrainQuadtree
{
    public TerrainChunk Root;
    public int MaxLODLevel;
    public float ChunkSize;
    public List<TerrainChunk> VisibleChunks = new();

    public TerrainQuadtree(Float3 origin, float terrainSize, int maxLOD)
    {
        MaxLODLevel = maxLOD;
        ChunkSize = terrainSize;
        Root = new TerrainChunk(origin, terrainSize, 0);
    }

    /// <summary>
    /// Updates the quadtree based on camera position.
    /// </summary>
    /// <param name="cameraPosition">Camera position in terrain-local space.</param>
    /// <param name="lodQuality">Quality multiplier for LOD distance thresholds.
    /// 1.0 = default, higher = more detail (subdivides further from camera), lower = less detail.</param>
    public void Update(Float3 cameraPosition, float lodQuality = 1f)
    {
        VisibleChunks.Clear();
        UpdateNode(Root, cameraPosition, lodQuality);
    }

    private void UpdateNode(TerrainChunk chunk, Float3 cameraPosition, float lodQuality)
    {
        // Calculate distance from camera to chunk center (XZ plane)
        Float3 chunkCenter = chunk.Position + new Float3(chunk.Size * 0.5f, 0, chunk.Size * 0.5f);
        float distanceToCamera = Float3.Distance(cameraPosition, chunkCenter);

        // Distance threshold scaled by quality multiplier
        // Higher quality = larger threshold = subdivides at greater distances
        float subdivideThreshold = chunk.Size * 1.5f * lodQuality;

        if (distanceToCamera < subdivideThreshold && chunk.LODLevel < MaxLODLevel)
        {
            // Subdivide and recurse into children
            if (chunk.Children == null)
                chunk.Subdivide();

            foreach (var child in chunk.Children)
                UpdateNode(child, cameraPosition, lodQuality);
        }
        else
        {
            // Merge children if they exist and camera is far enough
            if (chunk.Children != null)
            {
                // Hysteresis: merge at a slightly larger distance to prevent thrashing
                if (distanceToCamera > subdivideThreshold * 1.1f)
                    chunk.Merge();
                else
                {
                    // Still within hysteresis range - keep children
                    foreach (var child in chunk.Children)
                        UpdateNode(child, cameraPosition, lodQuality);
                    return;
                }
            }

            chunk.IsVisible = true;
            VisibleChunks.Add(chunk);
        }
    }

    public void DrawGizmos(Float3 offset)
    {
        Root.DrawGizmos(offset);
    }

    public List<TerrainChunk> GetVisibleChunks()
    {
        return VisibleChunks;
    }
}
