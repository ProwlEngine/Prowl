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
/// Creates a universal joint utilizing a <see cref="TwistAngle"/>, <see cref="BallSocket"/>, and an optional <see cref="AngularMotor"/>
/// constraint.
/// </summary>
public class UniversalJoint : Joint
{
    public RigidBody Body1 { get; private set; }
    public RigidBody Body2 { get; private set; }

    public TwistAngle TwistAngle { get; }
    public BallSocket BallSocket { get; }
    public AngularMotor? Motor { get; }

    public UniversalJoint(World world, RigidBody body1, RigidBody body2, JVector center, JVector rotateAxis1, JVector rotateAxis2, bool hasMotor = false)
    {
        Body1 = body1;
        Body2 = body2;

        rotateAxis1.Normalize();
        rotateAxis2.Normalize();

        TwistAngle = world.CreateConstraint<TwistAngle>(body1, body2);
        TwistAngle.Initialize(rotateAxis1, rotateAxis2);
        Register(TwistAngle);

        BallSocket = world.CreateConstraint<BallSocket>(body1, body2);
        BallSocket.Initialize(center);
        Register(TwistAngle);

        if (hasMotor)
        {
            Motor = world.CreateConstraint<AngularMotor>(body1, body2);
            Motor.Initialize(rotateAxis1, rotateAxis2);
            Register(Motor);
        }
    }
}