// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>A pose followed from step to step, with how fast it moved over the last step and the one before.</summary>
internal struct TrackedPose
{
    public Float3 Position, LastPosition;
    public Quaternion Rotation, LastRotation;
    public Float3 Velocity, LastVelocity;
    public Float3 Spin, LastSpin;
    private int _count;

    /// <summary>Whether it has moved from one pose to another, so it has a velocity.</summary>
    public readonly bool HasVelocity => _count >= 2;

    /// <summary>Whether it has two velocities, so it has an acceleration.</summary>
    public readonly bool HasAcceleration => _count >= 3;

    public void Push(Float3 position, Quaternion rotation, float dt)
    {
        bool first = _count == 0;
        LastPosition = first ? position : Position;
        LastRotation = first ? rotation : Rotation;
        Position = position;
        Rotation = rotation;
        LastVelocity = Velocity;
        LastSpin = Spin;
        Velocity = first ? Float3.Zero : (Position - LastPosition) / dt;
        Spin = first ? Float3.Zero : Quaternion.ToRotationVector(Rotation * Quaternion.Inverse(LastRotation)) / dt;
        if (_count < 3) _count++;
    }

    public readonly Float3 Acceleration(float dt) => (Velocity - LastVelocity) / dt;

    public readonly Float3 AngularAcceleration(float dt) => (Spin - LastSpin) / dt;

    /// <summary>The pose between the last one and this, 0 at the last and 1 at this.</summary>
    public readonly Float3 PositionAt(float t) => Maths.Lerp(LastPosition, Position, t);

    public readonly Quaternion RotationAt(float t) => Quaternion.Slerp(LastRotation, Rotation, t);
}
