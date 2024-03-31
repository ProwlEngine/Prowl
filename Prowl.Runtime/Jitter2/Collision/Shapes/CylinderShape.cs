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
/// Represents a cylinder shape.
/// </summary>
public class CylinderShape : Shape
{
    private float radius;
    private float height;

    /// <summary>
    /// Gets or sets the radius of the cylinder.
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
    /// Gets or sets the height of the cylinder.
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
    /// Initializes a new instance of the <see cref="CylinderShape"/> class, creating a cylinder shape with the specified height and radius. The symmetry axis of the cylinder is aligned along the y-axis.
    /// </summary>
    /// <param name="height">The height of the cylinder.</param>
    /// <param name="radius">The radius of the cylinder at its base.</param>
    public CylinderShape(float height, float radius)
    {
        this.radius = radius;
        this.height = height;
        UpdateShape();
    }

    public override void SupportMap(in JVector direction, out JVector result)
    {
        float sigma = (float)Math.Sqrt(direction.X * direction.X + direction.Z * direction.Z);

        if (sigma > 0.0f)
        {
            result.X = direction.X / sigma * radius;
            result.Y = Math.Sign(direction.Y) * height * 0.5f;
            result.Z = direction.Z / sigma * radius;
        }
        else
        {
            result.X = 0.0f;
            result.Y = Math.Sign(direction.Y) * height * 0.5f;
            result.Z = 0.0f;
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

        JVector p1 = -0.5f * height * upa;
        JVector p2 = +0.5f * height * upa;

        JVector delta = JVector.Max(p1, p2) + new JVector(xext, yext, zext);

        box.Min = position - delta;
        box.Max = position + delta;
    }

    public override void CalculateMassInertia(out JMatrix inertia, out JVector com, out float mass)
    {
        mass = MathF.PI * radius * radius * height;

        inertia = JMatrix.Identity;
        inertia.M11 = 1.0f / 4.0f * mass * radius * radius + 1.0f / 12.0f * mass * height * height;
        inertia.M22 = 1.0f / 2.0f * mass * radius * radius;
        inertia.M33 = 1.0f / 4.0f * mass * radius * radius + 1.0f / 12.0f * mass * height * height;

        com = JVector.Zero;
    }
}