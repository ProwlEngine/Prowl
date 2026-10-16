// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime.Terrain;

/// <summary>
/// Manages the quadtree LOD system for terrain rendering.
/// Simple distance-based subdivision with a quality multiplier. Each view keeps its own tree, so its
/// hysteresis follows that view's camera.
/// </summary>
public class TerrainQuadtree
{
    public TerrainChunk Root;
    public int MaxLODLevel;
    public float ChunkSize;
    public List<TerrainChunk> VisibleChunks = [];

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
    public void Update(Float3 cameraPosition, float lodQuality = 1f) => Update([cameraPosition], lodQuality);

    /// <summary>
    /// Updates the quadtree for several cameras at once, each chunk as fine as the nearest camera needs, for views
    /// such as shadows that draw the terrain for other cameras.
    /// </summary>
    public void Update(ReadOnlySpan<Float3> cameraPositions, float lodQuality = 1f)
    {
        VisibleChunks.Clear();
        if (cameraPositions.IsEmpty) return;
        UpdateNode(Root, cameraPositions, lodQuality);
    }

    private void UpdateNode(TerrainChunk chunk, ReadOnlySpan<Float3> cameraPositions, float lodQuality)
    {
        // Distance from the nearest camera to the chunk center
        Float3 chunkCenter = chunk.Position + new Float3(chunk.Size * 0.5f, 0, chunk.Size * 0.5f);
        float distanceToCamera = float.MaxValue;
        foreach (Float3 camera in cameraPositions)
            distanceToCamera = MathF.Min(distanceToCamera, Float3.Distance(camera, chunkCenter));

        // Higher quality = larger threshold = subdivides at greater distances
        float subdivideThreshold = chunk.Size * 1.5f * lodQuality;

        if (distanceToCamera < subdivideThreshold && chunk.LODLevel < MaxLODLevel)
        {
            if (chunk.Children == null)
                chunk.Subdivide();

            foreach (TerrainChunk child in chunk.Children!)
                UpdateNode(child, cameraPositions, lodQuality);
        }
        else
        {
            if (chunk.Children != null)
            {
                // Hysteresis: merge at a slightly larger distance to prevent thrashing
                if (distanceToCamera > subdivideThreshold * 1.1f)
                    chunk.Merge();
                else
                {
                    foreach (TerrainChunk child in chunk.Children)
                        UpdateNode(child, cameraPositions, lodQuality);
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
