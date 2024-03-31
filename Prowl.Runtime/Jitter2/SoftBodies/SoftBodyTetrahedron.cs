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

using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace Jitter2.SoftBodies;

public class SoftBodyTetrahedron : Shape, ISoftBodyShape
{
    public SoftBodyTetrahedron(SoftBody body, RigidBody v1, RigidBody v2, RigidBody v3, RigidBody v4)
    {
        Vertices[0] = v1;
        Vertices[1] = v2;
        Vertices[2] = v3;
        Vertices[3] = v4;

        SoftBody = body;

        UpdateShape();
    }

    public RigidBody[] Vertices { get; } = new RigidBody[4];

    public override JVector Velocity
    {
        get
        {
            JVector vel = JVector.Zero;

            for (int i = 0; i < 4; i++)
            {
                vel += Vertices[i].Velocity;
            }

            vel *= 0.25f;

            return vel;
        }
    }

    public override void CalculateMassInertia(out JMatrix inertia, out JVector com, out float mass)
    {
        inertia = JMatrix.Identity;
        mass = 1;
        com = JVector.Zero;

        for (int i = 0; i < 4; i++)
        {
            com += Vertices[i].Position;
        }

        com *= 0.25f;
    }

    public RigidBody GetClosest(in JVector pos)
    {
        float dist = float.MaxValue;
        int closest = 0;

        for (int i = 0; i < 4; i++)
        {
            float len = (pos - Vertices[i].Position).LengthSquared();
            if (len < dist)
            {
                dist = len;
                closest = i;
            }
        }

        return Vertices[closest];
    }

    public SoftBody SoftBody { get; }

    public override void UpdateWorldBoundingBox()
    {
        const float extraMargin = 0.01f;

        var box = JBBox.SmallBox;
        GeometricCenter = JVector.Zero;

        for (int i = 0; i < 4; i++)
        {
            box.AddPoint(Vertices[i].Position);
            GeometricCenter += Vertices[i].Position;
        }

        GeometricCenter *= 0.25f;

        box.Min -= JVector.One * extraMargin;
        box.Max += JVector.One * extraMargin;

        WorldBoundingBox = box;
    }

    public override void SupportMap(in JVector direction, out JVector result)
    {
        float maxDot = float.MinValue;
        int furthest = 0;

        for (int i = 0; i < 4; i++)
        {
            float dot = JVector.Dot(direction, Vertices[i].Position);
            if (dot > maxDot)
            {
                maxDot = dot;
                furthest = i;
            }
        }

        result = Vertices[furthest].Position;
    }
}