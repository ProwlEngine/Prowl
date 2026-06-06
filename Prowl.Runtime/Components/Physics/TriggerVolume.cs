// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>The overlap shape used by a <see cref="TriggerVolume"/>.</summary>
public enum TriggerShape
{
    Box,
    Sphere,
    Capsule,
}

/// <summary>
/// A non-solid sensor volume. It does not collide with or push anything; instead it queries the physics
/// world each fixed step for overlapping rigidbodies and raises enter/stay/exit events. Use it for pickups,
/// checkpoints, damage zones, detectors, and similar gameplay triggers.
///
/// Only bodies that can be identified (a <see cref="Rigidbody3D"/>) are reported. Static geometry shares a
/// per-layer body and has no component to hand back, so it is ignored.
/// </summary>
[AddComponentMenu("Physics/Trigger Volume")]
[ComponentIcon("")] // Square (region)
public sealed class TriggerVolume : MonoBehaviour
{
    [SerializeField] private TriggerShape shape = TriggerShape.Box;

    /// <summary>Local-space offset of the volume from the GameObject's origin.</summary>
    public Float3 Center = Float3.Zero;

    /// <summary>Box full extents (width, height, depth). Used when <see cref="Shape"/> is Box.</summary>
    public Float3 Size = Float3.One;

    /// <summary>Radius. Used when <see cref="Shape"/> is Sphere or Capsule.</summary>
    public float Radius = 0.5f;

    /// <summary>Capsule total height along the local up axis. Used when <see cref="Shape"/> is Capsule.</summary>
    public float Height = 2.0f;

    /// <summary>Which layers the volume detects.</summary>
    public LayerMask LayerMask = LayerMask.Everything;

    public TriggerShape Shape { get => shape; set => shape = value; }

    /// <summary>Raised once when a rigidbody first enters the volume.</summary>
    public event Action<Rigidbody3D> Entered;

    /// <summary>Raised every fixed step for each rigidbody currently inside the volume.</summary>
    public event Action<Rigidbody3D> Staying;

    /// <summary>Raised once when a rigidbody leaves the volume (or is destroyed while inside).</summary>
    public event Action<Rigidbody3D> Exited;

    private readonly List<ShapeCastHit> _hits = new();
    private HashSet<Rigidbody3D> _current = new();
    private HashSet<Rigidbody3D> _previous = new();

    /// <summary>The rigidbodies currently inside the volume.</summary>
    public IReadOnlyCollection<Rigidbody3D> Overlapping => _current;

    public override void FixedUpdate()
    {
        PhysicsWorld physics = GameObject?.Scene?.Physics;
        if (physics == null) return;

        // Swap buffers: last step's occupants become the baseline we diff against.
        (_previous, _current) = (_current, _previous);
        _current.Clear();

        QueryOverlaps(physics, _hits);

        Rigidbody3D self = GetComponentInParent<Rigidbody3D>();
        foreach (ShapeCastHit hit in _hits)
        {
            Rigidbody3D other = hit.Rigidbody;
            if (other == null || other == self) continue; // skip static/unidentifiable and our own body
            _current.Add(other);
        }

        foreach (Rigidbody3D rb in _current)
        {
            if (_previous.Contains(rb)) Staying?.Invoke(rb);
            else Entered?.Invoke(rb);
        }

        foreach (Rigidbody3D rb in _previous)
            if (!_current.Contains(rb)) Exited?.Invoke(rb);
    }

    public override void OnDisable()
    {
        // Everything that was inside counts as having left when the volume turns off.
        foreach (Rigidbody3D rb in _current) Exited?.Invoke(rb);
        _current.Clear();
        _previous.Clear();
    }

    private void QueryOverlaps(PhysicsWorld physics, List<ShapeCastHit> hits)
    {
        Float3 worldCenter = Transform.TransformPoint(Center);
        Float3 scale = Transform.LossyScale;

        switch (shape)
        {
            case TriggerShape.Sphere:
                physics.OverlapSphere(worldCenter, Radius * MaxComponent(scale), hits, LayerMask);
                break;

            case TriggerShape.Capsule:
                float capRadius = Radius * Maths.Max(Maths.Abs(scale.X), Maths.Abs(scale.Z));
                float halfSegment = Maths.Max(0.0f, Height * 0.5f * Maths.Abs(scale.Y) - capRadius);
                Float3 up = Transform.Up;
                physics.OverlapCapsule(worldCenter + up * halfSegment, worldCenter - up * halfSegment, capRadius, hits, LayerMask);
                break;

            default:
                physics.OverlapBox(worldCenter, Size * scale, Transform.Rotation, hits, LayerMask);
                break;
        }
    }

    private static float MaxComponent(Float3 v) => Maths.Max(Maths.Abs(v.X), Maths.Max(Maths.Abs(v.Y), Maths.Abs(v.Z)));

    public override void DrawGizmos()
    {
        Color color = _current.Count > 0 ? new Color(1f, 0.85f, 0f, 1f) : new Color(0f, 1f, 0.4f, 1f);
        Float3 worldCenter = Transform.TransformPoint(Center);
        Float3 scale = Transform.LossyScale;

        switch (shape)
        {
            case TriggerShape.Sphere:
                Debug.DrawWireSphere(worldCenter, Radius * MaxComponent(scale), color, 16);
                break;

            case TriggerShape.Capsule:
                float capRadius = Radius * Maths.Max(Maths.Abs(scale.X), Maths.Abs(scale.Z));
                float halfSegment = Maths.Max(0.0f, Height * 0.5f * Maths.Abs(scale.Y) - capRadius);
                Float3 up = Transform.Up;
                Float3 a = worldCenter + up * halfSegment;
                Float3 b = worldCenter - up * halfSegment;
                Debug.DrawWireSphere(a, capRadius, color, 12);
                Debug.DrawWireSphere(b, capRadius, color, 12);
                Float3 right = Transform.Right * capRadius;
                Float3 fwd = Transform.Forward * capRadius;
                Debug.DrawLine(a + right, b + right, color);
                Debug.DrawLine(a - right, b - right, color);
                Debug.DrawLine(a + fwd, b + fwd, color);
                Debug.DrawLine(a - fwd, b - fwd, color);
                break;

            default:
                Float4x4 matrix = Float4x4.CreateTRS(Transform.Position, Transform.Rotation, scale);
                Debug.PushMatrix(matrix);
                Debug.DrawWireCube(Center, Size * 0.5f, color);
                Debug.PopMatrix();
                break;
        }
    }
}
