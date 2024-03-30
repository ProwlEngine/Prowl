/*
 * Copyright (c) Thorben Linneweber and others
 *
 * Permission is hereby granted, free of charge, to any person obtaining
 * a copy of this software and associated documentation files (the
 * "Software"), to deal in the Software without restriction, including
 * without limitation the rights to use, copy, modify, merge, publish,
 * distribute, sublicense, and/or sell copies of the Software, and to
 * permit persons to whom the Software is furnished to do so, subject to
 * the following conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
 * LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
 * OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
 * WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using Jitter2.LinearMath;

namespace Jitter2.Collision.Shapes;

/// <summary>
/// Represents a single triangle within a mesh.
/// </summary>
public class TriangleShape : Shape
{
    public readonly TriangleMesh Mesh;
    public int Index;

    private readonly JVector geomCen;

    /// <summary>
    /// Initializes a new instance of the TriangleShape class.
    /// </summary>
    /// <param name="mesh">The triangle mesh to which this triangle belongs.</param>
    /// <param name="index">The index representing the position of the triangle within the mesh.</param>
    public TriangleShape(TriangleMesh mesh, int index)
    {
        Mesh = mesh;
        Index = index;

        ref var triangle = ref mesh.Indices[index];

        JVector A = mesh.Vertices[triangle.IndexA];
        JVector B = mesh.Vertices[triangle.IndexB];
        JVector C = mesh.Vertices[triangle.IndexC];

        geomCen = 1.0f / 3.0f * (A + B + C);

        UpdateShape();
    }

    public override void CalculateMassInertia(out JMatrix inertia, out JVector com, out float mass)
    {
        inertia = JMatrix.Identity;
        mass = 1;
        com = geomCen;
    }

    /// <summary>
    /// Retrieves the vertices transformed to world space coordinates, as affected by the rigid body's transformation.
    /// </summary>
    /// <param name="a">The transformed coordinate of the first vertex.</param>
    /// <param name="b">The transformed coordinate of the second vertex.</param>
    /// <param name="c">The transformed coordinate of the third vertex.</param>
    public void GetWorldVertices(out JVector a, out JVector b, out JVector c)
    {
        ref var triangle = ref Mesh.Indices[Index];
        a = Mesh.Vertices[triangle.IndexA];
        b = Mesh.Vertices[triangle.IndexB];
        c = Mesh.Vertices[triangle.IndexC];

        if (RigidBody == null) return;

        ref JMatrix orientation = ref RigidBody.Data.Orientation;
        ref JVector position = ref RigidBody.Data.Position;

        JVector.Transform(a, orientation, out a);
        JVector.Transform(b, orientation, out b);
        JVector.Transform(c, orientation, out c);

        a += position;
        b += position;
        c += position;
    }

    public override void CalculateBoundingBox(in JMatrix orientation, in JVector position, out JBBox box)
    {
        const float extraMargin = 0.01f;

        GetWorldVertices(out JVector aworld, out JVector bworld, out JVector cworld);

        box = JBBox.SmallBox;
        box.AddPoint(aworld);
        box.AddPoint(bworld);
        box.AddPoint(cworld);

        box.Min -= JVector.One * extraMargin;
        box.Max += JVector.One * extraMargin;
    }

    public override void SupportMap(in JVector direction, out JVector result)
    {
        ref var triangle = ref Mesh.Indices[Index];

        JVector A = Mesh.Vertices[triangle.IndexA];
        JVector B = Mesh.Vertices[triangle.IndexB];
        JVector C = Mesh.Vertices[triangle.IndexC];

        float min = JVector.Dot(A, direction);
        float dot = JVector.Dot(B, direction);

        result = A;

        if (dot > min)
        {
            min = dot;
            result = B;
        }

        dot = JVector.Dot(C, direction);

        if (dot > min)
        {
            result = C;
        }
    }
}