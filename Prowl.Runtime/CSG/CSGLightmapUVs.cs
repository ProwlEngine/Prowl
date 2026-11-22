// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.AssetImporting;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.CSG;

/// <summary>Lightmap UVs for generated CSG meshes. Never unwrapped during a rebuild, the owning <see cref="CSGModel"/> tracks when they go stale.</summary>
public static class CSGLightmapUVs
{
    /// <summary>How long the geometry must stop changing before an automatic unwrap runs.</summary>
    public const double QuietPeriodSeconds = 2.0;

    /// <summary>Order sensitive hash of the positions and indices an unwrap would be built from.</summary>
    public static long GeometryHash(Mesh? mesh)
    {
        if (mesh.IsNotValid()) return 0;

        Float3[] verts = mesh!.Vertices;
        uint[] indices = mesh.Indices;

        long hash = 17;
        hash = hash * 31 + verts.Length;
        hash = hash * 31 + indices.Length;

        foreach (Float3 v in verts)
        {
            hash = hash * 31 + v.X.GetHashCode();
            hash = hash * 31 + v.Y.GetHashCode();
            hash = hash * 31 + v.Z.GetHashCode();
        }

        foreach (uint i in indices) hash = hash * 31 + i;

        return hash;
    }

    /// <summary>Unwraps into UV2.</summary>
    public static void Generate(Mesh? mesh)
    {
        if (mesh.IsNotValid() || mesh!.Vertices.Length == 0) return;
        LightmapUVGenerator.Generate(mesh);
    }
}
