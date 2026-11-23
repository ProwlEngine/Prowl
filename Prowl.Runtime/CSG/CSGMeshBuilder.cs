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

/// <summary>Converts a built <see cref="GeometryData"/> into engine meshes.</summary>
public static class CSGMeshBuilder
{
    /// <summary>Looks up how a surface behaves, given the brush and source face a CSG output face came from.</summary>
    public delegate CSGSurfaceInfo SurfaceResolver(int brushId, int sourceFaceIndex);

    /// <summary>Squared area below which a collision triangle is dropped as degenerate.</summary>
    private const float DegenerateArea = 1e-14f;

    public static CSGMeshes Build(GeometryData geometry, SurfaceResolver resolve, string? name = null)
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

            face.Material = Math.Max(material, 0);
            face.Hide = !info.Render;
            face.IsSmooth = info.SmoothingGroup != 0;

            anyRender |= info.Render;
            anyCollide |= info.Collide;
        }

        MarkSmoothingBreaks(geometry, infos);

        if (anyRender) result.Visible = BuildVisible(geometry, result.Materials, name);
        if (anyCollide) result.Collision = BuildCollision(geometry, infos, name);
        return result;
    }

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
