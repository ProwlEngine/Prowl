// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.CSG;

/// <summary>How one CSG surface contributes to the generated meshes.</summary>
public struct CSGSurfaceInfo
{
    /// <summary>Material this surface renders with. Faces are grouped into submeshes by it.</summary>
    public Material? Material;

    /// <summary>Include in the visible mesh.</summary>
    public bool Render;

    /// <summary>Include in the collision mesh.</summary>
    public bool Collide;

    /// <summary>Surfaces sharing a group and an edge are shaded smooth across it. 0 means flat shaded.</summary>
    public int SmoothingGroup;

    public static CSGSurfaceInfo Default(Material? material) =>
        new() { Material = material, Render = true, Collide = true };
}

/// <summary>Meshes produced from one CSG build.</summary>
public sealed class CSGMeshes
{
    /// <summary>Renderable mesh, one submesh per material, or null when nothing renders.</summary>
    public Mesh? Visible;

    /// <summary>Materials in submesh order. Same length as <see cref="Visible"/>'s submesh count.</summary>
    public List<Material?> Materials = new();

    /// <summary>Collision mesh, a single untextured submesh, or null when nothing collides.</summary>
    public Mesh? Collision;
}

/// <summary>
/// The visible mesh of a model kept in pieces, one per cube of space, so a rebuild only redoes the cubes whose faces
/// changed and joins the rest as they were.
/// </summary>
public sealed class CSGMeshCache
{
    /// <summary>The edge length of the cubes the faces are grouped by, from the middle of each face.</summary>
    public const float ChunkSize = 16f;

    internal readonly Dictionary<Int3, Piece> Pieces = new();

    /// <summary>How many cubes the last build redid, for tests.</summary>
    public int LastRebuilt { get; internal set; }

    internal sealed class Piece
    {
        public ulong Key;
        public Float3[] Positions = [];
        public Float3[] Normals = [];
        public Float2[] UVs = [];
        public Float4[] Tangents = [];
        public List<(Material? Material, uint[] Indices)> Parts = new();
    }
}

/// <summary>Converts a built <see cref="GeometryData"/> into engine meshes.</summary>
public static class CSGMeshBuilder
{
    /// <summary>Looks up how a surface behaves, given the brush and source face a CSG output face came from.</summary>
    public delegate CSGSurfaceInfo SurfaceResolver(int brushId, int sourceFaceIndex);

    /// <summary>Squared area below which a collision triangle is dropped as degenerate.</summary>
    private const float DegenerateArea = 1e-14f;

    /// <param name="geometry">The built CSG output.</param>
    /// <param name="resolve">How each surface behaves.</param>
    /// <param name="name">Name for the meshes.</param>
    /// <param name="cache">Pieces from the last build, so only the parts of the model that changed are redone.</param>
    /// <param name="collision">Build the collision mesh too.</param>
    public static CSGMeshes Build(GeometryData geometry, SurfaceResolver resolve, string? name = null, CSGMeshCache? cache = null, bool collision = true)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(resolve);

        var result = new CSGMeshes();
        if (geometry.Faces.Count == 0) return result;

        var infos = new CSGSurfaceInfo[geometry.Faces.Count];
        bool anyRender = false, anyCollide = false;

        geometry.AddFaceAttribute(GeometryData.MaterialAttribute, GeometryData.AttributeBaseType.Int, 1);

        for (int f = 0; f < geometry.Faces.Count; f++)
        {
            GeometryData.Face face = geometry.Faces[f];
            CSGSurfaceInfo info = resolve(CSGScene.GetBrushId(face), CSGScene.GetSourceFaceIndex(face));
            infos[f] = info;

            int material = result.Materials.IndexOf(info.Material);
            if (info.Render && material < 0)
            {
                material = result.Materials.Count;
                result.Materials.Add(info.Material);
            }

            // Written only when they change, since the scene hands back the same faces build after build
            int slot = Math.Max(material, 0);
            if (face.Material != slot) face.Material = slot;
            if (face.Hide == info.Render) face.Hide = !info.Render;
            bool smooth = info.SmoothingGroup != 0;
            if (face.IsSmooth != smooth) face.IsSmooth = smooth;

            anyRender |= info.Render;
            anyCollide |= info.Collide;
        }

        MarkSmoothingBreaks(geometry, infos);

        if (anyRender) result.Visible = cache != null ? BuildVisible(geometry, infos, result.Materials, name, cache) : BuildVisible(geometry, result.Materials, name);
        if (anyCollide && collision) result.Collision = BuildCollision(geometry, infos, name);
        return result;
    }

    /// <summary>The collision mesh alone, for when it was left out of <see cref="Build"/>.</summary>
    public static Mesh? BuildCollision(GeometryData geometry, SurfaceResolver resolve, string? name = null)
    {
        var infos = new CSGSurfaceInfo[geometry.Faces.Count];
        for (int f = 0; f < infos.Length; f++)
        {
            GeometryData.Face face = geometry.Faces[f];
            infos[f] = resolve(CSGScene.GetBrushId(face), CSGScene.GetSourceFaceIndex(face));
        }
        return BuildCollision(geometry, infos, name);
    }

    /// <summary>The visible mesh from cached pieces, redoing only the pieces whose faces changed.</summary>
    private static Mesh? BuildVisible(GeometryData geometry, CSGSurfaceInfo[] infos, List<Material?> materials, string? name, CSGMeshCache cache)
    {
        // Faces grouped by the cube their middle lies in, each group keyed by everything that shows of its faces
        var chunkOf = new int[geometry.Faces.Count];
        var cells = new List<Int3>();
        var keys = new List<ulong>();
        var index = new Dictionary<Int3, int>();
        for (int f = 0; f < geometry.Faces.Count; f++)
        {
            GeometryData.Face face = geometry.Faces[f];
            if (!infos[f].Render || face.VertCount < 3)
            {
                chunkOf[f] = -1;
                continue;
            }

            Float3 middle = Float3.Zero;
            GeometryData.Loop loop = face.Loop!;
            do { middle += loop.Vert.Point; loop = loop.Next!; } while (loop != face.Loop);
            middle /= face.VertCount;
            var cell = new Int3((int)MathF.Floor(middle.X / CSGMeshCache.ChunkSize), (int)MathF.Floor(middle.Y / CSGMeshCache.ChunkSize), (int)MathF.Floor(middle.Z / CSGMeshCache.ChunkSize));
            if (!index.TryGetValue(cell, out int chunk))
            {
                chunk = cells.Count;
                index[cell] = chunk;
                cells.Add(cell);
                keys.Add(14695981039346656037UL);
            }
            chunkOf[f] = chunk;
            keys[chunk] = Mix(keys[chunk], face, infos[f]);
        }

        cache.LastRebuilt = 0;
        var kept = new HashSet<Int3>(cells);
        foreach (Int3 cell in new List<Int3>(cache.Pieces.Keys))
            if (!kept.Contains(cell)) cache.Pieces.Remove(cell);

        for (int chunk = 0; chunk < cells.Count; chunk++)
        {
            if (cache.Pieces.TryGetValue(cells[chunk], out CSGMeshCache.Piece? piece) && piece.Key == keys[chunk]) continue;
            int only = chunk;
            RenderMesh render = geometry.ToRenderMesh(new RenderMeshOptions { SkipHidden = true, Emit = face => chunkOf[face.Index] == only });
            piece = new CSGMeshCache.Piece
            {
                Key = keys[chunk],
                Positions = render.Positions,
                Normals = render.Normals,
                UVs = render.UVs ?? new Float2[render.Positions.Length],
            };
            piece.Tangents = Mesh.ComputeTangents(piece.Positions, piece.Normals, piece.UVs, render.Indices);
            foreach (var subMesh in render.SubMeshes)
                piece.Parts.Add((materials[subMesh.Material], render.Indices[subMesh.IndexStart..(subMesh.IndexStart + subMesh.IndexCount)]));
            cache.Pieces[cells[chunk]] = piece;
            cache.LastRebuilt++;
        }

        return Join(cells, cache, materials, name);
    }

    // The pieces of the cells, in their order, as one mesh with a submesh per material
    private static Mesh? Join(List<Int3> cells, CSGMeshCache cache, List<Material?> materials, string? name)
    {
        int vertexCount = 0;
        var used = new List<Material?>();
        var byMaterial = new List<List<(uint[] Indices, uint Offset)>>();
        foreach (Int3 cell in cells)
        {
            CSGMeshCache.Piece piece = cache.Pieces[cell];
            foreach ((Material? material, uint[] indices) in piece.Parts)
            {
                int slot = used.IndexOf(material);
                if (slot < 0)
                {
                    slot = used.Count;
                    used.Add(material);
                    byMaterial.Add(new List<(uint[], uint)>());
                }
                byMaterial[slot].Add((indices, (uint)vertexCount));
            }
            vertexCount += piece.Positions.Length;
        }

        materials.Clear();
        if (vertexCount == 0) return null;
        materials.AddRange(used);

        var positions = new Float3[vertexCount];
        var normals = new Float3[vertexCount];
        var uvs = new Float2[vertexCount];
        var tangents = new Float4[vertexCount];
        int at = 0;
        foreach (Int3 cell in cells)
        {
            CSGMeshCache.Piece piece = cache.Pieces[cell];
            piece.Positions.CopyTo(positions, at);
            piece.Normals.CopyTo(normals, at);
            piece.UVs.CopyTo(uvs, at);
            piece.Tangents.CopyTo(tangents, at);
            at += piece.Positions.Length;
        }

        int indexCount = 0;
        foreach (var parts in byMaterial) foreach ((uint[] indices, _) in parts) indexCount += indices.Length;
        var allIndices = new uint[indexCount];
        var subMeshes = new List<(int Start, int Count)>();
        int written = 0;
        foreach (var parts in byMaterial)
        {
            int start = written;
            foreach ((uint[] indices, uint offset) in parts)
                for (int i = 0; i < indices.Length; i++) allIndices[written++] = indices[i] + offset;
            subMeshes.Add((start, written - start));
        }

        var mesh = new Mesh { Name = name ?? "CSG Mesh" };
        mesh.IndexFormat = vertexCount > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.Vertices = positions;
        mesh.Normals = normals;
        mesh.UV = uvs;
        mesh.Tangents = tangents;
        mesh.Indices = allIndices;
        mesh.RecalculateBounds();
        mesh.SetSubMeshCount(subMeshes.Count);
        for (int i = 0; i < subMeshes.Count; i++) mesh.SetSubMesh(i, new SubMeshDescriptor(subMeshes[i].Start, subMeshes[i].Count));
        return mesh;
    }

    // Folds what shows of a face into a running hash: where its corners are, their texture coordinates, which edges
    // break its shading, and how its surface draws
    private static ulong Mix(ulong hash, GeometryData.Face face, in CSGSurfaceInfo info)
    {
        hash = Mix(hash, info.Material.IsValid() ? RuntimeHelpers.GetHashCode(info.Material) : 0);
        hash = Mix(hash, (face.IsSmooth ? 1 : 0) | (info.SmoothingGroup << 1));
        GeometryData.Loop loop = face.Loop!;
        do
        {
            Float3 p = loop.Vert.Point;
            hash = Mix(hash, BitConverter.SingleToInt32Bits(p.X));
            hash = Mix(hash, BitConverter.SingleToInt32Bits(p.Y));
            hash = Mix(hash, BitConverter.SingleToInt32Bits(p.Z));
            if (loop.TryGetAttribute(GeometryData.UvAttribute, out GeometryData.AttributeValue? value) && value is GeometryData.FloatAttributeValue { Data.Length: >= 2 } uv)
            {
                hash = Mix(hash, BitConverter.SingleToInt32Bits(uv.Data[0]));
                hash = Mix(hash, BitConverter.SingleToInt32Bits(uv.Data[1]));
            }
            hash = Mix(hash, loop.Edge.IsSharp ? 1 : 0);
            loop = loop.Next!;
        } while (loop != face.Loop);
        return Mix(hash, -1);
    }

    private static ulong Mix(ulong hash, int value) => (hash ^ (uint)value) * 1099511628211UL;

    /// <summary>Smooth shading stops where two smooth faces from different groups meet.</summary>
    private static void MarkSmoothingBreaks(GeometryData geometry, CSGSurfaceInfo[] infos)
    {
        foreach (GeometryData.Edge edge in geometry.Edges)
        {
            bool sharp = false;
            GeometryData.Loop? first = edge.Loop;
            if (first != null && first.RadialNext != first && first.RadialNext!.RadialNext == first)
            {
                int a = infos[first.Face.Index].SmoothingGroup, b = infos[first.RadialNext.Face.Index].SmoothingGroup;
                sharp = a != 0 && b != 0 && a != b;
            }
            edge.IsSharp = sharp;
        }
    }

    private static Mesh? BuildVisible(GeometryData geometry, List<Material?> materials, string? name)
    {
        RenderMesh render = geometry.ToRenderMesh(new RenderMeshOptions { SkipHidden = true });
        if (render.Indices.Length == 0)
        {
            materials.Clear();
            return null;
        }

        // A material with no faces left produces no submesh
        var used = new List<Material?>(render.SubMeshes.Length);
        foreach (var subMesh in render.SubMeshes) used.Add(materials[subMesh.Material]);
        materials.Clear();
        materials.AddRange(used);

        var mesh = new Mesh { Name = name ?? "CSG Mesh" };
        mesh.IndexFormat = render.Positions.Length > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.Vertices = render.Positions;
        mesh.Normals = render.Normals;
        mesh.UV = render.UVs ?? new Float2[render.Positions.Length];
        mesh.Indices = render.Indices;
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        mesh.SetSubMeshCount(render.SubMeshes.Length);
        for (int i = 0; i < render.SubMeshes.Length; i++)
            mesh.SetSubMesh(i, new SubMeshDescriptor(render.SubMeshes[i].IndexStart, render.SubMeshes[i].IndexCount));

        return mesh;
    }

    /// <summary>Shares corners between faces since physics reads only positions and indices.</summary>
    private static Mesh? BuildCollision(GeometryData geometry, CSGSurfaceInfo[] infos, string? name)
    {
        var positions = new List<Float3>();
        var indices = new List<uint>();
        var welded = new int[geometry.Vertices.Count];
        Array.Fill(welded, -1);

        uint IndexOf(GeometryData.Vertex v)
        {
            if (welded[v.Index] < 0)
            {
                welded[v.Index] = positions.Count;
                positions.Add(v.Point);
            }
            return (uint)welded[v.Index];
        }

        for (int f = 0; f < geometry.Faces.Count; f++)
        {
            if (!infos[f].Collide) continue;

            List<GeometryData.Vertex> verts = geometry.Faces[f].NeighborVertices();
            for (int i = 1; i < verts.Count - 1; i++)
            {
                Float3 a = verts[0].Point, b = verts[i].Point, c = verts[i + 1].Point;
                if (Float3.LengthSquared(Float3.Cross(b - a, c - a)) < DegenerateArea) continue;

                indices.Add(IndexOf(verts[0]));
                indices.Add(IndexOf(verts[i]));
                indices.Add(IndexOf(verts[i + 1]));
            }
        }

        if (indices.Count == 0) return null;

        var mesh = new Mesh { Name = name != null ? name + " Collision" : "CSG Collision" };
        mesh.IndexFormat = positions.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.Vertices = positions.ToArray();
        mesh.Indices = indices.ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }
}
