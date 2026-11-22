// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor.Modeling;

/// <summary>The shapes a model object can start as.</summary>
public enum PrimitiveKind
{
    Cube,
    Plane,
    Cylinder,
    Sphere,
    Torus,
    Grid,
}

/// <summary>Primitive meshes for the model world, one unit across and given texture coordinates, which the generators do not write.</summary>
public static class Primitives
{
    public static GeometryData Make(PrimitiveKind kind)
    {
        GeometryData mesh = kind switch
        {
            PrimitiveKind.Plane => GeometryGenerator.Plane(new Float2(1f)),
            PrimitiveKind.Grid => GeometryGenerator.Plane(new Float2(1f), default, new Int2(10, 10)),
            PrimitiveKind.Cylinder => GeometryGenerator.Cylinder(0.5f, 1f),
            PrimitiveKind.Sphere => GeometryGenerator.Sphere(0.5f),
            PrimitiveKind.Torus => GeometryGenerator.Torus(0.5f, 0.15f),
            _ => GeometryGenerator.Box(new Float3(1f)),
        };
        // Some generators repeat the points along a seam, which would leave the surface open there
        GeometryOperators.MergeByDistance(mesh, null, 1e-5f);
        if (kind is PrimitiveKind.Cylinder or PrimitiveKind.Sphere or PrimitiveKind.Torus) CylinderProjection(mesh);
        else CubeProjection(mesh);
        return mesh;
    }

    /// <summary>Each face takes coordinates from the axis plane it faces most, one unit of space to one of texture.</summary>
    public static void CubeProjection(GeometryData mesh)
    {
        EnsureUV(mesh);
        foreach (GeometryData.Face face in mesh.Faces)
        {
            Float3 normal = face.Normal();
            Float3 size = new(MathF.Abs(normal.X), MathF.Abs(normal.Y), MathF.Abs(normal.Z));
            int axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
            ForCorners(face, p => axis switch
            {
                0 => new Float2(p.Z, p.Y),
                1 => new Float2(p.X, p.Z),
                _ => new Float2(p.X, p.Y),
            } + new Float2(0.5f));
        }
    }

    /// <summary>
    /// Coordinates wrapped round the Y axis, U the angle and V the height. Faces looking up or down take the plane
    /// across Y instead. A face across the seam keeps its corners together rather than spanning the whole texture.
    /// </summary>
    public static void CylinderProjection(GeometryData mesh)
    {
        EnsureUV(mesh);
        var corners = new List<(GeometryData.Loop Loop, Float2 UV)>();
        foreach (GeometryData.Face face in mesh.Faces)
        {
            if (MathF.Abs(face.Normal().Y) > 0.7f)
            {
                ForCorners(face, p => new Float2(p.X, p.Z) + new Float2(0.5f));
                continue;
            }

            corners.Clear();
            Float3 centre = face.Center();
            GeometryData.Loop loop = face.Loop!;
            do
            {
                Float3 p = loop.Vert.Point;
                // A point on the axis takes the face's angle, so a pole does not pick one at random
                Float3 around = MathF.Abs(p.X) + MathF.Abs(p.Z) < 1e-5f ? centre : p;
                corners.Add((loop, new Float2(MathF.Atan2(around.Z, around.X) / (2f * MathF.PI) + 0.5f, p.Y + 0.5f)));
                loop = loop.Next!;
            } while (loop != face.Loop);

            float min = float.MaxValue, max = float.MinValue;
            foreach ((_, Float2 uv) in corners)
            {
                min = MathF.Min(min, uv.X);
                max = MathF.Max(max, uv.X);
            }
            bool across = max - min > 0.5f;
            foreach ((GeometryData.Loop corner, Float2 uv) in corners)
            {
                float u = across && uv.X < 0.5f ? uv.X + 1f : uv.X;
                corner.Attributes[MeshGeometry.UV] = new GeometryData.FloatAttributeValue(u, uv.Y);
            }
        }
    }

    private static void EnsureUV(GeometryData mesh)
    {
        if (!mesh.HasLoopAttribute(MeshGeometry.UV)) mesh.AddLoopAttribute(MeshGeometry.UV, GeometryData.AttributeBaseType.Float, 2);
    }

    private static void ForCorners(GeometryData.Face face, Func<Float3, Float2> uv)
    {
        GeometryData.Loop loop = face.Loop!;
        do
        {
            Float2 at = uv(loop.Vert.Point);
            loop.Attributes[MeshGeometry.UV] = new GeometryData.FloatAttributeValue(at.X, at.Y);
            loop = loop.Next!;
        } while (loop != face.Loop);
    }
}
