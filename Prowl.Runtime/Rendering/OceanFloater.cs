// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

using G = Prowl.Graphite.Rendering;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Floats a <see cref="Rigidbody3D"/> on the <see cref="Ocean"/>, each probe pushes straight
/// up in proportion to how deep it sits below the water, the body drags against the water's motion along its own axes,
/// rising and falling with the waves and carried by the current, and its spin is damped while it is wet.
/// The water is read a few frames late, from the GPU.
/// </summary>
[AddComponentMenu("Physics/Ocean Floater")]
public sealed class OceanFloater : Component
{
    /// <summary>Points on the body that float, in its local space.</summary>
    public Float3[] Probes = [Float3.Zero];

    /// <summary>The upward force at a metre under water, in body weights. 2 floats the body half a metre deep.</summary>
    public float Buoyancy = 2f;

    /// <summary>Waves shorter than this many metres do not move the body, so large hulls ride the swell, not the ripples.</summary>
    public float SpatialLength = 2f;

    /// <summary>How hard the water holds the body against moving up and down, sideways and forward, per second.</summary>
    public float DragUp = 3f, DragSideways = 2f, DragForward = 1f;

    /// <summary>How hard the water damps the body's spin, per second.</summary>
    public float AngularDrag = 0.5f;

    [System.NonSerialized, NotHeld] private G.OceanQuery? _query;
    [System.NonSerialized, NotHeld] private Ocean? _ocean;

    public override void OnDisable()
    {
        Forget();
    }

    private void Forget()
    {
        if (_ocean.IsValid() && _query != null)
            _ocean!.RemoveQuery(_query);
        (_query, _ocean) = (null, null);
    }

    public override void FixedUpdate()
    {
        Rigidbody3D? body = GameObject.GetComponent<Rigidbody3D>();
        Ocean? ocean = Ocean.Active;
        if (body.IsNotValid() || ocean.IsNotValid() || Probes.Length == 0)
            return;
        if (!ReferenceEquals(_ocean, ocean) || _query == null || _query.Points.Length != Probes.Length)
        {
            Forget();
            _ocean = ocean;
            _query = ocean!.CreateQuery(Probes.Length);
        }

        _query.SpatialLength = SpatialLength;
        Float3[] world = new Float3[Probes.Length];
        for (int i = 0; i < Probes.Length; i++)
        {
            world[i] = Transform.TransformPoint(Probes[i]);
            _query.Points[i] = new Double2(world[i].X, world[i].Z);
        }
        if (!_query.Ready)
            return;

        float share = body!.Mass * 9.81f * Buoyancy / Probes.Length;
        bool wet = false;
        Float3 water = Float3.Zero;
        for (int i = 0; i < Probes.Length; i++)
        {
            float depth = (float)(_query.Heights[i] - world[i].Y);
            water += new Float3(_query.Flows[i].X, _query.Velocities[i], _query.Flows[i].Y) / Probes.Length;
            if (depth <= 0f)
                continue;
            wet = true;
            body.AddForceAtPosition(Float3.UnitY * (share * depth), world[i]);
        }
        if (!wet)
            return;

        Float3 relative = body.LinearVelocity - water;
        Float3 up = Float3.UnitY, right = Transform.Right, forward = Transform.Forward;
        Float3 drag = up * (-DragUp * Float3.Dot(relative, up)) + right * (-DragSideways * Float3.Dot(relative, right)) + forward * (-DragForward * Float3.Dot(relative, forward));
        body.AddForce(drag, ForceMode.Acceleration);
        body.AddTorque(body.AngularVelocity * -AngularDrag, ForceMode.Acceleration);
    }
}
