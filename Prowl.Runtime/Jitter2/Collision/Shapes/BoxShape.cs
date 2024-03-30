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
/// Represents a three-dimensional box shape.
/// </summary>
public class BoxShape : Shape
{
    private JVector halfSize;

    /// <summary>
    /// Gets or sets the dimensions of the box.
    /// </summary>
    public JVector Size
    {
        get => 2.0f * halfSize;
        set
        {
            halfSize = value * 0.5f;
            UpdateShape();
        }
    }

    /// <summary>
    /// Creates a box shape with specified dimensions.
    /// </summary>
    /// <param name="size">The dimensions of the box.</param>
    public BoxShape(JVector size)
    {
        halfSize = 0.5f * size;
        UpdateShape();
    }

    /// <summary>
    /// Creates a cube shape with the specified side length.
    /// </summary>
    /// <param name="size">The length of each side of the cube.</param>
    public BoxShape(float size)
    {
        halfSize = new JVector(size * 0.5f);
        UpdateShape();
    }

    /// <summary>
    /// Creates a box shape with the specified length, height, and width.
    /// </summary>
    /// <param name="length">The length of the box.</param>
    /// <param name="height">The height of the box.</param>
    /// <param name="width">The width of the box.</param>
    public BoxShape(float length, float height, float width)
    {
        halfSize = 0.5f * new JVector(length, height, width);
        UpdateShape();
    }

    public override void SupportMap(in JVector direction, out JVector result)
    {
        result.X = Math.Sign(direction.X) * halfSize.X;
        result.Y = Math.Sign(direction.Y) * halfSize.Y;
        result.Z = Math.Sign(direction.Z) * halfSize.Z;
    }

    public override void CalculateBoundingBox(in JMatrix orientation, in JVector position, out JBBox box)
    {
        JMatrix.Absolute(in orientation, out JMatrix abs);
        JVector.Transform(halfSize, abs, out JVector temp);

        box.Max = temp;
        JVector.Negate(temp, out box.Min);

        JVector.Add(box.Min, position, out box.Min);
        JVector.Add(box.Max, position, out box.Max);
    }

    public override void CalculateMassInertia(out JMatrix inertia, out JVector com, out float mass)
    {
        JVector size = halfSize * 2.0f;
        mass = size.X * size.Y * size.Z;

        inertia = JMatrix.Identity;
        inertia.M11 = 1.0f / 12.0f * mass * (size.Y * size.Y + size.Z * size.Z);
        inertia.M22 = 1.0f / 12.0f * mass * (size.X * size.X + size.Z * size.Z);
        inertia.M33 = 1.0f / 12.0f * mass * (size.X * size.X + size.Y * size.Y);

        com = JVector.Zero;
    }
}