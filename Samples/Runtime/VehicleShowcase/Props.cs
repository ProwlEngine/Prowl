// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Vector;

namespace VehicleShowcase;

/// <summary>
/// Carries a kinematic Rigidbody3D back and forth between where it starts and <see cref="Travel"/> away, waiting at
/// each end. It moves by velocity rather than by teleporting, so wheels and anything resting on it ride along.
/// </summary>
public sealed class MovingPlatform : Component
{
    /// <summary>How far the far end is from the start, in world space.</summary>
    public Float3 Travel = new(0f, 0f, 10f);
    public float Speed = 4f;

    /// <summary>Seconds spent stopped at each end.</summary>
    public float Wait = 3f;

    private Rigidbody3D _body = null!;
    private Float3 _start;
    private bool _outbound = true;
    private float _waiting;

    public override void OnEnable()
    {
        _body = GetComponent<Rigidbody3D>()!;
        _start = Transform.Position;
    }

    public override void FixedUpdate()
    {
        float dt = Time.FixedDeltaTime;
        if (_waiting > 0f)
        {
            _waiting -= dt;
            _body.LinearVelocity = Float3.Zero;
            return;
        }

        Float3 target = _outbound ? _start + Travel : _start;
        Float3 toTarget = target - _body.Position;
        float distance = Float3.Length(toTarget);
        if (distance < 0.01f)
        {
            _outbound = !_outbound;
            _waiting = Wait;
            _body.LinearVelocity = Float3.Zero;
            return;
        }

        // Eases in to the stop rather than overshooting it within a step.
        float speed = MathF.Min(Speed, distance / dt);
        _body.LinearVelocity = toTarget / distance * speed;
    }
}

/// <summary>Turns a kinematic Rigidbody3D steadily about its own up axis, for a turntable.</summary>
public sealed class Spinner : Component
{
    public float DegreesPerSecond = 20f;

    private Rigidbody3D _body = null!;

    public override void OnEnable() => _body = GetComponent<Rigidbody3D>()!;

    public override void FixedUpdate()
        => _body.AngularVelocity = _body.Rotation * Float3.UnitY * (DegreesPerSecond * MathF.PI / 180f);
}
