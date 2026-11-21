// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Courier;
using Prowl.Vector;

namespace Prowl.Runtime.Networking;

/// <summary>
///     Lets clients simulate a rigidbody the host controls instead of following it.
///     The host sends the body's state while it moves and once a second while it rests. A client
///     compares each state with where it had the body at that time, and past a threshold snaps the body to the host's
///     state plus the movement it made since. Use it in place of a <see cref="NetworkTransform" />, not with one. It is for
///     objects the host controls: while a client owns the object nobody sends the body, and peers drift apart.
/// </summary>
[AddComponentMenu("Networking/Predicted Rigidbody")]
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody3D))]
public sealed class PredictedRigidbody : Component, INetworkSerialize
{
    /// <summary>How far, in units, a client's body may be from the host's before it is corrected.</summary>
    public float PositionThreshold = 0.1f;

    /// <summary>How far, in degrees, a client's body may be turned from the host's before it is corrected.</summary>
    public float RotationThreshold = 5f;

    // Seconds of its own states a client keeps to compare host states with, and between host states of a resting body.
    private const double HistorySeconds = 1;
    private const double RestInterval = 1;
    private const float MovedDistance = 0.001f;
    private const float MovedAngle = 0.1f;

    internal readonly record struct BodyState(Float3 Position, Quaternion Rotation, Float3 Velocity, Float3 AngularVelocity);

    private readonly List<(double Time, BodyState State)> _history = new();
    private Rigidbody3D? _body;
    private BodyState _sent;
    private double _sentAt = double.NegativeInfinity;
    private double _now;

    /// <summary>How many times this peer's body was corrected to the host's state.</summary>
    public int Corrections { get; private set; }

    private Rigidbody3D? Body
    {
        get
        {
            if (_body.IsNotValid())
            {
                _body = GetComponent<Rigidbody3D>();
            }

            return _body;
        }
    }

    public override void FixedUpdate() => Step(Networking.ServerTime);

    /// <summary>One fixed step at a session time: the host decides whether to send, a client records where it has the body.</summary>
    internal void Step(double now)
    {
        _now = now;
        if (!Networking.IsActive || !GameObject.Network.Active)
        {
            return;
        }

        if (GameObject.IsProxy)
        {
            if (!Networking.IsHost)
            {
                Record(now);
            }

            return;
        }

        if (!Networking.IsHost)
        {
            _history.Clear();
            return;
        }

        BodyState current = Current();
        if (now - _sentAt >= RestInterval || Differs(current, _sent, MovedDistance, MovedAngle) ||
            current.Velocity != _sent.Velocity || current.AngularVelocity != _sent.AngularVelocity)
        {
            Networking.SetDirty(this);
        }
    }

    /// <summary>Remembers where this peer has the body at a session time.</summary>
    internal void Record(double time)
    {
        while (_history.Count > 0 && (_history[0].Time < time - HistorySeconds || _history[^1].Time >= time))
        {
            _history.RemoveAt(_history[0].Time < time - HistorySeconds ? 0 : _history.Count - 1);
        }

        _history.Add((time, Current()));
    }

    /// <summary>
    ///     Compares the host's state at a time with this peer's own then. Past a threshold the body, and what is
    ///     remembered of it, moves by the difference, so it keeps the movement it made since.
    /// </summary>
    internal void Correct(double time, in BodyState host)
    {
        if (_history.Count == 0)
        {
            Apply(host);
            return;
        }

        BodyState then = StateAt(time);
        if (!Differs(then, host, PositionThreshold, RotationThreshold))
        {
            return;
        }

        Float3 position = host.Position - then.Position;
        Quaternion rotation = Quaternion.Normalize(host.Rotation * Quaternion.Inverse(then.Rotation));
        Float3 velocity = host.Velocity - then.Velocity;
        Float3 angular = host.AngularVelocity - then.AngularVelocity;

        BodyState Moved(in BodyState state) => new(state.Position + position,
            Quaternion.Normalize(rotation * state.Rotation), state.Velocity + velocity, state.AngularVelocity + angular);

        BodyState current = Current();
        for (int i = 0; i < _history.Count; i++)
        {
            _history[i] = (_history[i].Time, Moved(_history[i].State));
        }

        Apply(Moved(current));
        Corrections++;
    }

    /// <summary>This peer's state at a time: between the two it recorded around it, or the nearest one it has.</summary>
    private BodyState StateAt(double time)
    {
        if (time <= _history[0].Time)
        {
            return _history[0].State;
        }

        for (int i = 1; i < _history.Count; i++)
        {
            (double laterTime, BodyState later) = _history[i];
            if (time > laterTime)
            {
                continue;
            }

            (double earlierTime, BodyState earlier) = _history[i - 1];
            float t = (float)((time - earlierTime) / (laterTime - earlierTime));
            return new BodyState(Maths.Lerp(earlier.Position, later.Position, t),
                Quaternion.Slerp(earlier.Rotation, later.Rotation, t), Maths.Lerp(earlier.Velocity, later.Velocity, t),
                Maths.Lerp(earlier.AngularVelocity, later.AngularVelocity, t));
        }

        return _history[^1].State;
    }

    private static bool Differs(in BodyState a, in BodyState b, float distance, float degrees)
    {
        if (Float3.Distance(a.Position, b.Position) > distance)
        {
            return true;
        }

        float dot = MathF.Min(1f, MathF.Abs(Quaternion.Dot(a.Rotation, b.Rotation)));
        return 2f * MathF.Acos(dot) * (180f / MathF.PI) > degrees;
    }

    private BodyState Current()
    {
        Rigidbody3D? body = Body;
        return body.IsValid()
            ? new BodyState(body!.Position, body.Rotation, body.LinearVelocity, body.AngularVelocity)
            : new BodyState(Transform.Position, Transform.Rotation, Float3.Zero, Float3.Zero);
    }

    private void Apply(in BodyState state)
    {
        Rigidbody3D? body = Body;
        if (body.IsValid() && body!.IsSimulated)
        {
            body.MoveRotation(state.Rotation);
            body.MovePosition(state.Position);
            body.LinearVelocity = state.Velocity;
            body.AngularVelocity = state.AngularVelocity;
            return;
        }

        Transform.Position = state.Position;
        Transform.Rotation = state.Rotation;
    }

    void INetworkSerialize.OnSerialize(NetWriter writer, bool initial)
    {
        // A spawn goes to one peer. What everyone was last sent, and when, is only what a send to all carried.
        BodyState state = Current();
        if (!initial)
        {
            _sent = state;
            _sentAt = _now;
        }

        writer.WriteDouble(initial ? Networking.ServerTime : _now);
        NetSerializers.WriteFloat3(writer, state.Position);
        NetSerializers.WriteQuaternion(writer, state.Rotation);
        NetSerializers.WriteFloat3(writer, state.Velocity);
        NetSerializers.WriteFloat3(writer, state.AngularVelocity);
    }

    void INetworkSerialize.OnDeserialize(ref NetReader reader, bool initial)
    {
        double time = reader.ReadDouble();
        Float3 position = NetSerializers.ReadFloat3(ref reader);
        Quaternion rotation = NetSerializers.ReadQuaternion(ref reader);
        Float3 velocity = NetSerializers.ReadFloat3(ref reader), angularVelocity = NetSerializers.ReadFloat3(ref reader);

        // What goes into the physics body has to be numbers it can use.
        NetworkValidation.Transform(0, position, rotation, Float3.One);
        if (!double.IsFinite(time) || !NetworkValidation.Finite(velocity) || !NetworkValidation.Finite(angularVelocity))
        {
            throw new InvalidDataException("A predicted body's state contains an invalid time or velocity.");
        }

        BodyState host = new(position, Quaternion.Normalize(rotation), velocity, angularVelocity);
        if (initial)
        {
            _history.Clear();
            Apply(host);
        }
        else if (!Networking.IsActive || GameObject.IsProxy)
        {
            Correct(time, host);
        }
    }
}
