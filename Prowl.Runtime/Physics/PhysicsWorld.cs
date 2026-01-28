// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Jitter2;
using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.LinearMath;

using Prowl.Vector;

namespace Prowl.Runtime;

public class PhysicsWorld
{
    public static void IgnoreCollisionBetween(Rigidbody3D bodyA, Rigidbody3D bodyB) => LayerFilter.IgnoreCollisionBetween(bodyA, bodyB);

    public static void EnableCollisionBetween(Rigidbody3D bodyA, Rigidbody3D bodyB) => LayerFilter.EnableCollisionBetween(bodyA, bodyB);

    public World World { get; private set; }

    /// <summary>
    /// Static rigidbodies indexed by layer. Each layer has its own static rigidbody to ensure collision filtering works correctly.
    /// Orphan colliders (colliders without a Rigidbody3D component) will attach to the static rigidbody for their layer.
    /// </summary>
    private Dictionary<int, Jitter2.Dynamics.RigidBody> _staticRigidbodiesByLayer = new();

    /// <summary>
    /// Composite filter that chains multiple broad phase filters together.
    /// </summary>
    private CompositeBroadPhaseFilter _compositeBroadPhaseFilter;

    public Float3 Gravity = new(0, -9.81f, 0);
    public int SolverIterations = 8;
    public int RelaxIterations = 4;
    public int Substep = 2;
    public bool AllowSleep = true;
    public bool UseMultithreading = true;
    public bool AutoSyncTransforms = true;

    /// <summary>
    /// Event triggered before each physics step.
    /// </summary>
    public event Action<float> PreStep;

    /// <summary>
    /// Event triggered after each physics step.
    /// </summary>
    public event Action<float> PostStep;

    public PhysicsWorld()
    {
        World = new World();

        World.DynamicTree.Filter = World.DefaultDynamicTreeFilter;

        // Set up composite broad phase filter
        _compositeBroadPhaseFilter = new CompositeBroadPhaseFilter();
        _compositeBroadPhaseFilter.AddFilter(new LayerFilter());
        World.BroadPhaseFilter = _compositeBroadPhaseFilter;

        World.NarrowPhaseFilter = new TriangleEdgeCollisionFilter();

        // Hook up physics step events
        World.PreStep += OnPreStep;
        World.PostStep += OnPostStep;
    }

    /// <summary>
    /// Gets or creates a static rigidbody for the specified layer.
    /// Each layer has its own static rigidbody to ensure collision filtering works correctly.
    /// </summary>
    public Jitter2.Dynamics.RigidBody GetOrCreateStaticRigidBody(int layer)
    {
        if (_staticRigidbodiesByLayer.TryGetValue(layer, out var staticBody))
        {
            return staticBody;
        }

        // Create a new static rigidbody for this layer
        staticBody = World.CreateRigidBody();
        staticBody.IsStatic = true;
        staticBody.Tag = new Rigidbody3D.RigidBodyUserData()
        {
            Rigidbody = null, // No Rigidbody3D component associated with this
            InstanceID = layer, // This is just used to sort for collision filtering, it just needs to be a consistent value
            Layer = layer
        };

        _staticRigidbodiesByLayer[layer] = staticBody;
        return staticBody;
    }

    private void OnPreStep(float deltaTime)
    {
        PreStep?.Invoke(deltaTime);
    }

    private void OnPostStep(float deltaTime)
    {
        PostStep?.Invoke(deltaTime);
    }

    public void Clear()
    {
        World?.Clear();

        // Clear the static rigidbodies dictionary - they will be recreated as needed
        _staticRigidbodiesByLayer.Clear();
    }

    public void Update()
    {
        // Configure world settings
        World.AllowDeactivation = AllowSleep;

        World.SubstepCount = Substep;
        World.SolverIterations = (SolverIterations, RelaxIterations);

        World.Gravity = new JVector(Gravity.X, Gravity.Y, Gravity.Z);

        World.Step(Time.FixedDeltaTime, UseMultithreading);
    }

    /// <summary>
    /// Casts a ray against all colliders in this physics world.
    /// </summary>
    public bool Raycast(Float3 origin, Float3 direction)
    {
        direction = Float3.Normalize(direction);
        var jOrigin = new JVector(origin.X, origin.Y, origin.Z);
        var jDirection = new JVector(direction.X, direction.Y, direction.Z);

        return World.DynamicTree.RayCast(jOrigin, jDirection,
            PreFilter, PostFilter,
            out _, out _, out _);
    }

    /// <summary>
    /// Casts a ray against all colliders and returns detailed information about the hit.
    /// </summary>
    public bool Raycast(Float3 origin, Float3 direction, out RaycastHit hitInfo)
    {
        direction = Float3.Normalize(direction);
        var jOrigin = new JVector(origin.X, origin.Y, origin.Z);
        var jDirection = new JVector(direction.X, direction.Y, direction.Z);

        hitInfo = new RaycastHit();
        bool hit = World.DynamicTree.RayCast(jOrigin, jDirection,
            PreFilter, PostFilter,
            out IDynamicTreeProxy shape, out JVector normal, out float lambda);

        if (hit)
        {
            var result = new DynamicTree.RayCastResult
            {
                Entity = shape,
                Lambda = lambda,
                Normal = normal
            };
            hitInfo.SetFromJitterResult(result, origin, direction);
        }

        return hit;
    }

    /// <summary>
    /// Casts a ray within a maximum distance.
    /// </summary>
    public bool Raycast(Float3 origin, Float3 direction, float maxDistance)
    {
        direction = Float3.Normalize(direction);
        var jOrigin = new JVector(origin.X, origin.Y, origin.Z);
        var jDirection = new JVector(direction.X, direction.Y, direction.Z);

        return World.DynamicTree.RayCast(jOrigin, jDirection,
            PreFilter, PostFilter,
            out _, out _, out float dist) && dist <= maxDistance;
    }

    /// <summary>
    /// Casts a ray within a maximum distance and returns detailed information.
    /// </summary>
    public bool Raycast(Float3 origin, Float3 direction, float maxDistance, out RaycastHit hitInfo)
    {
        direction = Float3.Normalize(direction);
        var jOrigin = new JVector(origin.X, origin.Y, origin.Z);
        var jDirection = new JVector(direction.X, direction.Y, direction.Z);

        hitInfo = new RaycastHit();
        bool hit = World.DynamicTree.RayCast(jOrigin, jDirection,
            PreFilter, PostFilter,
            out IDynamicTreeProxy shape, out JVector normal, out float lambda) && lambda <= maxDistance;

        if (hit)
        {
            var result = new DynamicTree.RayCastResult
            {
                Entity = shape,
                Lambda = lambda,
                Normal = normal,
            };
            hitInfo.SetFromJitterResult(result, origin, direction);
        }

        return hit;
    }

    /// <summary>
    /// Casts a ray with layer mask filtering.
    /// </summary>
    public bool Raycast(Float3 origin, Float3 direction, float maxDistance, LayerMask layerMask)
    {
        direction = Float3.Normalize(direction);
        var jOrigin = new JVector(origin.X, origin.Y, origin.Z);
        var jDirection = new JVector(direction.X, direction.Y, direction.Z);

        return World.DynamicTree.RayCast(jOrigin, jDirection,
            shape => PreFilterWithLayer(shape, layerMask), PostFilter,
            out _, out _, out float lambda) && lambda <= maxDistance;
    }

    /// <summary>
    /// Casts a ray with layer mask filtering and returns detailed information.
    /// </summary>
    public bool Raycast(Float3 origin, Float3 direction, out RaycastHit hitInfo, float maxDistance, LayerMask layerMask)
    {
        direction = Float3.Normalize(direction);
        var jOrigin = new JVector(origin.X, origin.Y, origin.Z);
        var jDirection = new JVector(direction.X, direction.Y, direction.Z);

        hitInfo = new RaycastHit();
        bool hit = World.DynamicTree.RayCast(jOrigin, jDirection,
            shape => PreFilterWithLayer(shape, layerMask), PostFilter,
            out IDynamicTreeProxy shape, out JVector normal, out float lambda) && lambda <= maxDistance;

        if (hit)
        {
            var result = new DynamicTree.RayCastResult
            {
                Entity = shape,
                Lambda = lambda,
                Normal = normal,
            };
            hitInfo.SetFromJitterResult(result, origin, direction);
        }

        return hit;
    }

    private static bool PreFilter(IDynamicTreeProxy proxy)
    {
        return true;
    }

    private static bool PreFilterWithLayer(IDynamicTreeProxy proxy, LayerMask layerMask)
    {
        if (proxy is RigidBodyShape shape)
        {
            if (!PreFilter(proxy)) return false;

            var userData = shape.RigidBody.Tag as Rigidbody3D.RigidBodyUserData;

            return layerMask.HasLayer(userData.Layer);
        }

        return false;
    }

    private static bool PostFilter(DynamicTree.RayCastResult result)
    {
        return true;
    }

    #region Shape Casting

    /// <summary>
    /// Generic shape cast that returns all hits along the sweep path.
    /// </summary>
    /// <param name="shape">The shape to cast.</param>
    /// <param name="orientation">The orientation of the casting shape.</param>
    /// <param name="origin">Starting position of the shape.</param>
    /// <param name="direction">Direction to cast the shape.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hits">List to populate with all hits found.</param>
    /// <param name="layerMask">Layer mask for filtering.</param>
    /// <returns>Number of hits found.</returns>
    public int ShapeCastAll(RigidBodyShape shape, Quaternion orientation, Float3 origin, Float3 direction, float maxDistance, List<ShapeCastHit> hits, LayerMask layerMask)
    {
        direction = Float3.Normalize(direction);

        var jOrigin = new JVector(origin.X, origin.Y, origin.Z);
        var jDirection = new JVector(direction.X, direction.Y, direction.Z);
        JVector sweep = jDirection * maxDistance;

        hits.Clear();

        // Get all shapes from the dynamic tree that could potentially be hit
        var potentialShapes = new List<IDynamicTreeProxy>();

        // Create a bounding box that encompasses the entire sweep
        JBoundingBox sweepBox = new();
        shape.CalculateBoundingBox(new JQuaternion(orientation.X, orientation.Y, orientation.Z, orientation.W), jOrigin, out JBoundingBox startBox);
        shape.CalculateBoundingBox(new JQuaternion(orientation.X, orientation.Y, orientation.Z, orientation.W), jOrigin + sweep, out JBoundingBox endBox);

        sweepBox.Min = JVector.Min(startBox.Min, endBox.Min);
        sweepBox.Max = JVector.Max(startBox.Max, endBox.Max);

        World.DynamicTree.Query(potentialShapes, in sweepBox);

        foreach (IDynamicTreeProxy proxy in potentialShapes)
        {
            if (proxy is not RigidBodyShape targetShape) continue;

            // Check layer mask
            var userData = targetShape.RigidBody.Tag as Rigidbody3D.RigidBodyUserData;
            if (userData == null || !layerMask.HasLayer(userData.Layer)) continue;

            Jitter2.Dynamics.RigidBody targetBody = targetShape.RigidBody;

            // Perform sweep test
            bool hit = NarrowPhase.Sweep(
                shape, targetShape,
                new JQuaternion(orientation.X, orientation.Y, orientation.Z, orientation.W), targetBody.Data.Orientation,
                jOrigin, targetBody.Data.Position,
                sweep, JVector.Zero,
                out JVector pointA, out JVector pointB, out JVector normal, out float lambda);

            if (hit && lambda >= 0 && lambda <= 1.0)
            {
                if (normal.LengthSquared() <= 0)
                {
                    _ = NarrowPhase.MprEpa(
                        shape, targetShape,
                        new JQuaternion(orientation.X, orientation.Y, orientation.Z, orientation.W), targetBody.Data.Orientation,
                        jOrigin, targetBody.Data.Position,
                        out JVector _, out JVector _, out normal, out lambda);
                    normal = JVector.Normalize(normal);
                }

                var castHit = new ShapeCastHit
                {
                    Hit = true,
                    Fraction = lambda,
                    Normal = -(new Float3(normal.X, normal.Y, normal.Z)),
                    Point = new Float3(pointA.X, pointA.Y, pointA.Z),
                    HitPoint = new Float3(pointB.X, pointB.Y, pointB.Z),
                    Rigidbody = userData.Rigidbody,
                    Shape = targetShape,
                    Transform = userData.Rigidbody?.GameObject?.Transform
                };
                hits.Add(castHit);
            }
        }

        return hits.Count;
    }

    /// <summary>
    /// Generic shape cast that returns all hits with default layer mask.
    /// </summary>
    public int ShapeCastAll(RigidBodyShape shape, Quaternion orientation, Float3 origin, Float3 direction, float maxDistance, List<ShapeCastHit> hits)
    {
        return ShapeCastAll(shape, orientation, origin, direction, maxDistance, hits, LayerMask.Everything);
    }

    /// <summary>
    /// Generic shape cast that returns only the closest hit.
    /// </summary>
    /// <param name="shape">The shape to cast.</param>
    /// <param name="orientation">The orientation of the casting shape.</param>
    /// <param name="origin">Starting position of the shape.</param>
    /// <param name="direction">Direction to cast the shape.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hitInfo">Information about the closest hit.</param>
    /// <param name="layerMask">Layer mask for filtering.</param>
    /// <returns>True if the shape hit something.</returns>
    public bool ShapeCast(RigidBodyShape shape, Quaternion orientation, Float3 origin, Float3 direction, float maxDistance, out ShapeCastHit hitInfo, LayerMask layerMask)
    {
        var hits = new List<ShapeCastHit>();
        int hitCount = ShapeCastAll(shape, orientation, origin, direction, maxDistance, hits, layerMask);

        if (hitCount > 0)
        {
            // Find closest hit
            ShapeCastHit closest = hits[0];
            for (int i = 1; i < hits.Count; i++)
            {
                if (hits[i].Fraction < closest.Fraction)
                    closest = hits[i];
            }
            hitInfo = closest;
            return true;
        }

        hitInfo = new ShapeCastHit();
        return false;
    }

    /// <summary>
    /// Generic shape cast that returns only the closest hit with default orientation and layer mask.
    /// </summary>
    public bool ShapeCast(RigidBodyShape shape, Float3 origin, Float3 direction, float maxDistance, out ShapeCastHit hitInfo)
    {
        return ShapeCast(shape, Quaternion.Identity, origin, direction, maxDistance, out hitInfo, LayerMask.Everything);
    }

    /// <summary>
    /// Casts a sphere along a direction and returns the closest hit.
    /// </summary>
    /// <param name="origin">Starting position of the sphere center.</param>
    /// <param name="radius">Radius of the sphere.</param>
    /// <param name="direction">Direction to cast the sphere.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hitInfo">Information about what was hit.</param>
    /// <returns>True if the sphere hit something.</returns>
    public bool SphereCast(Float3 origin, float radius, Float3 direction, float maxDistance, out ShapeCastHit hitInfo)
    {
        return SphereCast(origin, radius, direction, maxDistance, out hitInfo, LayerMask.Everything);
    }

    /// <summary>
    /// Casts a sphere along a direction with layer filtering and returns the closest hit.
    /// </summary>
    public bool SphereCast(Float3 origin, float radius, Float3 direction, float maxDistance, out ShapeCastHit hitInfo, LayerMask layerMask)
    {
        var sphere = new SphereShape(radius);
        return ShapeCast(sphere, Quaternion.Identity, origin, direction, maxDistance, out hitInfo, layerMask);
    }

    /// <summary>
    /// Casts a sphere along a direction and returns all hits.
    /// </summary>
    /// <param name="origin">Starting position of the sphere center.</param>
    /// <param name="radius">Radius of the sphere.</param>
    /// <param name="direction">Direction to cast the sphere.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hits">List to populate with all hits found.</param>
    /// <returns>Number of hits found.</returns>
    public int SphereCastAll(Float3 origin, float radius, Float3 direction, float maxDistance, List<ShapeCastHit> hits)
    {
        return SphereCastAll(origin, radius, direction, maxDistance, hits, LayerMask.Everything);
    }

    /// <summary>
    /// Casts a sphere along a direction with layer filtering and returns all hits.
    /// </summary>
    public int SphereCastAll(Float3 origin, float radius, Float3 direction, float maxDistance, List<ShapeCastHit> hits, LayerMask layerMask)
    {
        var sphere = new SphereShape(radius);
        return ShapeCastAll(sphere, Quaternion.Identity, origin, direction, maxDistance, hits, layerMask);
    }

    /// <summary>
    /// Casts a capsule along a direction and returns the closest hit.
    /// </summary>
    /// <param name="point1">Start point of the capsule's line segment.</param>
    /// <param name="point2">End point of the capsule's line segment.</param>
    /// <param name="radius">Radius of the capsule.</param>
    /// <param name="direction">Direction to cast the capsule.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hitInfo">Information about what was hit.</param>
    /// <returns>True if the capsule hit something.</returns>
    public bool CapsuleCast(Float3 point1, Float3 point2, float radius, Float3 direction, float maxDistance, out ShapeCastHit hitInfo)
    {
        return CapsuleCast(point1, point2, radius, direction, maxDistance, out hitInfo, LayerMask.Everything);
    }

    /// <summary>
    /// Casts a capsule along a direction with layer filtering and returns the closest hit.
    /// </summary>
    public bool CapsuleCast(Float3 point1, Float3 point2, float radius, Float3 direction, float maxDistance, out ShapeCastHit hitInfo, LayerMask layerMask)
    {
        // Calculate capsule properties
        Float3 capsuleCenter = (point1 + point2) * 0.5f;
        Float3 capsuleAxis = point2 - point1;
        float capsuleLength = Float3.Length(capsuleAxis);

        // Create a capsule shape (aligned along Y-axis)
        var capsule = new CapsuleShape(radius, capsuleLength);

        // Calculate orientation to align capsule with the segment
        Quaternion capsuleOrientation = CalculateCapsuleOrientation(capsuleAxis, capsuleLength);

        return ShapeCast(capsule, capsuleOrientation, capsuleCenter, direction, maxDistance, out hitInfo, layerMask);
    }

    /// <summary>
    /// Casts a capsule along a direction and returns all hits.
    /// </summary>
    /// <param name="point1">Start point of the capsule's line segment.</param>
    /// <param name="point2">End point of the capsule's line segment.</param>
    /// <param name="radius">Radius of the capsule.</param>
    /// <param name="direction">Direction to cast the capsule.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hits">List to populate with all hits found.</param>
    /// <returns>Number of hits found.</returns>
    public int CapsuleCastAll(Float3 point1, Float3 point2, float radius, Float3 direction, float maxDistance, List<ShapeCastHit> hits)
    {
        return CapsuleCastAll(point1, point2, radius, direction, maxDistance, hits, LayerMask.Everything);
    }

    /// <summary>
    /// Casts a capsule along a direction with layer filtering and returns all hits.
    /// </summary>
    public int CapsuleCastAll(Float3 point1, Float3 point2, float radius, Float3 direction, float maxDistance, List<ShapeCastHit> hits, LayerMask layerMask)
    {
        // Calculate capsule properties
        Float3 capsuleCenter = (point1 + point2) * 0.5f;
        Float3 capsuleAxis = point2 - point1;
        float capsuleLength = Float3.Length(capsuleAxis);

        // Create a capsule shape (aligned along Y-axis)
        var capsule = new CapsuleShape(radius, capsuleLength);

        // Calculate orientation to align capsule with the segment
        Quaternion capsuleOrientation = CalculateCapsuleOrientation(capsuleAxis, capsuleLength);

        return ShapeCastAll(capsule, capsuleOrientation, capsuleCenter, direction, maxDistance, hits, layerMask);
    }

    /// <summary>
    /// Helper method to calculate the orientation needed to align a capsule (Y-axis aligned) with a given axis.
    /// </summary>
    private static Quaternion CalculateCapsuleOrientation(Float3 capsuleAxis, float capsuleLength)
    {
        if (capsuleLength <= 1e-6)
            return Quaternion.Identity;

        Float3 normalizedAxis = capsuleAxis / capsuleLength;
        Float3 yAxis = new(0, 1, 0);

        // If axis is aligned with Y, no rotation needed
        if (Maths.Abs(Float3.Dot(normalizedAxis, yAxis) - 1.0) < 1e-6)
        {
            return Quaternion.Identity;
        }
        // If axis is opposite to Y, rotate 180 degrees around X
        else if (Maths.Abs(Float3.Dot(normalizedAxis, yAxis) + 1.0) < 1e-6)
        {
            return Quaternion.AxisAngle(new Float3(1, 0, 0), Maths.PI);
        }
        // Calculate rotation from Y-axis to the capsule axis
        else
        {
            Float3 rotAxis = Float3.Cross(yAxis, normalizedAxis);
            rotAxis = Float3.Normalize(rotAxis);
            float angle = Maths.Acos(Float3.Dot(yAxis, normalizedAxis));
            return Quaternion.AxisAngle(new Float3(rotAxis.X, rotAxis.Y, rotAxis.Z), angle);
        }
    }

    /// <summary>
    /// Casts a box along a direction and returns the closest hit.
    /// </summary>
    /// <param name="origin">Starting position of the box center.</param>
    /// <param name="size">Size of the box (width, height, depth).</param>
    /// <param name="orientation">Orientation of the box.</param>
    /// <param name="direction">Direction to cast the box.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hitInfo">Information about what was hit.</param>
    /// <returns>True if the box hit something.</returns>
    public bool BoxCast(Float3 origin, Float3 size, Quaternion orientation, Float3 direction, float maxDistance, out ShapeCastHit hitInfo)
    {
        return BoxCast(origin, size, orientation, direction, maxDistance, out hitInfo, LayerMask.Everything);
    }

    /// <summary>
    /// Casts a box along a direction with layer filtering and returns the closest hit.
    /// </summary>
    public bool BoxCast(Float3 origin, Float3 size, Quaternion orientation, Float3 direction, float maxDistance, out ShapeCastHit hitInfo, LayerMask layerMask)
    {
        var box = new BoxShape(size.X, size.Y, size.Z);
        return ShapeCast(box, orientation, origin, direction, maxDistance, out hitInfo, layerMask);
    }

    /// <summary>
    /// Casts a box along a direction and returns all hits.
    /// </summary>
    /// <param name="origin">Starting position of the box center.</param>
    /// <param name="size">Size of the box (width, height, depth).</param>
    /// <param name="orientation">Orientation of the box.</param>
    /// <param name="direction">Direction to cast the box.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hits">List to populate with all hits found.</param>
    /// <returns>Number of hits found.</returns>
    public int BoxCastAll(Float3 origin, Float3 size, Quaternion orientation, Float3 direction, float maxDistance, List<ShapeCastHit> hits)
    {
        return BoxCastAll(origin, size, orientation, direction, maxDistance, hits, LayerMask.Everything);
    }

    /// <summary>
    /// Casts a box along a direction with layer filtering and returns all hits.
    /// </summary>
    public int BoxCastAll(Float3 origin, Float3 size, Quaternion orientation, Float3 direction, float maxDistance, List<ShapeCastHit> hits, LayerMask layerMask)
    {
        var box = new BoxShape(size.X, size.Y, size.Z);
        return ShapeCastAll(box, orientation, origin, direction, maxDistance, hits, layerMask);
    }

    /// <summary>
    /// Casts a cylinder along a direction and returns the closest hit.
    /// </summary>
    /// <param name="origin">Starting position of the cylinder center.</param>
    /// <param name="radius">Radius of the cylinder.</param>
    /// <param name="height">Height of the cylinder.</param>
    /// <param name="orientation">Orientation of the cylinder.</param>
    /// <param name="direction">Direction to cast the cylinder.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hitInfo">Information about what was hit.</param>
    /// <returns>True if the cylinder hit something.</returns>
    public bool CylinderCast(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, out ShapeCastHit hitInfo)
    {
        return CylinderCast(origin, radius, height, orientation, direction, maxDistance, out hitInfo, LayerMask.Everything);
    }

    /// <summary>
    /// Casts a cylinder along a direction with layer filtering and returns the closest hit.
    /// </summary>
    public bool CylinderCast(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, out ShapeCastHit hitInfo, LayerMask layerMask)
    {
        var cylinder = new CylinderShape(height, radius);
        return ShapeCast(cylinder, orientation, origin, direction, maxDistance, out hitInfo, layerMask);
    }

    /// <summary>
    /// Casts a cylinder along a direction and returns all hits.
    /// </summary>
    /// <param name="origin">Starting position of the cylinder center.</param>
    /// <param name="radius">Radius of the cylinder.</param>
    /// <param name="height">Height of the cylinder.</param>
    /// <param name="orientation">Orientation of the cylinder.</param>
    /// <param name="direction">Direction to cast the cylinder.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hits">List to populate with all hits found.</param>
    /// <returns>Number of hits found.</returns>
    public int CylinderCastAll(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, List<ShapeCastHit> hits)
    {
        return CylinderCastAll(origin, radius, height, orientation, direction, maxDistance, hits, LayerMask.Everything);
    }

    /// <summary>
    /// Casts a cylinder along a direction with layer filtering and returns all hits.
    /// </summary>
    public int CylinderCastAll(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, List<ShapeCastHit> hits, LayerMask layerMask)
    {
        var cylinder = new CylinderShape(height, radius);
        return ShapeCastAll(cylinder, orientation, origin, direction, maxDistance, hits, layerMask);
    }

    /// <summary>
    /// Casts a cone along a direction and returns the closest hit.
    /// </summary>
    /// <param name="origin">Starting position of the cone center.</param>
    /// <param name="radius">Base radius of the cone.</param>
    /// <param name="height">Height of the cone.</param>
    /// <param name="orientation">Orientation of the cone.</param>
    /// <param name="direction">Direction to cast the cone.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hitInfo">Information about what was hit.</param>
    /// <returns>True if the cone hit something.</returns>
    public bool ConeCast(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, out ShapeCastHit hitInfo)
    {
        return ConeCast(origin, radius, height, orientation, direction, maxDistance, out hitInfo, LayerMask.Everything);
    }

    /// <summary>
    /// Casts a cone along a direction with layer filtering and returns the closest hit.
    /// </summary>
    public bool ConeCast(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, out ShapeCastHit hitInfo, LayerMask layerMask)
    {
        var cone = new ConeShape(radius, height);
        return ShapeCast(cone, orientation, origin, direction, maxDistance, out hitInfo, layerMask);
    }

    /// <summary>
    /// Casts a cone along a direction and returns all hits.
    /// </summary>
    /// <param name="origin">Starting position of the cone center.</param>
    /// <param name="radius">Base radius of the cone.</param>
    /// <param name="height">Height of the cone.</param>
    /// <param name="orientation">Orientation of the cone.</param>
    /// <param name="direction">Direction to cast the cone.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hits">List to populate with all hits found.</param>
    /// <returns>Number of hits found.</returns>
    public int ConeCastAll(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, List<ShapeCastHit> hits)
    {
        return ConeCastAll(origin, radius, height, orientation, direction, maxDistance, hits, LayerMask.Everything);
    }

    /// <summary>
    /// Casts a cone along a direction with layer filtering and returns all hits.
    /// </summary>
    public int ConeCastAll(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, List<ShapeCastHit> hits, LayerMask layerMask)
    {
        var cone = new ConeShape(radius, height);
        return ShapeCastAll(cone, orientation, origin, direction, maxDistance, hits, layerMask);
    }

    #endregion

    #region Overlap Queries

    /// <summary>
    /// Generic overlap query that returns all colliders overlapping the given shape.
    /// </summary>
    /// <param name="shape">The shape to test for overlaps.</param>
    /// <param name="orientation">The orientation of the shape.</param>
    /// <param name="position">Position of the shape.</param>
    /// <param name="hits">List to populate with all overlapping colliders.</param>
    /// <param name="layerMask">Layer mask for filtering.</param>
    /// <returns>Number of overlapping colliders found.</returns>
    public int Overlap(RigidBodyShape shape, Quaternion orientation, Float3 position, List<ShapeCastHit> hits, LayerMask layerMask)
    {
        var jPosition = new JVector(position.X, position.Y, position.Z);
        hits.Clear();

        // Get all shapes from the dynamic tree that could potentially overlap
        var potentialShapes = new List<IDynamicTreeProxy>();

        // Create a bounding box for the shape
        shape.CalculateBoundingBox(new JQuaternion(orientation.X, orientation.Y, orientation.Z, orientation.W), jPosition, out JBoundingBox shapeBounds);
        World.DynamicTree.Query(potentialShapes, in shapeBounds);

        foreach (IDynamicTreeProxy proxy in potentialShapes)
        {
            if (proxy is not RigidBodyShape targetShape) continue;

            // Check layer mask
            var userData = targetShape.RigidBody.Tag as Rigidbody3D.RigidBodyUserData;
            if (userData == null || !layerMask.HasLayer(userData.Layer)) continue;

            Jitter2.Dynamics.RigidBody targetBody = targetShape.RigidBody;

            // Perform overlap test using sweep with zero distance
            bool overlaps = NarrowPhase.MprEpa(
                shape, targetShape,
                new JQuaternion(orientation.X, orientation.Y, orientation.Z, orientation.W), targetBody.Data.Orientation,
                jPosition, targetBody.Data.Position,
                out JVector pointA, out JVector pointB, out JVector normal, out float penetration);

            if (overlaps && penetration > 0)
            {
                var hit = new ShapeCastHit
                {
                    Hit = true,
                    Fraction = 0,
                    Penetration = penetration,
                    Normal = -(new Float3(normal.X, normal.Y, normal.Z)),
                    Point = new Float3(pointA.X, pointA.Y, pointA.Z),
                    HitPoint = new Float3(pointB.X, pointB.Y, pointB.Z),
                    Rigidbody = userData.Rigidbody,
                    Shape = targetShape,
                    Transform = userData.Rigidbody?.GameObject?.Transform
                };
                hits.Add(hit);
            }
        }

        return hits.Count;
    }

    /// <summary>
    /// Generic overlap query with default layer mask.
    /// </summary>
    public int Overlap(RigidBodyShape shape, Quaternion orientation, Float3 position, List<ShapeCastHit> hits)
    {
        return Overlap(shape, orientation, position, hits, LayerMask.Everything);
    }

    /// <summary>
    /// Tests if a sphere overlaps with any colliders.
    /// </summary>
    /// <param name="position">Center position of the sphere.</param>
    /// <param name="radius">Radius of the sphere.</param>
    /// <param name="hits">List to populate with all overlapping colliders.</param>
    /// <returns>Number of overlapping colliders found.</returns>
    public int OverlapSphere(Float3 position, float radius, List<ShapeCastHit> hits)
    {
        return OverlapSphere(position, radius, hits, LayerMask.Everything);
    }

    /// <summary>
    /// Tests if a sphere overlaps with any colliders with layer filtering.
    /// </summary>
    public int OverlapSphere(Float3 position, float radius, List<ShapeCastHit> hits, LayerMask layerMask)
    {
        var sphere = new SphereShape(radius);
        return Overlap(sphere, Quaternion.Identity, position, hits, layerMask);
    }

    /// <summary>
    /// Tests if a capsule overlaps with any colliders.
    /// </summary>
    /// <param name="point1">Start point of the capsule's line segment.</param>
    /// <param name="point2">End point of the capsule's line segment.</param>
    /// <param name="radius">Radius of the capsule.</param>
    /// <param name="hits">List to populate with all overlapping colliders.</param>
    /// <returns>Number of overlapping colliders found.</returns>
    public int OverlapCapsule(Float3 point1, Float3 point2, float radius, List<ShapeCastHit> hits)
    {
        return OverlapCapsule(point1, point2, radius, hits, LayerMask.Everything);
    }

    /// <summary>
    /// Tests if a capsule overlaps with any colliders with layer filtering.
    /// </summary>
    public int OverlapCapsule(Float3 point1, Float3 point2, float radius, List<ShapeCastHit> hits, LayerMask layerMask)
    {
        // Calculate capsule properties
        Float3 capsuleCenter = (point1 + point2) * 0.5f;
        Float3 capsuleAxis = point2 - point1;
        float capsuleLength = Float3.Length(capsuleAxis);

        // Create a capsule shape (aligned along Y-axis)
        var capsule = new CapsuleShape(radius, capsuleLength);

        // Calculate orientation to align capsule with the segment
        Quaternion capsuleOrientation = CalculateCapsuleOrientation(capsuleAxis, capsuleLength);

        return Overlap(capsule, capsuleOrientation, capsuleCenter, hits, layerMask);
    }

    /// <summary>
    /// Tests if a box overlaps with any colliders.
    /// </summary>
    /// <param name="position">Center position of the box.</param>
    /// <param name="size">Size of the box (width, height, depth).</param>
    /// <param name="orientation">Orientation of the box.</param>
    /// <param name="hits">List to populate with all overlapping colliders.</param>
    /// <returns>Number of overlapping colliders found.</returns>
    public int OverlapBox(Float3 position, Float3 size, Quaternion orientation, List<ShapeCastHit> hits)
    {
        return OverlapBox(position, size, orientation, hits, LayerMask.Everything);
    }

    /// <summary>
    /// Tests if a box overlaps with any colliders with layer filtering.
    /// </summary>
    public int OverlapBox(Float3 position, Float3 size, Quaternion orientation, List<ShapeCastHit> hits, LayerMask layerMask)
    {
        var box = new BoxShape(size.X, size.Y, size.Z);
        return Overlap(box, orientation, position, hits, layerMask);
    }

    /// <summary>
    /// Tests if a cylinder overlaps with any colliders.
    /// </summary>
    /// <param name="position">Center position of the cylinder.</param>
    /// <param name="radius">Radius of the cylinder.</param>
    /// <param name="height">Height of the cylinder.</param>
    /// <param name="orientation">Orientation of the cylinder.</param>
    /// <param name="hits">List to populate with all overlapping colliders.</param>
    /// <returns>Number of overlapping colliders found.</returns>
    public int OverlapCylinder(Float3 position, float radius, float height, Quaternion orientation, List<ShapeCastHit> hits)
    {
        return OverlapCylinder(position, radius, height, orientation, hits, LayerMask.Everything);
    }

    /// <summary>
    /// Tests if a cylinder overlaps with any colliders with layer filtering.
    /// </summary>
    public int OverlapCylinder(Float3 position, float radius, float height, Quaternion orientation, List<ShapeCastHit> hits, LayerMask layerMask)
    {
        var cylinder = new CylinderShape(height, radius);
        return Overlap(cylinder, orientation, position, hits, layerMask);
    }

    /// <summary>
    /// Tests if a cone overlaps with any colliders.
    /// </summary>
    /// <param name="position">Center position of the cone.</param>
    /// <param name="radius">Base radius of the cone.</param>
    /// <param name="height">Height of the cone.</param>
    /// <param name="orientation">Orientation of the cone.</param>
    /// <param name="hits">List to populate with all overlapping colliders.</param>
    /// <returns>Number of overlapping colliders found.</returns>
    public int OverlapCone(Float3 position, float radius, float height, Quaternion orientation, List<ShapeCastHit> hits)
    {
        return OverlapCone(position, radius, height, orientation, hits, LayerMask.Everything);
    }

    /// <summary>
    /// Tests if a cone overlaps with any colliders with layer filtering.
    /// </summary>
    public int OverlapCone(Float3 position, float radius, float height, Quaternion orientation, List<ShapeCastHit> hits, LayerMask layerMask)
    {
        var cone = new ConeShape(radius, height);
        return Overlap(cone, orientation, position, hits, layerMask);
    }

    #endregion

    #region Check Queries

    /// <summary>
    /// Checks if a sphere overlaps with any colliders.
    /// </summary>
    /// <param name="position">Center position of the sphere.</param>
    /// <param name="radius">Radius of the sphere.</param>
    /// <returns>True if the sphere overlaps with any collider.</returns>
    public bool CheckSphere(Float3 position, float radius)
    {
        return CheckSphere(position, radius, LayerMask.Everything);
    }

    /// <summary>
    /// Checks if a sphere overlaps with any colliders with layer filtering.
    /// </summary>
    public bool CheckSphere(Float3 position, float radius, LayerMask layerMask)
    {
        var hits = new List<ShapeCastHit>();
        return OverlapSphere(position, radius, hits, layerMask) > 0;
    }

    /// <summary>
    /// Checks if a capsule overlaps with any colliders.
    /// </summary>
    /// <param name="point1">Start point of the capsule's line segment.</param>
    /// <param name="point2">End point of the capsule's line segment.</param>
    /// <param name="radius">Radius of the capsule.</param>
    /// <returns>True if the capsule overlaps with any collider.</returns>
    public bool CheckCapsule(Float3 point1, Float3 point2, float radius)
    {
        return CheckCapsule(point1, point2, radius, LayerMask.Everything);
    }

    /// <summary>
    /// Checks if a capsule overlaps with any colliders with layer filtering.
    /// </summary>
    public bool CheckCapsule(Float3 point1, Float3 point2, float radius, LayerMask layerMask)
    {
        var hits = new List<ShapeCastHit>();
        return OverlapCapsule(point1, point2, radius, hits, layerMask) > 0;
    }

    /// <summary>
    /// Checks if a box overlaps with any colliders.
    /// </summary>
    /// <param name="position">Center position of the box.</param>
    /// <param name="size">Size of the box (width, height, depth).</param>
    /// <param name="orientation">Orientation of the box.</param>
    /// <returns>True if the box overlaps with any collider.</returns>
    public bool CheckBox(Float3 position, Float3 size, Quaternion orientation)
    {
        return CheckBox(position, size, orientation, LayerMask.Everything);
    }

    /// <summary>
    /// Checks if a box overlaps with any colliders with layer filtering.
    /// </summary>
    public bool CheckBox(Float3 position, Float3 size, Quaternion orientation, LayerMask layerMask)
    {
        var hits = new List<ShapeCastHit>();
        return OverlapBox(position, size, orientation, hits, layerMask) > 0;
    }

    /// <summary>
    /// Checks if a cylinder overlaps with any colliders.
    /// </summary>
    /// <param name="position">Center position of the cylinder.</param>
    /// <param name="radius">Radius of the cylinder.</param>
    /// <param name="height">Height of the cylinder.</param>
    /// <param name="orientation">Orientation of the cylinder.</param>
    /// <returns>True if the cylinder overlaps with any collider.</returns>
    public bool CheckCylinder(Float3 position, float radius, float height, Quaternion orientation)
    {
        return CheckCylinder(position, radius, height, orientation, LayerMask.Everything);
    }

    /// <summary>
    /// Checks if a cylinder overlaps with any colliders with layer filtering.
    /// </summary>
    public bool CheckCylinder(Float3 position, float radius, float height, Quaternion orientation, LayerMask layerMask)
    {
        var hits = new List<ShapeCastHit>();
        return OverlapCylinder(position, radius, height, orientation, hits, layerMask) > 0;
    }

    /// <summary>
    /// Checks if a cone overlaps with any colliders.
    /// </summary>
    /// <param name="position">Center position of the cone.</param>
    /// <param name="radius">Base radius of the cone.</param>
    /// <param name="height">Height of the cone.</param>
    /// <param name="orientation">Orientation of the cone.</param>
    /// <returns>True if the cone overlaps with any collider.</returns>
    public bool CheckCone(Float3 position, float radius, float height, Quaternion orientation)
    {
        return CheckCone(position, radius, height, orientation, LayerMask.Everything);
    }

    /// <summary>
    /// Checks if a cone overlaps with any colliders with layer filtering.
    /// </summary>
    public bool CheckCone(Float3 position, float radius, float height, Quaternion orientation, LayerMask layerMask)
    {
        var hits = new List<ShapeCastHit>();
        return OverlapCone(position, radius, height, orientation, hits, layerMask) > 0;
    }

    #endregion

    #region Terrain Collision

    /// <summary>
    /// Registers a terrain collider with the physics world.
    /// </summary>
    /// <param name="heightmapProxy">The terrain heightmap proxy for raycasting.</param>
    /// <param name="collisionFilter">The terrain collision filter for broad phase collision detection.</param>
    public void RegisterTerrain(TerrainHeightmapProxy heightmapProxy, TerrainCollisionFilter collisionFilter)
    {
        if (heightmapProxy == null || collisionFilter == null)
            return;

        // Add the heightmap proxy to the dynamic tree for raycasting
        World.DynamicTree.AddProxy(heightmapProxy, false);

        // Add the terrain collision filter to the composite filter
        // Terrain filters should be processed before the layer filter
        _compositeBroadPhaseFilter.AddFilter(collisionFilter);
    }

    /// <summary>
    /// Unregisters a terrain collider from the physics world.
    /// </summary>
    /// <param name="heightmapProxy">The terrain heightmap proxy to remove.</param>
    /// <param name="collisionFilter">The terrain collision filter to remove.</param>
    public void UnregisterTerrain(TerrainHeightmapProxy heightmapProxy, TerrainCollisionFilter collisionFilter)
    {
        if (heightmapProxy == null || collisionFilter == null)
            return;

        // Remove the heightmap proxy from the dynamic tree
        if (heightmapProxy.SetIndex != -1)
        {
            World.DynamicTree.RemoveProxy(heightmapProxy);
        }

        // Remove the terrain collision filter from the composite filter
        _compositeBroadPhaseFilter.RemoveFilter(collisionFilter);
    }

    #endregion
}
