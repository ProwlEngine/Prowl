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

namespace Jitter2.Dynamics.Constraints;

/// <summary>
/// Constructs a prismatic joint utilizing a <see cref="PointOnLine"/> constraint in conjunction with
/// <see cref="FixedAngle"/>, <see cref="HingeAngle"/>, and <see cref="LinearMotor"/> constraints.
/// </summary>
public class PrismaticJoint : Joint
{
    public RigidBody Body1 { get; private set; }
    public RigidBody Body2 { get; private set; }

    public PointOnLine Slider { get; }

    public FixedAngle? FixedAngle { get; }
    public HingeAngle? HingeAngle { get; }
    public LinearMotor? Motor { get; }

    public PrismaticJoint(World world, RigidBody body1, RigidBody body2, JVector center, JVector axis,
        bool pinned = true, bool hasMotor = false) :
        this(world, body1, body2, center, axis, LinearLimit.Full, pinned, hasMotor)
    {
    }

    public PrismaticJoint(World world, RigidBody body1, RigidBody body2, JVector center, JVector axis, LinearLimit limit,
        bool pinned = true, bool hasMotor = false)
    {
        Body1 = body1;
        Body2 = body2;

        axis.Normalize();

        Slider = world.CreateConstraint<PointOnLine>(body1, body2);
        Slider.Initialize(axis, center, center, limit);
        Register(Slider);

        if (pinned)
        {
            FixedAngle = world.CreateConstraint<FixedAngle>(body1, body2);
            FixedAngle.Initialize();
            Register(FixedAngle);
        }
        else
        {
            HingeAngle = world.CreateConstraint<HingeAngle>(body1, body2);
            HingeAngle.Initialize(axis, AngularLimit.Full);
            Register(HingeAngle);
        }

        if (hasMotor)
        {
            Motor = world.CreateConstraint<LinearMotor>(body1, body2);
            Motor.Initialize(axis, axis);
            Register(Motor);
        }
    }

    public void DebugDraw(IDebugDrawer drawer)
    {
        Slider.DebugDraw(drawer);
        // TODO: ..
    }
}