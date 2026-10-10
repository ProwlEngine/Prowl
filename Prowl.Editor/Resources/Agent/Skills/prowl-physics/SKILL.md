---
name: prowl-physics
description: Prowl physics for gameplay. Use when writing movement with CharacterController, raycasts or shape casts for shooting and ground checks, layers and query filters, rigidbody projectiles and forces, trigger zones, collision callbacks, or making objects shootable.
---

# Prowl physics

## Queries

Queries live on the scene: `Scene.Current.Physics`. `Scene` is in `Prowl.Runtime.Resources`, and without that using, `Scene` inside a component means the component's own nullable `Scene` property. Queries are **main thread only**.

- `Raycast(origin, direction, out RaycastHit hit)` and `Raycast(origin, direction, maxDistance, out hit)`.
- **With a filter, Raycast moves the out before the distance:** `Raycast(origin, direction, out hit, maxDistance, filter)`. SphereCast keeps it after: `SphereCast(origin, radius, direction, maxDistance, out hit, filter)`.
- `RaycastAll(origin, direction, maxDistance, hits)` fills a list, nearest first. `Linecast(from, to, out hit)`.
- `SphereCast(origin, radius, direction, maxDistance, out ShapeCastHit hit)`, `OverlapSphere(position, radius, hits)`, `OverlapBox(position, size, rotation, hits)`. Box sizes are full extents, not half.
- The direction is normalized for you.

Hit data:

- `hit.Point`, `hit.Normal`, `hit.Distance`, `hit.Collider`, `hit.Transform`.
- Use `hit.Transform.GameObject` to find what was hit. **`hit.Collider` is null for terrain and `hit.Rigidbody` is null for static colliders.**

Filters and layers:

- `new QueryFilter(mask).Ignoring(myBody)` skips the shooter. A `LayerMask` converts to a `QueryFilter` by itself.
- `default(LayerMask)` and `LayerMask.Everything` hit every layer. Build masks with `LayerMask.FromMask(1u << LayerMask.NameToLayer("Enemy"))`.

```csharp
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

public sealed class HitscanGun : MonoBehaviour
{
    public float Range = 100f;
    public float Damage = 10f;
    public Rigidbody3D Owner;

    public override void Update()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        QueryFilter filter = Owner.IsValid() ? new QueryFilter(LayerMask.Everything).Ignoring(Owner) : QueryFilter.Default;
        if (Scene.Current.Physics.Raycast(Transform.Position, Transform.Forward, out RaycastHit hit, Range, filter))
        {
            if (hit.Transform.GameObject.TryGetComponent<Health>(out var health)) health.Hurt(Damage);
            if (hit.Rigidbody.IsValid()) hit.Rigidbody.ApplyImpulse(Transform.Forward * 2f, hit.Point);
        }
    }
}

public sealed class Health : MonoBehaviour
{
    public float Value = 100f;

    public void Hurt(float amount)
    {
        Value -= amount;
        if (Value <= 0f) GameObject.Destroy();
    }
}
```

## CharacterController

- The pivot is at the **feet**. `Height` and `Radius` extend up from there.
- `Move(displacement)` takes a distance for this frame, not a velocity, and returns `CharacterController.CollisionFlags` (`Sides`, `Above`, `Below`). There is no `SimpleMove` and **no gravity**, apply it yourself.
- `IsGrounded`, `Velocity` (what the last move achieved), `GroundNormal`, `GroundVelocity`.
- Use `Teleport(position)` to place it. Setting the Transform directly is not a move.
- It never pushes rigidbodies.
- **It has no physics body, so raycasts and triggers do not see it as a body.** To make the player shootable, add a collider and a kinematic `Rigidbody3D` on the controller's own GameObject, which the controller ignores. A body on a child is not ignored, so call `controller.IgnoreCollisionWith(childBody)` for it.
- A CharacterController gets no `OnCollisionBegin` for what it walks into. After `Move`, read `controller.Hits` (a list of `ShapeCastHit`) and `controller.Collisions`.

```csharp
using System;
using Prowl.Runtime;
using Prowl.Vector;

[RequireComponent(typeof(CharacterController))]
public sealed class FpsMover : MonoBehaviour
{
    public float Speed = 5f;
    public float JumpHeight = 1.2f;
    public float Gravity = 20f;

    private CharacterController _controller;
    private Float3 _velocity;

    public override void OnEnable() => _controller = GetComponent<CharacterController>();

    public override void Update()
    {
        float dt = Time.DeltaTime;
        Float2 input = Input.GetWASD();
        Float3 wish = (Transform.Forward * input.Y + Transform.Right * input.X) * Speed;

        float rising = _velocity.Y;
        if (_controller.IsGrounded && rising <= 0f) rising = -2f;
        else rising -= Gravity * dt;
        if (_controller.IsGrounded && Input.GetKeyDown(KeyCode.Space)) rising = MathF.Sqrt(2f * Gravity * JumpHeight);

        _velocity = new Float3(wish.X, rising, wish.Z);
        var flags = _controller.Move(_velocity * dt);
        if ((flags & CharacterController.CollisionFlags.Above) != 0 && _velocity.Y > 0f) _velocity.Y = 0f;
    }
}
```

## Rigidbody3D

- `MotionType` (namespace `Jitter2.Dynamics`, add `using Jitter2.Dynamics;`) is `Dynamic`, `Kinematic` or `Static`. There is no `isKinematic` bool.
- `LinearVelocity`, `AngularVelocity`, `Mass`, `AffectedByGravity`, `AddForce(force, ForceMode)`, `ApplyImpulse(impulse)`, `MovePosition`, `MoveRotation`.
- A collider with no `Rigidbody3D` on it or a parent is static.
- Interpolation is on by default.
- `LinearDamping` and `AngularDamping` (0 to 1) are the fraction of velocity removed every physics step, so they are strong: at 60 steps a second, 0.1 leaves under 1 percent of the speed after one second. The defaults are 0.002 and 0.005.
- A ball rolled only by torque climbs onto light bodies such as crates instead of pushing them. Add a horizontal force in the direction of travel as well.

## Collisions and triggers

- `OnCollisionBegin`, `OnCollisionStay` and `OnCollisionEnd(Collision collision)` fire on both sides: on the object with the collider and on the object with the rigidbody. `collision.GameObject` is the other object and `collision.Normal` points toward this side.
- **Colliders have no trigger flag.** Add a `TriggerVolume` component instead. `OnTriggerEnter/Stay/Exit(Rigidbody3D other)` then fire on the trigger's own GameObject, for bodies with a `Rigidbody3D`. Character controllers entering it raise `OnCharacterEnter/Stay/Exit(CharacterController controller)` instead.
- `TriggerVolume.Overlapping` and `OverlappingCharacters` list what is inside right now.

Look up `PhysicsWorld`, `CharacterController`, `Rigidbody3D`, `TriggerVolume`, `QueryFilter` and `ShapeCastHit` with `api --type`.
