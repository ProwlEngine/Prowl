// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.Terrain;

/// <summary>
/// Represents a single terrain chunk in the quadtree LOD system.
/// Each chunk is rendered as a GPU instance of the base terrain mesh.
/// </summary>
public class TerrainChunk
{
    public Float3 Position;      // World position of chunk
    public float Size;           // Size of this chunk
    public int LODLevel;          // LOD level (0 = highest detail)
    public bool IsVisible;        // Whether this chunk should be rendered

    // Children for quadtree (null if leaf node)
    public TerrainChunk[] Children;

    public TerrainChunk(Float3 position, float size, int lodLevel)
    {
        Position = position;
        Size = size;
        LODLevel = lodLevel;
        IsVisible = false;
        Children = null;
    }

    /// <summary>
    /// Splits this chunk into 4 children.
    /// </summary>
    public void Subdivide()
    {
        if (Children != null)
            return; // Already subdivided

        float childSize = Size * 0.5f;
        int childLOD = LODLevel + 1;

        Children = new TerrainChunk[4];
        Children[0] = new TerrainChunk(Position, childSize, childLOD);
        Children[1] = new TerrainChunk(Position + new Float3(childSize, 0, 0), childSize, childLOD);
        Children[2] = new TerrainChunk(Position + new Float3(0, 0, childSize), childSize, childLOD);
        Children[3] = new TerrainChunk(Position + new Float3(childSize, 0, childSize), childSize, childLOD);
    }

    /// <summary>
    /// Merges this chunk by removing all children.
    /// Converts this node back to a leaf.
    /// </summary>
    public void Merge()
    {
        Children = null;
        IsVisible = false;
    }

    public void DrawGizmos(Float3 offset)
    {
        var min = offset + Position;
        var max = offset + Position + new Float3(Size, 0, Size);
        Debug.DrawLine(min, new Float3(max.X, min.Y, min.Z), Color.Green);
        Debug.DrawLine(min, new Float3(min.X, min.Y, max.Z), Color.Green);
        Debug.DrawLine(new Float3(max.X, min.Y, min.Z), max, Color.Green);
        Debug.DrawLine(new Float3(min.X, min.Y, max.Z), max, Color.Green);

        if (Children != null)
        {
            foreach (var child in Children)
            {
                child.DrawGizmos(offset);
            }
        }
    }

    /// <summary>
    /// Checks if this chunk is a leaf node (no children).
    /// </summary>
    public bool IsLeaf => Children == null;
}
