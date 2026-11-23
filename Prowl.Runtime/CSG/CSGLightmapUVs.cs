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

    /// <summary>Hash of everything a CSG build writes into a mesh: positions, normals, UVs, indices and submesh ranges.</summary>
    public static long ContentHash(Mesh? mesh)
    {
        if (mesh.IsNotValid()) return 0;

        long hash = GeometryHash(mesh);
        foreach (Float3 n in mesh!.Normals)
        {
            hash = hash * 31 + n.X.GetHashCode();
            hash = hash * 31 + n.Y.GetHashCode();
            hash = hash * 31 + n.Z.GetHashCode();
        }
        foreach (Float2 uv in mesh.UV)
        {
            hash = hash * 31 + uv.X.GetHashCode();
            hash = hash * 31 + uv.Y.GetHashCode();
        }
        for (int i = 0; i < mesh.SubMeshCount; i++)
        {
            SubMeshDescriptor sub = mesh.GetSubMesh(i);
            hash = hash * 31 + sub.IndexStart;
            hash = hash * 31 + sub.IndexCount;
        }
        return hash;
    }

    /// <summary>Unwraps into UV2.</summary>
    public static void Generate(Mesh? mesh)
    {
        if (mesh.IsNotValid() || mesh!.Vertices.Length == 0) return;
        LightmapUVGenerator.Generate(mesh);
    }
}
