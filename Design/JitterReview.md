# Jitter2 adversarial review (2026-10-05)

Scope: the Jitter2 source at `jitterphysics2` (HEAD `8fdfcbc`) plus how Prowl integrates it.
Prowl consumes the **2.9.0 NuGet package** (`Prowl.Runtime.csproj:30`), about 30 commits behind HEAD. Unless noted, every issue below exists in both.
Jitter line numbers are HEAD unless marked (2.9.0). Prowl paths are relative to `Prowl.Runtime/`.

Status tags: CONFIRMED means proven with a scratch probe or unambiguous code reading. PLAUSIBLE means reasoned but not run.
The probes live in the session scratchpad under `jitter_review/` (constraints, dynamics, collision, math_api). They compile a private copy of the source, and the repo was not modified.

---

## 1. Critical

### C1. Mutating the world inside BeginCollide corrupts it (CONFIRMED)
- **Where:** `World.Step.cs:855` HandleDeferredArbiters `foreach`es `deferredArbiters` and raises BeginCollide inside the loop. `World.Remove(Arbiter)` (`World.cs:660`) does a SlimBag swap-remove on that same bag, and it skips `IslandHelper.ArbiterRemoved` for arbiters it thinks are still pending, including ones the loop already linked.
- **Proven outcomes:**
  - Removing the body in the handler throws an NRE from Step.
  - Removing the arbiter leaves a recycled arbiter with a null body inside body contact lists, and BeginCollide fires every step after that.
  - With two simultaneous contacts, the second arbiter never gets linked or raises its event. It stays in the solver, and after its body is removed it writes into freed RigidBodyData.
- **Why it matters:** "destroy on hit" is the most common game pattern, and the docs do not forbid it.
- **Fix:** swap the bag out before dispatching, or raise the events after the loop. Make Remove(Arbiter) aware of arbiters that were already linked.

### C2. A self contact hangs World.Step forever (CONFIRMED)
- **Where:** RegisterContact and GetOrCreateArbiter (`World.Detect.cs:289,351,412`) accept body1 == body2. CreateConstraint rejects the same case. `TryLockTwoBody` (`World.Step.cs:879`) cannot lock one body twice, so the deferred queue re-enqueues the contact forever. This also happens single threaded.
- **Real trigger:** the shipped soft body broadphase filters (`SoftBodies/BroadPhaseCollisionFilter.cs:53-97`) produce these contacts for:
  - adjacent soft shapes that share a vertex,
  - vertex bodies that also carry a shape,
  - stitched soft bodies.
- **Fix:** throw in GetOrCreateArbiter, skip the pair in the filter, and lock once when the two bodies are the same.

### C3. The generic GJK ray cast reports false hits (CONFIRMED, verified by reading)
- **Where:** `NarrowPhase.cs:560-597`.
  - When lambda advances, only the new `w` is recomputed. The simplex keeps vertices built from the old ray point.
  - Running out of iterations falls through to `converged:` and returns true.
- **Measured:** 5-7% of near-miss rays came back as hits, with lambda up to 4e6. In a real world, 6900 of 100k rays against a rotated PointCloudShape box were false hits.
- **Affected shapes:** every shape without an analytic override: Capsule, Cylinder, Cone, ConvexHull, PointCloud, TransformedShape, and soft body shapes.
- **Fix:** store the support points and recompute every `w` (or reset the simplex) when lambda advances, and return false when iterations run out. With that fix the probe showed 0 false hits and 0 false misses.
- **Prowl link:** Prowl commit `03f46666` ("jitter Raycast being inaccurate on large bodies") works around this through the `Collider.cs:417` PlacedShape override.

### C4. MprEpa returns near-zero penetration with the wrong normal on deep contacts (CONFIRMED)
- **Where:** `NarrowPhase.cs:76-79`. A failed `AddVertex` is treated as converged even when deltaDist is still large. `Collision()` at :397-400 has the same pattern.
- **Measured against SAT:**
  - Box-box: 1.2% of contacts deeper than 0.02 collapse. Example: true depth 0.58 was reported as 0.14 with the normal off.
  - Box-sphere: 59 per 100k collapse to depth 0 with an arbitrary normal.
- **Note:** TriangleEdgeCollisionFilter forces this path (epaThreshold 0), so meshes always take it.
- **Fix:** treat that case as an EPA failure and fall back to the MPR result.

---

## 2. High

### H1. Motor max force is multiplied by SubstepCount (CONFIRMED, verified by reading)
- **Where:** `AngularMotor.cs:165` and `LinearMotor.cs:166` set `MaxLambda = MaxForce / invStepDt`. The clamp is applied on every substep, and the impulse is warm started across substeps.
- **Measured:** delivered acceleration was exactly 1x, 2x, 4x and 8x at 1, 2, 4 and 8 substeps. A load of 2.5 against max torque 1 slips at 1 substep and holds completely at 4.
- **Prowl:** `PhysicsConstraint.PerSubstep` is the exact correct compensation, and `PhysicsWorld.Substep` refreshes it.
- **Fix:** `MaxLambda = MaxForce * substepDt`.

### H2. Switching a jointed body to Static/Kinematic deletes its joints for good (CONFIRMED)
- **Where:** `RigidBody.cs:933,950` call `RemoveStaticStaticConstraints` (`World.cs:726`), which removes every constraint whose other side is non-dynamic, including NullBody.
- **Effect:**
  - Switching back to Dynamic does not restore them.
  - The Joint is not told, and touching it afterwards throws a bare NRE.
- **Prowl risk:** toggling IsKinematic on any body jointed to the world, or to another kinematic or static body, silently destroys those joints.
- **Fix:** make such constraints dormant instead of removing them, or at least document it and give Joint an invalid state.

### H3. CreateConstraint with a removed body bricks the world (CONFIRMED)
- **Where:** `World.cs:864-895` checks `body.World` but not `IsValid`. It allocates the constraint before `IslandHelper.ConstraintCreated` throws.
- **Effect:** every later Step throws an NRE in PrepareConstraints.
- **Same gap:** RegisterContact and GetOrCreateArbiter.

### H4. Slow kinematic bodies fall asleep and freeze (CONFIRMED, verified by reading)
- **Where:** `RigidBody.cs:715-728` applies the sleep threshold to every motion type.
- **Measured:** a kinematic box at 0.2 m/s stops after about 1 s while its Velocity still reads 0.2. Bodies riding it fall asleep too.
- **Breaks:** elevators, doors, slow rotators.
- **Fix:** exempt kinematic bodies with nonzero velocity from sleeping.

### H5. Linear sweeps have the same stale simplex bug as C3 (CONFIRMED)
- **Where:** `NarrowPhase.cs:1174-1205`.
- **Effect:** false hits, and TOIs that are early by more than 1 cm.
- **Reach:** World.Detect speculative contacts use this function through the 12 argument Sweep (`World.Detect.cs:171`).

### H6. Shape setters on static or sleeping bodies never reach the broadphase (CONFIRMED)
- **Where:** the Size, Radius, Length, Height and Transform setters only call UpdateWorldBoundingBox. DynamicTree only refits active proxies.
- **Measured:** a static box resized from 1 to 10 lets a sphere fall straight through its new volume. Mass and inertia are not refreshed either.

### H7. Triangle ray casts return hits behind the origin (CONFIRMED)
- **Where:** `JTriangle.RayIntersect` (`JTriangle.cs:82-108`) never checks `lambda < 0`.
- **Effect:** a ray starting 5 mm above a ground mesh and pointing up hits it at lambda -0.005.

### H8. Enumerating RigidBodies while removing silently skips bodies (CONFIRMED)
- **Where:** the PartitionedSet enumerator is index based with no version check (`PartitionedSet.cs:91,283`).
- **Measured:** 5 of 10 bodies survived a `foreach ... Remove` loop, with no exception.
- **Same enumerator:** Islands and DynamicTree.Proxies.

### H9. SoftBody only watches vertex 0 (CONFIRMED)
- **Where:** `SoftBody.cs:43,115`.
- **Effect:** removing any other vertex, for example to tear cloth, crashes the next Step part way through.

---

## 3. Medium

### Constraint math and units
- **Softness and Bias depend on the timestep (CONFIRMED).**
  - Every constraint gets the whole step's invDt (`World.Step.cs:179-181`).
  - Effective stiffness is `Bias / (Softness * dt / N)`, so more substeps or a higher framerate silently stiffens every joint.
  - Prowl compensates in `AnimatorRagdoll.Tune` (`AnimatorRagdoll.cs:428`) and `DriveConstraint` (`DriveConstraint.cs:113-194`).
  - `SoftBodies/SpringConstraint.SetSpringParameters` likewise needs the substep dt. The docs say "timestep", and a 1 Hz spring at 4 substeps ran at 0.52 s.
- **Angular rows are scaled inconsistently (CONFIRMED).**
  - FixedAngle feeds the half-angle error into a doubled Jacobian, so WeldJoint corrects at half the documented rate.
  - `HingeAngle.Impulse` reads 2x the real impulse.
  - Hinge is 4x softer than TwistAngle at the same softness value.
- **Angular limits crossing +-180 degrees are accepted but enforced wrongly (CONFIRMED).**
  - A body inside a 90-270 degree hinge range is driven out of it.
  - `From > To` silently locks the joint at the midpoint.
- **Motors inject momentum when their axes are not parallel (CONFIRMED).** A free-floating, motorized UniversalJoint bent 60 degrees spins itself up.

### Contacts, events and sleep
- **Forces and gravity lag a step (CONFIRMED).**
  - They are converted in `RigidBody.Update` after the substeps (`RigidBody.cs:736`) and applied in the next step.
  - A new body gets no gravity on its first step.
  - Changing dt or SubstepCount applies 2-4x for one step.
  - Prowl writes velocities directly in WheelCollider partly because of this.
- **EndCollide is not raised on body, shape or arbiter removal, and arbiters are pooled (CONFIRMED).** Prowl hooks only BeginCollide and polls `GetArbiter` every step (`PhysicsWorld.cs:1817-2088`).
- **Changing a collision filter keeps existing contacts alive (CONFIRMED).** Resting contacts never break, so the old filter stays in effect. Prowl purges them itself (`PhysicsWorld.cs:2090 RemoveFilteredContacts`).
- **Friction and restitution are captured when the arbiter is created (CONFIRMED).** Changing friction during a slide has no effect until the bodies separate.
- **Sleep and wake gaps (CONFIRMED):**
  - Disabling a constraint does not wake its bodies.
  - Teleporting a static body into sleeping bodies does not wake them.
  - `SetActivationState(false)` is a no-op on a moving body.
  - Motor setters do not wake bodies.
  - The Torque setter does not wake the body.
  - `ApplyImpulse(wakeup:false)` still resets the sleep timer.
- **Stabilize does not link restored contacts into islands (PLAUSIBLE race in deterministic multithreaded mode).**

### Collision details
- **Small objects get 1-2 contact points (CONFIRMED).** The absolute manifold epsilons (`CollisionManifold.cs:29`, about 3.2 cm merge distance) collapse contacts on dice and coins.
- **Fixed tolerances assume metres (CONFIRMED).** The contact tolerances (`Contact.cs:597-603`) and the 1 m/s restitution threshold are constants.
- **Unattached shape sweeps report pointA at the TOI instead of the origin (CONFIRMED).** `RigidBodyShape.cs:116`, and SoftBodyShape has the same code.
- **The angular conservative advancement Sweep returns true when iterations run out (CONFIRMED).** It reports hits that never happen.
- **Every Velocity set reinserts the proxy in the tree when speculative contacts are on (CONFIRMED by reading).**

### Math
- **`MathHelper.RotationQuaternion` with negative dt (CONFIRMED).** It uses signed theta (`MathHelper.cs:52`), so the result is badly wrong. `PredictOrientation` exposes it.
- **`JQuaternion.CreateFromToRotation` near antiparallel (CONFIRMED).** Error up to about 5.6 degrees in float.
- **`JVector[i]`, `GetColumn` and `UnsafeGet` have no bounds check (CONFIRMED).** `arr[0][3] = 42` overwrote `arr[1].X`. `Unsafe.AsPointer` on unpinned structs is a GC hole.
- **`JMatrix.Inverse` returns true with garbage for nearly singular matrices (CONFIRMED).** `MathHelper.InverseSymmetric` already does this test correctly.

---

## 4. Low
- A reversed infinite LinearLimit or PointOnLine limit produces NaN (`DistanceLimit.cs:229`).
- DistanceLimit limits are relative to the initial distance, but PointOnLine and PointOnPlane limits are absolute.
- Constraints are never relaxed, only contacts are (`World.Step.cs:649`). PLAUSIBLE energy gain in stiff chains.
- Many setters validate only with `[Conditional("DEBUG")]`, and NuGet ships Release. Examples: softness, bias, Position, SpringConstraint.
- Removed bodies and constraints throw a bare NRE from `JHandle.Data` instead of a clear exception.
- Damping is per step, not per second.
- `JBoundingBox.LargeBox` turns into NaN through `CreateTransformed`.
- `MathHelper.InverseSquareRoot` defaults to 2 sweeps, which leaves an error of 0.4.
- `JAngle.Equals` breaks for NaN, and NaN equality differs between the math types.
- `SlimBag.Trim` can keep stale references alive.
- `ReorderContacts` is O(N^2/1024) on a static body with many contacts. PLAUSIBLE.
- Capsule and Cylinder support maps throw `ArithmeticException` on NaN input through `Math.Sign`.
- GJK Distance reports 2-4 mm overlaps as separated (9 per 100k).
- Ray maxLambda is exclusive while sweep maxLambda is inclusive.
- With USE_DOUBLE_PRECISION, the conversions to Vector3 and Quaternion are implicit even though they drop to float. There is no Matrix4x4 interop.
- `TriangleShape.CalculateMassInertia` throws, which breaks the default AddShape for bodies with triangles. Prowl catches it.

---

## 5. What Prowl patches that Jitter could support natively
Each item names the workaround in Prowl and a general-purpose addition for Jitter.

1. **Motor force cap.**
   - Prowl: `PhysicsConstraint.PerSubstep`.
   - Jitter: fix H1.
2. **Physical soft constraint parameters.**
   - Prowl: `AnimatorRagdoll.Tune`, `DriveConstraint`.
   - Jitter: pass the substep dt to Prepare and Iterate. Add `SetSpring(frequencyHz, dampingRatio)` on soft constraints, computed per substep.
3. **Spring/damper drive.**
   - Prowl: `DriveConstraint.cs`, a full custom six-axis constraint.
   - Jitter: ship a drive constraint with position and rotation targets, stiffness, damping and a max force, and give the motors a servo mode.
4. **Contact lifecycle.**
   - Prowl: polls arbiters for End and emulates Stay.
   - Jitter: raise EndCollide from every removal path, hand out a stable key or snapshot instead of the pooled object, add a contact enumerator on Arbiter, and optionally add a persisted or stay hook.
5. **Filter changes.**
   - Prowl: `RemoveFilteredContacts`.
   - Jitter: break the arbiter when Detect rejects an existing pair, or add `World.InvalidateFilters()`.
6. **Collide connected.**
   - Prowl: `LayerFilter` walks `Constraints` per pair on worker threads.
   - Jitter: add a `Constraint.CollideConnected` flag that Detect checks.
7. **Triggers and sensors.**
   - Prowl: `TriggerVolume` diffs overlap queries every step, and static geometry cannot be reported.
   - Jitter: add `RigidBodyShape.IsSensor`. Run the narrow phase and raise Begin and End events, but keep the pair out of the solver and islands.
8. **Queries.**
   - Prowl: reimplements shape cast, cast all, overlap and start-overlap resolution (`PhysicsWorld.cs:216-422`).
   - Jitter: add `Overlap(shape, pose, sink)`, all-hits ray and sweep variants with sinks, MTD on initial overlap, ignoring separating starts, and a `StartedInside` flag instead of the zero normal.
9. **TransformedShape raycast.**
   - Prowl: the PlacedShape override.
   - Jitter: forward to the inner shape's analytic ray cast, plus fix C3.
10. **Meshes.**
    - Prowl: bakes the transform into vertices and creates one tree leaf per triangle.
    - Jitter: let the edge filter see through TransformedShape, and add a mesh or compound proxy with its own BVH.
11. **Heightfield.**
    - Prowl: TerrainCollisionFilter and a demo-copied triangle support map.
    - Jitter: ship a heightfield proxy and `SupportPrimitives.Triangle`.
12. **Shape metadata.**
    - Prowl: one static body per layer, plus ShapeId dictionaries.
    - Jitter: `Shape.Tag`, per-shape friction and restitution, and a pluggable combine rule (max, min, average, multiply).
13. **Center of mass.**
    - Prowl: offsets every shape and edits raw joint anchor Data.
    - Jitter: compute and expose the COM from `SetMassInertia`, or add a local COM offset, and add `Constraint.ShiftAnchors`.
14. **Forces.**
    - Prowl: wheels write velocities directly.
    - Jitter: convert Force and Torque at the start of Step using the current dt, or add a per-substep force hook.
15. **Wake semantics.**
    - Prowl: `WakeBodies` in every motor setter, and hand-written velocity edits to avoid resetting the sleep timer.
    - Jitter: motor setters wake their bodies, `wakeup:false` leaves the timer alone, and add `ApplyAngularImpulse` and `AddTorque(wakeup)`.
16. **Kinematic platforms.**
    - Jitter: fix H4, and add `MoveKinematic(targetPose, dt)` that derives velocities.
17. **Motor sign convention.**
    - Prowl: negates TargetVelocity everywhere.
    - Jitter: document that the motor drives body2 relative to body1.

---

## 6. Prowl-side issues found during the audit
- `Rigidbody3D.AddTorque` (Force and Acceleration modes, `Rigidbody3D.cs:882,887`) and the Torque setter (`:438`) never wake a sleeping body.
- `Collision.ImpulseMagnitude` sums the last substep's impulse but documents "last step". It under-reports by roughly the substep count, and so do the constraint `Impulse` getters.
- The TriggerVolume cone gizmo draws the base at -h/2. Jitter's cone base is at -h/4, so the gizmo and the query disagree (`TriggerVolume.cs:268`).
- `MovePosition` and `MoveRotation` teleport kinematic bodies with zero velocity, so platforms do not carry riders (`Rigidbody3D.cs:1042-1071`).
- Damping is passed through raw, so it changes with FixedDeltaTime (`Rigidbody3D.cs:643`).
- PLAUSIBLE: `UnregisterTerrain` (`PhysicsWorld.cs:2166`) leaves terrain arbiters behind, so resting bodies may hover on phantom contacts.
- PLAUSIBLE: zero-length capsule queries pass a zero axis to `Quaternion.FromToRotation` (`PhysicsWorld.cs:1352,1385,1634`).
- Stale comment: `CharacterController.cs:982` says Jitter rejects zero-length capsules. 2.9.0 accepts them.
- H2 applies to Prowl directly: toggling IsKinematic on a body jointed to the world destroys its joints.
- Upgrade note: HEAD renames `AngularMotor.MaximumForce` to `MaximumTorque` (the old name is obsolete), and `Remove(body)` and `Remove(constraint)` now throw on a second removal.
