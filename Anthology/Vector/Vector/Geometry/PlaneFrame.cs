// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Vector.Geometry;

/// <summary>A 2D frame on a plane. U cross V is the normal, so counter clockwise in 2D is counter clockwise about the normal.</summary>
public readonly struct PlaneFrame
{
    /// <summary>The unit normal of the plane.</summary>
    public readonly Float3 Normal;

    /// <summary>The point that maps to the 2D origin.</summary>
    public readonly Float3 Origin;

    /// <summary>The direction of the 2D x axis.</summary>
    public readonly Float3 U;

    /// <summary>The direction of the 2D y axis.</summary>
    public readonly Float3 V;

    /// <summary>A frame on the plane through the origin with the given normal, its axes lined up with the world where it can.</summary>
    public PlaneFrame(Float3 normal, Float3 origin)
    {
        Normal = Float3.Normalize(normal);
        Origin = origin;

        // World aligned axes where possible, so textures and rectangles line up with the grid
        Float3 reference = MathF.Abs(Normal.Y) > 0.7f ? Float3.UnitX : Float3.UnitY;
        if (MathF.Abs(Normal.Y) > 0.7f && Normal.Y < 0f) reference = -Float3.UnitX;
        U = Float3.Normalize(reference - Normal * Float3.Dot(reference, Normal));
        V = Float3.Cross(Normal, U);
    }

    /// <summary>A point in frame coordinates, dropping how far it is off the plane.</summary>
    public Float2 To2D(Float3 p)
    {
        Float3 d = p - Origin;
        return new Float2(Float3.Dot(d, U), Float3.Dot(d, V));
    }

    /// <summary>A point on the plane from frame coordinates.</summary>
    public Float3 To3D(Float2 p) => Origin + U * p.X + V * p.Y;

    /// <summary>The signed distance of a point from the plane.</summary>
    public float Distance(Float3 p) => Float3.Dot(p - Origin, Normal);

    /// <summary>The nearest point on the plane.</summary>
    public Float3 Project(Float3 p) => p - Normal * Distance(p);
}
