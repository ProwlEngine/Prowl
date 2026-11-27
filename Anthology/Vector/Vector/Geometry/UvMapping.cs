// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Vector.Geometry;

/// <summary>Maps world positions on a face to texture coordinates: u is the position along U plus Uo, v along V plus Vo.</summary>
public readonly record struct UvMapping(Float3 U, float Uo, Float3 V, float Vo)
{
    /// <summary>The texture coordinates of a point.</summary>
    public Float2 Map(Float3 p) => new(Float3.Dot(p, U) + Uo, Float3.Dot(p, V) + Vo);

    /// <summary>Whether two mappings give the same coordinates, to within a tolerance.</summary>
    public bool Matches(UvMapping other, float epsilon = 1e-4f)
        => Float3.Length(U - other.U) < epsilon && Float3.Length(V - other.V) < epsilon && MathF.Abs(Uo - other.Uo) < epsilon && MathF.Abs(Vo - other.Vo) < epsilon;
}
