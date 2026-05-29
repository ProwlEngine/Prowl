using System;
using System.Collections.Generic;

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Unwrapper;
using Prowl.Vector;

namespace Prowl.Editor.Importers;

/// <summary>
/// Generates a second UV set (UV2) suitable for lightmapping by running Prowl.Unwrapper over a
/// mesh and baking the resulting atlas UVs into <see cref="Mesh.UV2"/>.
///
/// <para>The unwrapper produces <b>per-corner</b> UVs (one per triangle corner) with seams between
/// charts, whereas a Mesh stores <b>per-vertex</b> attributes. So we split vertices wherever corners
/// that share an original vertex disagree on their lightmap UV, duplicating every other attribute
/// (position, normal, uv, tangent, color, bone data) onto the new vertices. The index buffer keeps
/// the same length and triangle order, so existing submesh ranges stay valid.</para>
/// </summary>
internal static class LightmapUVGenerator
{
    /// <summary>Unwrap <paramref name="mesh"/> and write the result into its UV2 channel. Best-effort:
    /// on failure (degenerate geometry, etc.) it logs a warning and leaves the mesh unchanged.</summary>
    public static void Generate(Mesh mesh)
    {
        if (mesh == null || mesh.MeshTopology != Topology.Triangles) return;

        Float3[] verts = mesh.Vertices;
        uint[] indices = mesh.Indices;
        if (verts.Length == 0 || indices.Length < 3 || indices.Length % 3 != 0) return;

        var positions = new Double3[verts.Length];
        for (int i = 0; i < verts.Length; i++)
            positions[i] = new Double3(verts[i].X, verts[i].Y, verts[i].Z);

        var tris = new int[indices.Length];
        for (int i = 0; i < indices.Length; i++)
            tris[i] = (int)indices[i];

        UnwrapResult result;
        try
        {
            var builder = new UnwrapMesh(positions, tris);

            // Normals act as a welding guard so coincident points with opposing normals stay apart.
            if (mesh.HasNormals)
            {
                Float3[] ns = mesh.Normals;
                if (ns.Length == positions.Length)
                {
                    var dn = new Double3[ns.Length];
                    for (int i = 0; i < ns.Length; i++)
                        dn[i] = new Double3(ns[i].X, ns[i].Y, ns[i].Z);
                    builder.WithNormals(dn);
                }
            }

            result = builder.Unwrap(new UnwrapOptions());
        }
        catch (Exception ex)
        {
            // UnwrapException (collapsed geometry) or arg errors must not fail the whole import.
            Debug.LogWarning($"[Lightmap UV] Unwrap failed for mesh '{mesh.Name}': {ex.Message}");
            return;
        }

        if (result.PerCornerUVs.Length != indices.Length)
        {
            Debug.LogWarning($"[Lightmap UV] Unexpected UV count for mesh '{mesh.Name}'; skipping.");
            return;
        }

        BakePerCornerUVs(mesh, verts, indices, result.PerCornerUVs);
    }

    private static void BakePerCornerUVs(Mesh mesh, Float3[] verts, uint[] indices, Double2[] perCorner)
    {
        // Snapshot the source per-vertex attributes so we can copy them onto the split vertices.
        Float3[]? normals = mesh.HasNormals ? mesh.Normals : null;
        Float2[]? uv = mesh.HasUV ? mesh.UV : null;
        Float4[]? tangents = mesh.HasTangents ? mesh.Tangents : null;
        Color[]? colors = mesh.HasColors ? mesh.Colors : null;
        Color32[]? colors32 = mesh.HasColors32 ? mesh.Colors32 : null;
        Float4[]? boneIdx = mesh.HasBoneIndices ? mesh.BoneIndices : null;
        Float4[]? boneW = mesh.HasBoneWeights ? mesh.BoneWeights : null;

        // A new vertex is unique per (original vertex, lightmap UV). UVs are quantized so corners
        // that are continuous within a chart weld, while corners across a seam stay split.
        var map = new Dictionary<(int orig, int qu, int qv), uint>(verts.Length * 2);
        var newVerts = new List<Float3>(verts.Length);
        var newNormals = normals != null ? new List<Float3>(verts.Length) : null;
        var newUV = uv != null ? new List<Float2>(verts.Length) : null;
        var newUV2 = new List<Float2>(verts.Length);
        var newTangents = tangents != null ? new List<Float4>(verts.Length) : null;
        var newColors = colors != null ? new List<Color>(verts.Length) : null;
        var newColors32 = colors32 != null ? new List<Color32>(verts.Length) : null;
        var newBoneIdx = boneIdx != null ? new List<Float4>(verts.Length) : null;
        var newBoneW = boneW != null ? new List<Float4>(verts.Length) : null;
        var newIndices = new uint[indices.Length];

        const double Q = 65536.0;

        for (int corner = 0; corner < indices.Length; corner++)
        {
            int orig = (int)indices[corner];
            Double2 uvd = perCorner[corner];
            var key = (orig, (int)Math.Round(uvd.X * Q), (int)Math.Round(uvd.Y * Q));

            if (!map.TryGetValue(key, out uint ni))
            {
                ni = (uint)newVerts.Count;
                map[key] = ni;

                newVerts.Add(verts[orig]);
                newNormals?.Add(normals![orig]);
                newUV?.Add(uv![orig]);
                newUV2.Add(new Float2((float)uvd.X, (float)uvd.Y));
                newTangents?.Add(tangents![orig]);
                newColors?.Add(colors![orig]);
                newColors32?.Add(colors32![orig]);
                newBoneIdx?.Add(boneIdx![orig]);
                newBoneW?.Add(boneW![orig]);
            }
            newIndices[corner] = ni;
        }

        // Vertices first: when the count changes the setter resets the dependent channels, so the
        // assignments below must follow. Index/topology length is unchanged so submeshes stay valid.
        mesh.Vertices = newVerts.ToArray();
        if (newNormals != null) mesh.Normals = newNormals.ToArray();
        if (newUV != null) mesh.UV = newUV.ToArray();
        mesh.UV2 = newUV2.ToArray();
        if (newTangents != null) mesh.Tangents = newTangents.ToArray();
        if (newColors != null) mesh.Colors = newColors.ToArray();
        else if (newColors32 != null) mesh.Colors32 = newColors32.ToArray();
        if (newBoneIdx != null) mesh.BoneIndices = newBoneIdx.ToArray();
        if (newBoneW != null) mesh.BoneWeights = newBoneW.ToArray();

        if (newVerts.Count > ushort.MaxValue)
            mesh.IndexFormat = IndexFormat.UInt32;
        mesh.Indices = newIndices;
    }
}
