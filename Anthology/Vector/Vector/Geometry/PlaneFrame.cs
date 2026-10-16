// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Vector.Geometry;

/// <summary>A 2D frame on a plane. U cross V is the normal, so counter clockwise in 2D is counter clockwise about the normal.</summary>
public readonly struct PlaneFrame
{
    public readonly Float3 Normal;
    public readonly Float3 Origin;
    public readonly Float3 U;
    public readonly Float3 V;

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

    public Float2 To2D(Float3 p)
    {
        Float3 d = p - Origin;
        return new Float2(Float3.Dot(d, U), Float3.Dot(d, V));
    }

    public Float3 To3D(Float2 p) => Origin + U * p.X + V * p.Y;

    public float Distance(Float3 p) => Float3.Dot(p - Origin, Normal);

    public Float3 Project(Float3 p) => p - Normal * Distance(p);
}
