// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

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
/// The visible mesh of a model kept in pieces, one per cube of space. The scene keeps every face it did not redo, so
/// each face is resolved once and a rebuild only redoes the cubes that gained or lost faces, joining the rest as they were.
/// </summary>
public sealed class CSGMeshCache
{
    /// <summary>The edge length of the cubes the faces are grouped by, from the middle of each face.</summary>
    public const float ChunkSize = 16f;

    internal readonly Dictionary<Int3, Piece> Pieces = new();
    internal readonly Dictionary<GeometryData.Face, Entry> Faces = new(ReferenceEqualityComparer.Instance);
    internal GeometryData? Source;
    internal int Stamp;

    /// <summary>Something else wrote the face flags of the mesh, so the next build writes them all again.</summary>
    internal bool FlagsStale;

    /// <summary>How many cubes the last build redid, for tests.</summary>
    public int LastRebuilt { get; internal set; }

    /// <summary>Forgets every face, so the next build resolves and draws the whole model again.</summary>
    public void Clear()
    {
        Pieces.Clear();
        Faces.Clear();
        Source = null;
    }

    internal sealed class Entry
    {
        public CSGSurfaceInfo Info;
        public Int3 Cell;
        public bool Drawn;
        public int Slot = -1;
        public int Seen;
    }

    internal sealed class Piece
    {
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
        if (cache != null) return BuildCached(geometry, resolve, name, cache, collision);

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

            int slot = Math.Max(material, 0);
            if (face.Material != slot) face.Material = slot;
            WriteFlags(face, info);

            anyRender |= info.Render;
            anyCollide |= info.Collide;
        }

        MarkSmoothingBreaks(geometry, infos);

        if (anyRender) result.Visible = BuildVisible(geometry, result.Materials, name);
        if (anyCollide && collision) result.Collision = BuildCollision(geometry, infos, name);
        return result;
    }

    // Written only when they change, since the scene hands back the same faces build after build
    private static void WriteFlags(GeometryData.Face face, in CSGSurfaceInfo info)
    {
        if (face.Hide == info.Render) face.Hide = !info.Render;
        bool smooth = info.SmoothingGroup != 0;
        if (face.IsSmooth != smooth) face.IsSmooth = smooth;
    }

    // Resolves only the faces the scene added since the last build and redraws only the cubes that gained or lost one
    private static CSGMeshes BuildCached(GeometryData geometry, SurfaceResolver resolve, string? name, CSGMeshCache cache, bool collision)
    {
        var result = new CSGMeshes();
        if (!ReferenceEquals(cache.Source, geometry))
        {
            cache.Clear();
            cache.Source = geometry;
        }

        geometry.AddFaceAttribute(GeometryData.MaterialAttribute, GeometryData.AttributeBaseType.Int, 1);

        bool rewrite = cache.FlagsStale;
        cache.FlagsStale = false;
        int stamp = ++cache.Stamp;
        int count = geometry.Faces.Count;
        var infos = new CSGSurfaceInfo[count];
        var entries = new CSGMeshCache.Entry[count];
        var added = new List<GeometryData.Face>();
        var dirty = new HashSet<Int3>();
        bool anyRender = false, anyCollide = false;

        for (int f = 0; f < count; f++)
        {
            GeometryData.Face face = geometry.Faces[f];
            if (!cache.Faces.TryGetValue(face, out CSGMeshCache.Entry? entry))
            {
                CSGSurfaceInfo resolved = resolve(CSGScene.GetBrushId(face), CSGScene.GetSourceFaceIndex(face));
                entry = new CSGMeshCache.Entry { Info = resolved, Drawn = resolved.Render && face.VertCount >= 3 };
                if (entry.Drawn) dirty.Add(entry.Cell = CellOf(face));
                cache.Faces[face] = entry;
                WriteFlags(face, resolved);
                added.Add(face);
            }
            else if (rewrite) WriteFlags(face, entry.Info);

            entry.Seen = stamp;
            entries[f] = entry;
            CSGSurfaceInfo info = infos[f] = entry.Info;

            int material = result.Materials.IndexOf(info.Material);
            if (info.Render && material < 0)
            {
                material = result.Materials.Count;
                result.Materials.Add(info.Material);
            }

            int slot = Math.Max(material, 0);
            if (entry.Slot != slot || rewrite)
            {
                if (face.Material != slot) face.Material = slot;
                entry.Slot = slot;
            }

            anyRender |= info.Render;
            anyCollide |= info.Collide;
        }

        // Every face kept was seen, so anything more in the cache left the mesh
        if (cache.Faces.Count > count)
        {
            var gone = new List<GeometryData.Face>();
            foreach ((GeometryData.Face face, CSGMeshCache.Entry entry) in cache.Faces)
            {
                if (entry.Seen == stamp) continue;
                gone.Add(face);
                if (entry.Drawn) dirty.Add(entry.Cell);
            }
            foreach (GeometryData.Face face in gone) cache.Faces.Remove(face);
        }

        MarkSmoothingBreaks(added, infos, entries, dirty);

        if (anyRender) result.Visible = BuildVisible(geometry, entries, result.Materials, name, cache, dirty);
        else cache.Pieces.Clear();
        if (anyCollide && collision) result.Collision = BuildCollision(geometry, infos, name);
        return result;
    }

    private static Int3 CellOf(GeometryData.Face face)
    {
        Float3 middle = Float3.Zero;
        GeometryData.Loop loop = face.Loop!;
        do { middle += loop.Vert.Point; loop = loop.Next!; } while (loop != face.Loop);
        middle /= face.VertCount * CSGMeshCache.ChunkSize;
        return new Int3((int)MathF.Floor(middle.X), (int)MathF.Floor(middle.Y), (int)MathF.Floor(middle.Z));
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

    /// <summary>The visible mesh from cached pieces, redoing the pieces of the dirty cells.</summary>
    private static Mesh? BuildVisible(GeometryData geometry, CSGMeshCache.Entry[] entries, List<Material?> materials, string? name, CSGMeshCache cache, HashSet<Int3> dirty)
    {
        cache.LastRebuilt = 0;
        if (dirty.Count > 0)
        {
            var faces = new Dictionary<Int3, List<GeometryData.Face>>();
            foreach (Int3 cell in dirty) faces[cell] = new List<GeometryData.Face>();
            for (int f = 0; f < entries.Length; f++)
                if (entries[f].Drawn && faces.TryGetValue(entries[f].Cell, out List<GeometryData.Face>? list)) list.Add(geometry.Faces[f]);

            foreach ((Int3 cell, List<GeometryData.Face> list) in faces)
            {
                if (list.Count == 0)
                {
                    cache.Pieces.Remove(cell);
                    continue;
                }
                cache.Pieces[cell] = BuildPiece(geometry, list, materials);
                cache.LastRebuilt++;
            }
        }

        var cells = new List<Int3>(cache.Pieces.Keys);
        cells.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z.CompareTo(b.Z));
        return Join(cells, cache, materials, name);
    }

    private static CSGMeshCache.Piece BuildPiece(GeometryData geometry, List<GeometryData.Face> faces, List<Material?> materials)
    {
        RenderMesh render = geometry.ToRenderMesh(new RenderMeshOptions { SkipHidden = true, Faces = faces });
        var piece = new CSGMeshCache.Piece
        {
            Positions = render.Positions,
            Normals = render.Normals,
            UVs = render.UVs ?? new Float2[render.Positions.Length],
        };
        piece.Tangents = Mesh.ComputeTangents(piece.Positions, piece.Normals, piece.UVs, render.Indices);
        foreach (var subMesh in render.SubMeshes)
            piece.Parts.Add((materials[subMesh.Material], render.Indices[subMesh.IndexStart..(subMesh.IndexStart + subMesh.IndexCount)]));
        return piece;
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

        Mesh mesh = Mesh.FromOwned(name ?? "CSG Mesh", positions, normals, uvs, tangents, allIndices);
        mesh.RecalculateBounds();
        mesh.SetSubMeshCount(subMeshes.Count);
        for (int i = 0; i < subMeshes.Count; i++) mesh.SetSubMesh(i, new SubMeshDescriptor(subMeshes[i].Start, subMeshes[i].Count));
        return mesh;
    }

    /// <summary>Smooth shading stops where two smooth faces from different groups meet.</summary>
    private static void MarkSmoothingBreaks(GeometryData geometry, CSGSurfaceInfo[] infos)
    {
        foreach (GeometryData.Edge edge in geometry.Edges)
        {
            bool sharp = Breaks(edge, infos);
            if (edge.IsSharp != sharp) edge.IsSharp = sharp;
        }
    }

    private static bool Breaks(GeometryData.Edge edge, CSGSurfaceInfo[] infos)
    {
        GeometryData.Loop? first = edge.Loop;
        if (first == null || first.RadialNext == first || first.RadialNext!.RadialNext != first) return false;
        int a = infos[first.Face.Index].SmoothingGroup, b = infos[first.RadialNext.Face.Index].SmoothingGroup;
        return a != 0 && b != 0 && a != b;
    }

    // Only the edges of added faces can change, and a cube drawing the other side of a changed edge is redone too
    private static void MarkSmoothingBreaks(List<GeometryData.Face> added, CSGSurfaceInfo[] infos, CSGMeshCache.Entry[] entries, HashSet<Int3> dirty)
    {
        foreach (GeometryData.Face face in added)
        {
            GeometryData.Loop loop = face.Loop!;
            do
            {
                GeometryData.Edge edge = loop.Edge;
                bool sharp = Breaks(edge, infos);
                if (edge.IsSharp != sharp)
                {
                    edge.IsSharp = sharp;
                    GeometryData.Loop radial = loop;
                    do
                    {
                        CSGMeshCache.Entry other = entries[radial.Face.Index];
                        if (other.Drawn) dirty.Add(other.Cell);
                        radial = radial.RadialNext!;
                    } while (radial != loop);
                }
                loop = loop.Next!;
            } while (loop != face.Loop);
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
