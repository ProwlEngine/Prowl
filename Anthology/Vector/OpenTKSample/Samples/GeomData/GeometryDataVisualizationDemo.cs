// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace OpenTKSample.Samples;

/// <summary>
/// Demonstrates the DrawGeometryData utility with different visualization flags.
/// Shows various combinations of normals, edges, loops, vertices, and solid rendering.
/// </summary>
public class GeometryDataVisualizationDemo : IDemo
{
    public string Name => "GeometryData Visualization";

    private readonly GeometryData _mesh;

    public GeometryDataVisualizationDemo()
    {
        // Create a subdivided cube for better visualization
        var aabb = new AABB(new Float3(-0.4f, -0.4f, -0.4f), new Float3(0.4f, 0.4f, 0.4f));
        _mesh = aabb.GetGeometryData();
        GeometryOperators.Subdivide(_mesh);

        // Calculate normals for the mesh
        GeometryOperators.RecalculateNormals(_mesh);
    }

    public void Draw(Float3 position, float timeInSeconds)
    {
        // Cycle through different visualization modes over time
        float cycle = (timeInSeconds * 0.2f) % 5.0f;
        int mode = (int)Maths.Floor(cycle);

        // Create a rotating mesh
        float rotation = timeInSeconds * 0.3f;
        var quat = Quaternion.AxisAngle(Float3.Normalize(new Float3(1, 1, 0)), rotation);
        var rotationMatrix = Float4x4.CreateFromQuaternion(quat);
        var rotated = CopyAndTransform(_mesh, position, rotationMatrix);

        // Different visualization modes
        switch (mode)
        {
            case 0:
                // Solid + Edges
                Gizmo.DrawGeometryData(rotated,
                    GeometryDataVisualization.Solid | GeometryDataVisualization.Edges);
                break;

            case 1:
                // Solid + Normals
                Gizmo.DrawGeometryData(rotated,
                    GeometryDataVisualization.Solid | GeometryDataVisualization.Normals,
                    normalLength: 0.15f);
                break;

            case 2:
                // Edges + Vertices
                Gizmo.DrawGeometryData(rotated,
                    GeometryDataVisualization.Edges | GeometryDataVisualization.Vertices);
                break;

            case 3:
                // Solid + Loops
                Gizmo.DrawGeometryData(rotated,
                    GeometryDataVisualization.Solid | GeometryDataVisualization.Loops,
                    loopSize: 0.04f);
                break;

            case 4:
                // Everything!
                Gizmo.DrawGeometryData(rotated,
                    GeometryDataVisualization.All,
                    normalLength: 0.12f,
                    vertexSize: 0.06f,
                    loopSize: 0.04f);
                break;
        }
    }

    private GeometryData CopyAndTransform(GeometryData source, Float3 position, Float4x4 rotation)
    {
        var copy = new GeometryData();

        // Copy vertices and transform
        var vertexMap = new Dictionary<GeometryData.Vertex, GeometryData.Vertex>();
        foreach (var v in source.Vertices)
        {
            var transformed = Float4x4.TransformPoint(v.Point, rotation);
            var newV = copy.AddVertex(transformed + position);
            newV.Id = v.Id;
            vertexMap[v] = newV;

            // Copy attributes (including normals)
            foreach (var attr in v.Attributes)
            {
                if (attr.Key == "normal" && attr.Value is GeometryData.FloatAttributeValue floatAttr && floatAttr.Data.Length >= 3)
                {
                    // Transform normal by rotation (now that TransformNormal is fixed)
                    var normal = new Float3(floatAttr.Data[0], floatAttr.Data[1], floatAttr.Data[2]);
                    var transformedNormal = Float4x4.TransformNormal(normal, rotation);

                    newV.Attributes[attr.Key] = new GeometryData.FloatAttributeValue(
                        transformedNormal.X,
                        transformedNormal.Y,
                        transformedNormal.Z);
                }
                else
                {
                    newV.Attributes[attr.Key] = GeometryData.AttributeValue.Copy(attr.Value);
                }
            }
        }

        // Copy faces
        foreach (var face in source.Faces)
        {
            var verts = face.NeighborVertices();
            var newVerts = verts.Select(v => vertexMap[v]).ToArray();
            copy.AddFace(newVerts);
        }

        return copy;
    }

    public Float3 GetBounds() => new Float3(2.0f, 2.0f, 2.0f);
}
