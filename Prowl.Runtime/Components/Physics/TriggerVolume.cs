// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>The overlap shape used by a <see cref="TriggerVolume"/>. Mirrors the primitive colliders.</summary>
public enum TriggerShape
{
    Box,
    Sphere,
    Capsule,
    Cylinder,
    Cone,
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

    /// <summary>Local-space offset of the volume from the GameObject's origin. As <see cref="Collider.Center"/>.</summary>
    public Float3 Center = Float3.Zero;

    /// <summary>Euler rotation of the volume relative to the GameObject, in degrees. As <see cref="Collider.Rotation"/>.</summary>
    public Float3 Rotation = Float3.Zero;

    /// <summary>Box full extents (width, height, depth). Used when <see cref="Shape"/> is Box.</summary>
    public Float3 Size = Float3.One;

    /// <summary>Radius. Used by every shape except Box.</summary>
    public float Radius = 0.5f;

    /// <summary>
    /// Total height along the local up axis, caps included for a capsule. Used by Capsule, Cylinder
    /// and Cone, and means the same thing as the matching collider's Height.
    /// </summary>
    public float Height = 2.0f;

    /// <summary>Which layers the volume detects.</summary>
    public LayerMask LayerMask = LayerMask.Everything;

    public TriggerShape Shape { get => shape; set => shape = value; }

    private readonly List<ShapeCastHit> _hits = new();
    private HashSet<Rigidbody3D> _current = new();
    private HashSet<Rigidbody3D> _previous = new();

    /// <summary>The rigidbodies currently inside the volume.</summary>
    public IReadOnlyCollection<Rigidbody3D> Overlapping => _current;

    private PhysicsWorld ResolvePhysics() =>
        GameObject.IsValid() && GameObject.Scene.IsValid() ? GameObject.Scene.Physics : null;

    private Rigidbody3D _selfBody;

    public override void OnEnable()
    {
        _selfBody = GetComponentInParent<Rigidbody3D>();

        // Sample after the step, not in FixedUpdate: FixedUpdate runs before the step, so it would
        // report overlaps against poses the solver is about to change.
        PhysicsWorld physics = ResolvePhysics();
        if (physics != null) physics.PostStep += OnPostStep;
    }

    private void OnPostStep(float deltaTime)
    {
        PhysicsWorld physics = ResolvePhysics();
        if (physics == null) return;

        // Swap buffers: last step's occupants become the baseline we diff against.
        (_previous, _current) = (_current, _previous);
        _current.Clear();

        QueryOverlaps(physics, _hits);

        foreach (ShapeCastHit hit in _hits)
        {
            // Our own body is already excluded by the query filter, so only unidentifiable hits (static
            // geometry, terrain) are left to drop here.
            Rigidbody3D other = hit.Rigidbody;
            if (other.IsNotValid()) continue;
            _current.Add(other);
        }

        foreach (Rigidbody3D rb in _current)
        {
            if (_previous.Contains(rb)) SceneDispatcher.TriggerStay(GameObject, rb);
            else SceneDispatcher.TriggerEnter(GameObject, rb);
        }

        foreach (Rigidbody3D rb in _previous)
            if (!_current.Contains(rb)) RaiseExit(rb);

        // The occupant set is rebuilt every step, so nothing is held longer than that, but the buffer
        // we just diffed against would otherwise pin its bodies until the step after next.
        _previous.Clear();
    }

    public override void OnDisable()
    {
        PhysicsWorld physics = ResolvePhysics();
        if (physics != null) physics.PostStep -= OnPostStep;

        // Everything that was inside counts as having left when the volume turns off.
        foreach (Rigidbody3D rb in _current) RaiseExit(rb);
        _current.Clear();
        _previous.Clear();
    }

    // A body destroyed while inside the volume has left as far as gameplay is concerned, but there is
    // nothing meaningful to hand the callback, so the exit is dropped rather than reported against a
    // destroyed component.
    private void RaiseExit(Rigidbody3D rb)
    {
        if (rb.IsValid()) SceneDispatcher.TriggerExit(GameObject, rb);
    }

    private void QueryOverlaps(PhysicsWorld physics, List<ShapeCastHit> hits)
    {
        // Skip our own body rather than filtering it out afterwards, so its static colliders go too.
        QueryFilter filter = _selfBody.IsValid() ? new QueryFilter(LayerMask).Ignoring(_selfBody) : new QueryFilter(LayerMask);

        Float3 worldCenter = WorldCenter;
        Quaternion orientation = WorldRotation;
        Float3 scale = Transform.LossyScale;

        switch (shape)
        {
            case TriggerShape.Sphere:
                physics.OverlapSphere(worldCenter, Radius * MaxComponent(scale), hits, filter);
                break;

            case TriggerShape.Capsule:
                GetCapsuleSegment(out Float3 top, out Float3 bottom, out float capRadius);
                physics.OverlapCapsule(top, bottom, capRadius, hits, filter);
                break;

            case TriggerShape.Cylinder:
                physics.OverlapCylinder(worldCenter, RadialScale(scale), HeightScale(scale), orientation, hits, filter);
                break;

            case TriggerShape.Cone:
                physics.OverlapCone(worldCenter, RadialScale(scale), HeightScale(scale), orientation, hits, filter);
                break;

            default:
                physics.OverlapBox(worldCenter, Size * scale, orientation, hits, filter);
                break;
        }
    }

    /// <summary>The volume's centre in world space, offset by <see cref="Center"/>.</summary>
    private Float3 WorldCenter => Transform.TransformPoint(Center);

    /// <summary>The volume's orientation in world space, including <see cref="Rotation"/>.</summary>
    private Quaternion WorldRotation => Transform.Rotation * Quaternion.FromEuler(Rotation);

    // Radius scales with the widest axis across the shape, height with the axis along it.
    private float RadialScale(Float3 scale) => Radius * Maths.Max(Maths.Abs(scale.X), Maths.Abs(scale.Z));
    private float HeightScale(Float3 scale) => Height * Maths.Abs(scale.Y);

    /// <summary>
    /// The capsule's segment endpoints. Height is the total including both caps, so the segment is
    /// what is left after removing them - matching how CapsuleCollider reads its own Height.
    /// </summary>
    private void GetCapsuleSegment(out Float3 top, out Float3 bottom, out float radius)
    {
        Float3 scale = Transform.LossyScale;
        radius = RadialScale(scale);

        float halfSegment = Maths.Max(0.0f, HeightScale(scale) * 0.5f - radius);
        Float3 up = WorldRotation * Float3.UnitY;
        Float3 center = WorldCenter;

        top = center + up * halfSegment;
        bottom = center - up * halfSegment;
    }

    private static float MaxComponent(Float3 v) => Maths.Max(Maths.Abs(v.X), Maths.Max(Maths.Abs(v.Y), Maths.Abs(v.Z)));

    public override void DrawGizmos()
    {
        Color color = _current.Count > 0 ? new Color(1f, 0.85f, 0f, 1f) : new Color(0f, 1f, 0.4f, 1f);
        Float3 worldCenter = WorldCenter;
        Quaternion orientation = WorldRotation;
        Float3 scale = Transform.LossyScale;

        switch (shape)
        {
            case TriggerShape.Sphere:
                Debug.DrawWireSphere(worldCenter, Radius * MaxComponent(scale), color, 16);
                break;

            case TriggerShape.Capsule:
                GetCapsuleSegment(out Float3 top, out Float3 bottom, out float capRadius);
                Debug.DrawWireCapsule(bottom, top, capRadius, color, 16);
                break;

            case TriggerShape.Cylinder:
                Debug.DrawWireCylinder(worldCenter, orientation, RadialScale(scale), HeightScale(scale), color, 16);
                break;

            case TriggerShape.Cone:
                float coneHeight = HeightScale(scale);
                Float3 up = orientation * Float3.UnitY;
                Debug.DrawWireCone(worldCenter - up * (coneHeight * 0.5f), up * coneHeight, RadialScale(scale), color, 16);
                break;

            default:
                Debug.PushMatrix(Float4x4.CreateTRS(worldCenter, orientation, scale));
                Debug.DrawWireCube(Float3.Zero, Size * 0.5f, color);
                Debug.PopMatrix();
                break;
        }
    }
}
