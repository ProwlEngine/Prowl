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
using Jitter2.DataStructures;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace Jitter2.Collision.Shapes;

/// <summary>
/// The main entity of the collision system. Implements <see cref="ISupportMap"/> for
/// narrow-phase and <see cref="IDynamicTreeProxy"/> for broad-phase collision detection.
/// The shape itself does not have a position or orientation. Shapes can be associated with 
/// instances of <see cref="RigidBody"/>.
/// </summary>
public abstract class Shape : ISupportMap, IListIndex, IDynamicTreeProxy
{
    int IListIndex.ListIndex { get; set; } = -1;

    /// <summary>
    /// A 64-bit integer representing the shape ID. This is used by algorithms that require 
    /// arranging shapes in a well-defined order.
    /// </summary>
    public readonly ulong ShapeId;

    public Shape()
    {
        ShapeId = World.RequestId();
    }

    internal bool AttachRigidBody(RigidBody? body)
    {
        if (RigidBody == null)
        {
            RigidBody = body;
            return true;
        }

        return false;
    }

    public bool IsRegistered => (this as IListIndex).ListIndex != -1;

    internal void DetachRigidBody()
    {
        RigidBody = null!;
    }

    /// <summary>
    /// The instance of <see cref="RigidBody"/> to which this shape is attached.
    /// </summary>
    public RigidBody? RigidBody { get; private set; }

    /// <summary>
    /// The bounding box of the shape in world space. It is automatically updated when the position or
    /// orientation of the corresponding instance of <see cref="RigidBody"/> changes.
    /// </summary>
    public JBBox WorldBoundingBox { get; protected set; }

    /// <summary>
    /// The inertia of the shape, assuming a homogeneous unit-mass density.
    /// The inertia is calculated with respect to the origin, not necessarily the center of mass.
    /// </summary>
    public JMatrix Inertia { get; protected set; }

    /// <summary>
    /// The geometric center of the shape, equivalent to the center of mass when assuming a
    /// homogeneous unit-mass density.
    /// </summary>
    public JVector GeometricCenter { get; protected set; }

    /// <summary>
    /// The mass of the shape, assuming a homogeneous unit-mass density.
    /// </summary>
    public float Mass { get; protected set; }

    int IDynamicTreeProxy.NodePtr { get; set; }

    public virtual JVector Velocity => RigidBody != null ? RigidBody.Velocity : JVector.Zero;

    /// <summary>
    /// Updates the mass and inertia properties, as well as the world bounding box. This method should be
    /// called by child classes whenever a property of the shape changes, such as the radius of a sphere.
    /// </summary>
    public void UpdateShape()
    {
        UpdateMassInertia();
        UpdateWorldBoundingBox();
    }

    /// <summary>
    /// Calls <see cref="CalculateMassInertia"/> to set the values of <see cref="Inertia"/>, 
    /// <see cref="Mass"/>, and <see cref="GeometricCenter"/>.
    /// </summary>
    public void UpdateMassInertia()
    {
        CalculateMassInertia(out JMatrix inertia, out JVector com, out float mass);
        Inertia = inertia;
        Mass = mass;
        GeometricCenter = com;
    }

    /// <summary>
    /// Calculates the mass and inertia of the shape. Can be overridden by child classes to improve
    /// performance or accuracy. The default implementation relies on an approximation of the shape 
    /// constructed using the <see cref="SupportMap"/> function.
    /// </summary>
    public virtual void CalculateMassInertia(out JMatrix inertia, out JVector com, out float mass)
    {
        ShapeHelper.CalculateMassInertia(this, out inertia, out com, out mass);
    }

    /// <summary>
    /// Calls <see cref="CalculateBoundingBox"/> to set the <see cref="WorldBoundingBox"/> in the frame
    /// of the <see cref="RigidBody"/> instance connected to this shape.
    /// </summary>
    public virtual void UpdateWorldBoundingBox()
    {
        if (RigidBody == null)
        {
            CalculateBoundingBox(JMatrix.Identity, JVector.Zero, out JBBox box);
            WorldBoundingBox = box;
        }
        else
        {
            CalculateBoundingBox(RigidBody.Data.Orientation, RigidBody.Data.Position, out JBBox box);
            WorldBoundingBox = box;
        }
    }

    /// <summary>
    /// Expands the world bounding box of the shape.
    /// </summary>
    /// <param name="sweptDirection">The direction in which to expand.</param>
    public void SweptExpandBoundingBox(in JVector sweptDirection)
    {
        JBBox box = WorldBoundingBox;

        float max;

        float sxa = MathF.Abs(sweptDirection.X);
        float sya = MathF.Abs(sweptDirection.Y);
        float sza = MathF.Abs(sweptDirection.Z);

        if (sxa > sya && sxa > sza) max = sxa;
        else if (sya >= sxa && sya > sza) max = sya;
        else max = sza;

        if (sweptDirection.X < 0.0f)
        {
            box.Min.X -= max;
        }
        else
        {
            box.Max.X += max;
        }

        if (sweptDirection.Y < 0.0f)
        {
            box.Min.Y -= max;
        }
        else
        {
            box.Max.Y += max;
        }

        if (sweptDirection.Z < 0.0f)
        {
            box.Min.Z -= max;
        }
        else
        {
            box.Max.Z += max;
        }

        WorldBoundingBox = box;
    }

    /// <summary>
    /// Calculates the bounding box of the shape in a reference frame defined by the orientation and
    /// position parameters. This bounding box should enclose the shape, which is implicitly defined by the
    /// <see cref="SupportMap"/> function. Child classes should override this implementation to improve
    /// performance.
    /// </summary>
    public virtual void CalculateBoundingBox(in JMatrix orientation, in JVector position, out JBBox box)
    {
        JMatrix oriT = JMatrix.Transpose(orientation);

        SupportMap(oriT.GetColumn(0), out JVector res);
        box.Max.X = JVector.Dot(oriT.GetColumn(0), res);

        SupportMap(oriT.GetColumn(1), out res);
        box.Max.Y = JVector.Dot(oriT.GetColumn(1), res);

        SupportMap(oriT.GetColumn(2), out res);
        box.Max.Z = JVector.Dot(oriT.GetColumn(2), res);

        SupportMap(-oriT.GetColumn(0), out res);
        box.Min.X = JVector.Dot(oriT.GetColumn(0), res);

        SupportMap(-oriT.GetColumn(1), out res);
        box.Min.Y = JVector.Dot(oriT.GetColumn(1), res);

        SupportMap(-oriT.GetColumn(2), out res);
        box.Min.Z = JVector.Dot(oriT.GetColumn(2), res);

        JVector.Add(box.Min, position, out box.Min);
        JVector.Add(box.Max, position, out box.Max);
    }

    /// <inheritdoc/>
    public abstract void SupportMap(in JVector direction, out JVector result);
}