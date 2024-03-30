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

using System;
using Jitter2.LinearMath;

namespace Jitter2.Collision.Shapes;

/// <summary>
/// Represents a cone shape.
/// </summary>
public class ConeShape : Shape
{
    private float radius;
    private float height;

    /// <summary>
    /// Gets or sets the radius of the cone at its base.
    /// </summary>
    public float Radius
    {
        get => radius;
        set
        {
            radius = value;
            UpdateShape();
        }
    }

    /// <summary>
    /// Gets or sets the height of the cone.
    /// </summary>
    public float Height
    {
        get => height;
        set
        {
            height = value;
            UpdateShape();
        }
    }

    /// <summary>
    /// Initializes a new instance of the ConeShape class with specified radius and height. The symmetry axis of the cone is aligned along the Y-axis.
    /// </summary>
    /// <param name="radius">The radius of the cone at its base.</param>
    /// <param name="height">The height of the cone.</param>
    public ConeShape(float radius = 0.5f, float height = 1.0f)
    {
        this.radius = radius;
        this.height = height;
        UpdateShape();
    }

    public override void SupportMap(in JVector direction, out JVector result)
    {
        const float ZeroEpsilon = 1e-12f;
        // cone = disk + point

        // center of mass of a cone is at 0.25 height
        JVector ndir = direction;
        ndir.Y = 0.0f;
        float ndir2 = ndir.LengthSquared();

        if (ndir2 > ZeroEpsilon)
        {
            ndir *= radius / MathF.Sqrt(ndir2);
        }

        ndir.Y = -0.25f * height;

        // disk support point vs (0, 0.75 * height, 0)
        if (JVector.Dot(direction, ndir) >= direction.Y * 0.75f * height)
        {
            result = ndir;
        }
        else
        {
            result = new JVector(0, 0.75f * height, 0);
        }
    }

    public override void CalculateBoundingBox(in JMatrix orientation, in JVector position, out JBBox box)
    {
        const float ZeroEpsilon = 1e-12f;

        JVector upa = orientation.GetColumn(1);

        float xx = upa.X * upa.X;
        float yy = upa.Y * upa.Y;
        float zz = upa.Z * upa.Z;

        float l1 = yy + zz;
        float l2 = xx + zz;
        float l3 = xx + yy;

        float xext = 0, yext = 0, zext = 0;

        if (l1 > ZeroEpsilon)
        {
            float sl = 1.0f / MathF.Sqrt(l1);
            xext = (yy + zz) * sl * radius;
        }

        if (l2 > ZeroEpsilon)
        {
            float sl = 1.0f / MathF.Sqrt(l2);
            yext = (xx + zz) * sl * radius;
        }

        if (l3 > ZeroEpsilon)
        {
            float sl = 1.0f / MathF.Sqrt(l3);
            zext = (xx + yy) * sl * radius;
        }

        JVector p1 = -0.25f * height * upa;
        JVector p2 = +0.75f * height * upa;

        box.Min = p1 - new JVector(xext, yext, zext);
        box.Max = p1 + new JVector(xext, yext, zext);

        box.AddPoint(p2);

        box.Min += position;
        box.Max += position;
    }

    public override void CalculateMassInertia(out JMatrix inertia, out JVector com, out float mass)
    {
        mass = 1.0f / 3.0f * MathF.PI * radius * radius * height;

        inertia = JMatrix.Identity;
        inertia.M11 = mass * (3.0f / 20.0f * radius * radius + 3.0f / 80.0f * height * height);
        inertia.M22 = 3.0f / 10.0f * mass * radius * radius;
        inertia.M33 = mass * (3.0f / 20.0f * radius * radius + 3.0f / 80.0f * height * height);

        com = JVector.Zero;
    }
}