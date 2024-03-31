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
/// Wraps any shape and allows to orientate and translate it.
/// </summary>
public class TransformedShape : Shape
{
    private enum TransformationType
    {
        Identity,
        Rotation,
        General
    }
    
    private JVector translation;
    private JMatrix transformation;
    private TransformationType type;

    /// <summary>
    /// Constructs a transformed shape through an affine transformation define by
    /// a linear map and a translation. 
    /// </summary>
    /// <param name="shape">The original shape which should be transformed.</param>
    /// <param name="translation">Shape is translated by this vector.</param>
    /// <param name="transform">A linear map (may include sheer and scale) of the transformation.</param>
    public TransformedShape(Shape shape, in JVector translation, in JMatrix transform)
    {
        OriginalShape = shape;
        this.translation = translation;
        this.transformation = transform;

        AnalyzeTransformation();
        UpdateShape();
    }

    public TransformedShape(Shape shape, JVector translation) :
        this(shape, translation, JMatrix.Identity)
    {
        
    }

    public Shape OriginalShape { get; }

    public JVector Translation
    {
        get => translation;
        set
        {
            translation = value;
            UpdateShape();
        }
    }

    private void AnalyzeTransformation()
    {
        if (MathHelper.IsRotationMatrix(transformation))
        {
            type = MathHelper.UnsafeIsZero(transformation - JMatrix.Identity) ? 
                TransformationType.Identity : TransformationType.Rotation;
        }
        else
        {
            type = TransformationType.General;
        }
    }

    public JMatrix Transformation
    {
        get => transformation;
        set
        {
            this.transformation = value;
            AnalyzeTransformation();
            UpdateShape();
        }
    }

    public override void SupportMap(in JVector direction, out JVector result)
    {
        if (type == TransformationType.Identity)
        {
            OriginalShape.SupportMap(direction, out result);
            result += translation;
        }
        else
        {
            JVector.TransposedTransform(direction, transformation, out JVector dir);
            OriginalShape.SupportMap(dir, out JVector sm);
            JVector.Transform(sm, transformation, out result);
            result += translation;
        }
    }

    public override void CalculateBoundingBox(in JMatrix orientation, in JVector position, out JBBox box)
    {
        if (type == TransformationType.General)
        {
            // just get the bounding box from the support map
            base.CalculateBoundingBox(orientation, position, out box);
        }
        else
        {
            OriginalShape.CalculateBoundingBox(orientation * this.transformation,
                JVector.Transform(translation, orientation) + position, out box);
        }
    }

    public override void CalculateMassInertia(out JMatrix inertia, out JVector com, out float mass)
    {
        mass = OriginalShape.Mass;

        com = JVector.Transform(OriginalShape.GeometricCenter, transformation) + translation;
        inertia = transformation * JMatrix.Multiply(OriginalShape.Inertia, JMatrix.Transpose(transformation));
        JMatrix pat = mass * (JMatrix.Identity * translation.LengthSquared() - JVector.Outer(translation, translation));
        inertia += pat;
    }
}