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
        if (kind is PrimitiveKind.Cylinder or PrimitiveKind.Sphere or PrimitiveKind.Torus) UVs.Cylinder(mesh, mesh.Faces);
        else UVs.Cube(mesh, mesh.Faces);
        return mesh;
    }
}
