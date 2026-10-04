// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.Rendering;

/// <summary>Settings for <see cref="MeshLODGenerator"/>.</summary>
public sealed class MeshLODOptions
{
    /// <summary>Keeps open borders in place, for foliage cards and open shells.</summary>
    public bool LockBorders;

    /// <summary>
    /// Largest surface deviation allowed for each step from one level to the next, in the mesh's own units.
    /// A level stops short of its ratio rather than exceed it.
    /// </summary>
    public float MaxError = float.PositiveInfinity;

    // Attribute weights compare squared attribute deviation against squared distance measured in units of
    // the mesh's size, so even small weights hold shading and texturing in place where they vary.

    /// <summary>How strongly shading is protected. Seams between hard edges are always kept regardless.</summary>
    public float NormalWeight = 0.1f;

    /// <summary>How strongly texturing is protected, in UV units. UV seams are always kept regardless.</summary>
    public float UVWeight = 0.1f;

    /// <summary>How strongly vertex colors are protected. Wind and masks are often stored there.</summary>
    public float ColorWeight = 0.1f;

    /// <summary>How strongly skin weights are protected. They cost like any other attribute.</summary>
    public float SkinWeight = 0.5f;

    /// <summary>
    /// Cost of moving onto a corner driven by a different set of bones, in squared fractions of the mesh's
    /// size. The default is about the same as moving the surface two percent of the way across the mesh.
    /// </summary>
    public float BoneSetPenalty = 0.0005f;

    /// <summary>How strongly blend shapes are protected, with offsets measured relative to the mesh's size.</summary>
    public float BlendShapeWeight = 0.5f;
}

/// <summary>One generated level.</summary>
public readonly struct MeshLOD
{
    public readonly Mesh Mesh;

    /// <summary>Estimated surface deviation from the source mesh, in the mesh's own units.</summary>
    public readonly float Error;

    public readonly int Triangles;

    public MeshLOD(Mesh mesh, float error, int triangles)
    {
        Mesh = mesh;
        Error = error;
        Triangles = triangles;
    }
}

/// <summary>
/// Builds lower detail versions of a mesh with <see cref="GeometryOperators.Simplify"/>. Every vertex in a
/// level is one of the source's own vertices, so skinning, blend shapes, every UV set and colors carry over
/// unchanged, and submeshes keep their order for the materials.
/// </summary>
public static class MeshLODGenerator
{
    /// <summary>One level keeping <paramref name="ratio"/> of the source's triangles.</summary>
    public static MeshLOD Generate(Mesh source, float ratio, MeshLODOptions? options = null)
        => GenerateChain(source, [ratio], options)[0];

    /// <summary>
    /// Several levels, each built from the one before so the work shrinks as the levels do.
    /// <paramref name="ratios"/> are fractions of the source's triangle count, not counting triangles
    /// that collapse to nothing when its vertices are welded. A ratio no lower than the one before it
    /// repeats that level.
    /// </summary>
    public static List<MeshLOD> GenerateChain(Mesh source, IReadOnlyList<float> ratios, MeshLODOptions? options = null)
    {
        options ??= new MeshLODOptions();
        GeometryData geometry = MeshGeometry.ToGeometryData(source);
        SimplifyOptions simplify = CreateSimplifyOptions(geometry, options);

        int sourceTriangles = geometry.Faces.Count;
        int current = sourceTriangles;
        float error = 0f;

        var levels = new List<MeshLOD>(ratios.Count);
        foreach (float ratio in ratios)
        {
            int wanted = (int)Math.Floor(sourceTriangles * (double)Math.Clamp(ratio, 0f, 1f));
            if (current > 0 && wanted < current)
            {
                simplify.TargetTriangleCount = wanted;
                SimplifyResult result = GeometryOperators.Simplify(geometry, simplify);
                current = result.TrianglesAfter;

                // Each level only knows its own deviation from the level above, so the sum bounds the total
                error += result.Error;
            }

            levels.Add(new MeshLOD(MeshGeometry.ToMesh(geometry, source), error, current));
        }
        return levels;
    }

    private static SimplifyOptions CreateSimplifyOptions(GeometryData geometry, MeshLODOptions options)
    {
        // Blend shape normals and tangents follow their positions, so they are not seams of their own. Leaving
        // them out also keeps the per corner comparisons cheap on meshes with dozens of shapes.
        var seams = new HashSet<string>();
        foreach (var def in geometry.LoopAttributes)
        {
            bool shapeShading = def.Name.StartsWith(MeshGeometry.BlendShapePrefix, StringComparison.Ordinal)
                && (def.Name.EndsWith("/normal", StringComparison.Ordinal) || def.Name.EndsWith("/tangent", StringComparison.Ordinal));
            if (!shapeShading) seams.Add(def.Name);
        }

        var simplify = new SimplifyOptions
        {
            LockBorders = options.LockBorders,
            MaxError = options.MaxError,
            SeamAttributes = seams,
        };

        var weights = simplify.AttributeWeights;
        weights[MeshGeometry.Normal] = options.NormalWeight;
        weights[MeshGeometry.UV] = options.UVWeight;
        weights[MeshGeometry.VertexColor] = options.ColorWeight;
        weights[MeshGeometry.BoneIndices] = options.BoneSetPenalty;
        weights[MeshGeometry.BoneWeights] = options.SkinWeight;

        // The simplifier measures positions in units of the geometry's extent, so offsets are brought to the
        // same scale. Only offsets are costed, blend shape normals and tangents follow their positions.
        Float3 size = geometry.GetAABB().Size;
        float extent = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        float shapeWeight = extent > 0 ? options.BlendShapeWeight / (extent * extent) : options.BlendShapeWeight;
        foreach (var def in geometry.LoopAttributes)
        {
            if (def.Name.StartsWith(MeshGeometry.BlendShapePrefix, StringComparison.Ordinal) && def.Name.EndsWith("/position", StringComparison.Ordinal))
                weights[def.Name] = shapeWeight;
        }

        return simplify;
    }
}
