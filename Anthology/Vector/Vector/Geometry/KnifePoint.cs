// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Vector.Geometry;

/// <summary>
/// One point of a knife cut: where it is, and what of the mesh it sits on. A vertex wins over an edge, an edge over
/// a face. With none of the three the point is off the mesh.
/// </summary>
public sealed class KnifePoint
{
    public Float3 Point;
    public GeometryData.Vertex? Vertex;
    public GeometryData.Edge? Edge;
    public GeometryData.Face? Face;
}
