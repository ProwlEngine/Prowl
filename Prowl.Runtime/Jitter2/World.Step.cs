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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.Dynamics.Constraints;
using Jitter2.LinearMath;
using Jitter2.Parallelization;
using Jitter2.UnmanagedMemory;
using ThreadPool = Jitter2.Parallelization.ThreadPool;

namespace Jitter2;

public partial class World
{
    private readonly Stack<Arbiter> deferredArbiters = new();
    private readonly List<JHandle<ContactData>> brokenArbiters = new();

    private Action<Parallel.Batch> integrate;
    private Action<Parallel.Batch> integrateForces;
    private Action<Parallel.Batch> prepareContacts;
    private Action<Parallel.Batch> iterateContacts;
    private Action<Parallel.Batch> updateContacts;
    private Action<Parallel.Batch> prepareConstraints;
    private Action<Parallel.Batch> iterateConstraints;
    private Action<Parallel.Batch> prepareSmallConstraints;
    private Action<Parallel.Batch> iterateSmallConstraints;
    private Action<Parallel.Batch> updateBodies;
    private Action<Parallel.Batch> updateShapes;
    private Action<Parallel.Batch> detectCollisions;

    private int stepper;

    private void InitParallelCallbacks()
    {
        integrate = IntegrateCallback;
        integrateForces = IntegrateForcesCallback;
        prepareContacts = PrepareContactsCallback;
        iterateContacts = IterateContactsCallback;
        prepareConstraints = PrepareConstraintsCallback;
        iterateConstraints = IterateConstraintsCallback;
        prepareSmallConstraints = PrepareSmallConstraintsCallback;
        iterateSmallConstraints = IterateSmallConstraintsCallback;
        updateContacts = UpdateContactsCallback;
        updateBodies = UpdateBodiesCallback;
        updateShapes = UpdateShapesCallback;
        detectCollisions = DetectCollisionsCallback;
    }

    public enum Timings
    {
        UpdateBodies,
        CollisionDetect1,
        CollisionDetect2,
        RemoveArbiter,
        AddArbiter,
        SolveContacts,
        UpdateContacts,
        IntegrateForces,
        Integrate,
        TrimPotentialPairs,
        CheckDeactivation,
        Last
    }

    /// <summary>
    /// Contains timings for the stages of the last call to <see cref="World.Step(float, bool)"/>.
    /// Array elements correspond to the enums in <see cref="Timings"/>. Can be used to identify
    /// bottlenecks.
    /// </summary>
    public double[] DebugTimings { get; } = new double[(int)Timings.Last];

    /// <summary>
    /// Performs a single simulation step.
    /// </summary>
    /// <param name="dt">The duration of time to simulate. This should remain fixed and not exceed 1/60 of a second.</param>
    /// <param name="multiThread">Indicates whether multithreading should be utilized. The behavior of the engine can be modified using <see cref="Parallelization.ThreadPool.Instance"/>.</param>
    public void Step(float dt, bool multiThread = true)
    {
        AssertNullBody();

        if (dt < 0.0f)
        {
            throw new ArgumentException("Time step cannot be negative.", nameof(dt));
        }

        if(dt == 0.0f) return; // nothing to do

        long time = Stopwatch.GetTimestamp();
        double invFrequency = 1.0d / Stopwatch.Frequency;

        void SetTime(Timings type)
        {
            long ctime = Stopwatch.GetTimestamp();
            double delta = (ctime - time) * 1000.0d;
            DebugTimings[(int)type] = delta * invFrequency;
            time = ctime;
        }

        int ssp1 = substeps;
        substep_dt = dt / ssp1;
        step_dt = dt;

        SetTime(Timings.Integrate);

        // Signal the thread pool to spin up threads
        if (multiThread)
        {
            ThreadPool.Instance.SignalWait();
        }

        PreStep?.Invoke(dt);

        SetTime(Timings.Integrate);

        DetectCollisions(multiThread);

        SetTime(Timings.CollisionDetect1);

        HandleDeferredArbiters();
        SetTime(Timings.AddArbiter);

        CheckDeactivation();
        SetTime(Timings.CheckDeactivation);

        // substep_dt = -dt;
        // Integrate(multiThread);

        // Add the new arbiters to their respective rigid body.

        // Go through potential pairs in the collision system and remove
        // pairs which are inactive. This speeds up the enumeration of all
        // collisions of interest.
        TrimPotentialPairs();
        SetTime(Timings.TrimPotentialPairs);

        // Sub-stepping
        // TODO: comment...
        // -> prepare for iteration does calculate new positions, but only linear
        // -> inertia is not transformed in the substeps.
        //
        for (int i = 0; i < ssp1; i++)
        {
            // we need to apply the forces each substep. we can not apply
            // them all at once since this would mess with the warm starting
            // of the solver
            IntegrateForces(multiThread); // FAST SWEEP
            Solve(multiThread, solverIterations); // FAST SWEEP
            Integrate(multiThread); // FAST SWEEP
        }

        SetTime(Timings.SolveContacts);

        RemoveBrokenArbiters();
        SetTime(Timings.RemoveArbiter);

        UpdateContacts(multiThread); // FAST SWEEP
        SetTime(Timings.UpdateContacts);

        // substep_dt = +dt;
        // Integrate(multiThread);
        ForeachActiveBody(multiThread);
        ForeachActiveShape(multiThread);
        SetTime(Timings.UpdateBodies);

        // Perform collision detection.
        // In the callback:
        // If both bodies are static we do nothing.
        // If both bodies are inactive we do nothing.
        // We perform narrow phase detection.
        // New arbiters are added to deferredArbiters
        DynamicTree.Update(multiThread);
        SetTime(Timings.CollisionDetect2);

        PostStep?.Invoke(dt);

        // Signal the threadpool that threads can go into a wait state. If threadModel is set to
        // aggressive this will not happen. Also make sure that a switch from (aggressive, multiThreaded)
        // to (aggressive, sequential) triggers a signalReset here.
        if (ThreadModel == ThreadModelType.Regular || !multiThread)
        {
            ThreadPool.Instance.SignalReset();
        }
    }

    private void TrimPotentialPairs()
    {
        PairHashSet phs = DynamicTree.PotentialPairs;

        // We actually only search 1/100 of the whole potentialPairs Hashset for
        // potentially prunable contacts. No need to sweep through the whole hashset
        // every step.
        const int divisions = 100;
        stepper += 1;

        for (int i = 0; i < phs.Slots.Length / divisions; i++)
        {
            int t = i * stepper % phs.Slots.Length;

            var n = phs.Slots[t];
            if (n.ID == 0) continue;

            Shape shapeA = DynamicTree.Nodes[n.ID1].Proxy;
            Shape shapeB = DynamicTree.Nodes[n.ID2].Proxy;

            if (!shapes.IsActive(shapeA) && !shapes.IsActive(shapeB))
            {
                phs.Remove(t);
                i -= 1;
            }
        }
    }

    private void UpdateShapesCallback(Parallel.Batch batch)
    {
        for (int i = batch.Start; i < batch.End; i++)
        {
            Shape shape = shapes[i];

            shape.UpdateWorldBoundingBox();

            if (shape.RigidBody != null && shape.RigidBody.EnableSpeculativeContacts)
            {
                shape.SweptExpandBoundingBox(shape.RigidBody.Velocity * step_dt);
            }
        }
    }

    private void UpdateBodiesCallback(Parallel.Batch batch)
    {
        for (int i = batch.Start; i < batch.End; i++)
        {
            RigidBody body = bodies[i];
            ref RigidBodyData rigidBody = ref body.Data;

            if (rigidBody.AngularVelocity.LengthSquared() < body.inactiveThresholdAngularSq &&
                rigidBody.Velocity.LengthSquared() < body.inactiveThresholdLinearSq)
            {
                body.sleepTime += step_dt;
            }
            else
            {
                body.sleepTime = 0;
            }

            if (body.sleepTime < body.deactivationTimeThreshold)
            {
                body.island.MarkedAsActive = true;
            }

            if (!rigidBody.IsStatic && rigidBody.IsActive)
            {
                rigidBody.AngularVelocity *= body.angularDampingMultiplier;
                rigidBody.Velocity *= body.linearDampingMultiplier;

                rigidBody.DeltaVelocity = body.Force * rigidBody.InverseMass * substep_dt;
                rigidBody.DeltaAngularVelocity = JVector.Transform(body.Torque, rigidBody.InverseInertiaWorld) * substep_dt;

                if (body.AffectedByGravity)
                {
                    rigidBody.DeltaVelocity += gravity * substep_dt;
                }

                body.Force = JVector.Zero;
                body.Torque = JVector.Zero;

                JMatrix.Multiply(rigidBody.Orientation, body.inverseInertia, out rigidBody.InverseInertiaWorld);
                JMatrix.MultiplyTransposed(rigidBody.InverseInertiaWorld, rigidBody.Orientation, out rigidBody.InverseInertiaWorld);

                rigidBody.InverseMass = body.inverseMass;
            }
        }
    }

    private void PrepareContactsCallback(Parallel.Batch batch)
    {
        float istep_dt = 1.0f / step_dt;

        var span = memContacts.Active[batch.Start..batch.End];

        for (int i = 0; i < span.Length; i++)
        {
            ref ContactData c = ref span[i];
            ref RigidBodyData b1 = ref c.Body1.Data;
            ref RigidBodyData b2 = ref c.Body2.Data;

            LockTwoBody(ref b1, ref b2);

            // Why step_dt and not substep_dt?
            // The contact uses the time to calculate the bias from dt:
            // bias = bias_factor x constraint_error / dt
            // The contact is solved in such a way that the contact points
            // move with 'bias' velocity along their normal after solving.
            // Since collision detection is happening at a rate of step_dt
            // and not substep_dt the penetration magnitude can be large.
            c.PrepareForIteration(istep_dt);
            UnlockTwoBody(ref b1, ref b2);
        }
    }

    private unsafe void PrepareSmallConstraintsCallback(Parallel.Batch batch)
    {
        float istep_dt = 1.0f / step_dt;

        var span = memSmallConstraints.Active[batch.Start..batch.End];

        for (int i = 0; i < span.Length; i++)
        {
            ref SmallConstraintData constraint = ref span[i];
            ref RigidBodyData b1 = ref constraint.Body1.Data;
            ref RigidBodyData b2 = ref constraint.Body2.Data;

            if (constraint.PrepareForIteration == null) continue;

            Debug.Assert(!b1.IsStatic || !b2.IsStatic);

            LockTwoBody(ref b1, ref b2);
            constraint.PrepareForIteration(ref constraint, istep_dt);
            UnlockTwoBody(ref b1, ref b2);
        }
    }

    private unsafe void IterateSmallConstraintsCallback(Parallel.Batch batch)
    {
        float istep_dt = 1.0f / step_dt;

        var span = memSmallConstraints.Active[batch.Start..batch.End];

        for (int i = 0; i < span.Length; i++)
        {
            ref SmallConstraintData constraint = ref span[i];
            ref RigidBodyData b1 = ref constraint.Body1.Data;
            ref RigidBodyData b2 = ref constraint.Body2.Data;

            AssertConstraint(ref b1, ref b2);

            if (constraint.Iterate == null) continue;

            LockTwoBody(ref b1, ref b2);
            constraint.Iterate(ref constraint, istep_dt);
            UnlockTwoBody(ref b1, ref b2);
        }
    }

    private unsafe void PrepareConstraintsCallback(Parallel.Batch batch)
    {
        float istep_dt = 1.0f / step_dt;

        var span = memConstraints.Active[batch.Start..batch.End];

        for (int i = 0; i < span.Length; i++)
        {
            ref ConstraintData constraint = ref span[i];
            ref RigidBodyData b1 = ref constraint.Body1.Data;
            ref RigidBodyData b2 = ref constraint.Body2.Data;

            if (constraint.PrepareForIteration == null) continue;

            Debug.Assert(!b1.IsStatic || !b2.IsStatic);

            LockTwoBody(ref b1, ref b2);
            constraint.PrepareForIteration(ref constraint, istep_dt);
            UnlockTwoBody(ref b1, ref b2);
        }
    }

    private unsafe void IterateConstraintsCallback(Parallel.Batch batch)
    {
        float istep_dt = 1.0f / step_dt;

        var span = memConstraints.Active[batch.Start..batch.End];

        for (int i = 0; i < span.Length; i++)
        {
            ref ConstraintData constraint = ref span[i];
            ref RigidBodyData b1 = ref constraint.Body1.Data;
            ref RigidBodyData b2 = ref constraint.Body2.Data;

            AssertConstraint(ref b1, ref b2);

            if (constraint.Iterate == null) continue;

            LockTwoBody(ref b1, ref b2);
            constraint.Iterate(ref constraint, istep_dt);
            UnlockTwoBody(ref b1, ref b2);
        }
    }

    private void IterateContactsCallback(Parallel.Batch batch)
    {
        var span = memContacts.Active[batch.Start..batch.End];

        for (int i = 0; i < span.Length; i++)
        {
            ref ContactData c = ref span[i];
            ref RigidBodyData b1 = ref c.Body1.Data;
            ref RigidBodyData b2 = ref c.Body2.Data;

            AssertConstraint(ref b1, ref b2);

            LockTwoBody(ref b1, ref b2);
            c.Iterate();
            UnlockTwoBody(ref b1, ref b2);
        }
    }

    private void DetectCollisionsCallback(Parallel.Batch batch)
    {
        PairHashSet phs = DynamicTree.PotentialPairs;

        for (int e = batch.Start; e < batch.End; e++)
        {
            var node = phs.Slots[e];
            if (node.ID == 0) continue;

            Shape shapeA = DynamicTree.Nodes[node.ID1].Proxy;
            Shape shapeB = DynamicTree.Nodes[node.ID2].Proxy;

            if (!shapeA.WorldBoundingBox.Disjoint(shapeB.WorldBoundingBox))
            {
                Detect(shapeA, shapeB);
            }
        }
    }

    private void AssertConstraint(ref RigidBodyData rb1, ref RigidBodyData rb2)
    {
        Debug.Assert(!(rb1.IsStaticOrInactive && rb2.IsStaticOrInactive));

        if (rb1.IsStatic)
        {
            Debug.Assert(rb1.InverseMass == 0.0f);
            Debug.Assert(rb1.InverseInertiaWorld.Equals(JMatrix.Zero));
        }

        if (rb2.IsStatic)
        {
            Debug.Assert(rb2.InverseMass == 0.0f);
            Debug.Assert(rb2.InverseInertiaWorld.Equals(JMatrix.Zero));
        }
    }

    private void AssertNullBody()
    {
        ref RigidBodyData rigidBody = ref NullBody.Data;
        Debug.Assert(rigidBody.IsStatic);
        Debug.Assert(rigidBody.InverseMass == 0.0f);
        Debug.Assert(rigidBody.InverseInertiaWorld.Equals(JMatrix.Zero));
    }

    private void ForeachActiveShape(bool multiThread)
    {
        if (multiThread)
        {
            shapes.ParallelForBatch(256, updateShapes);
        }
        else
        {
            Parallel.Batch batch = new(0, shapes.Active);
            UpdateShapesCallback(batch);
        }
    }

    private void ForeachActiveBody(bool multiThread)
    {
        if (multiThread)
        {
            bodies.ParallelForBatch(256, updateBodies);
        }
        else
        {
            Parallel.Batch batch = new(0, bodies.Active);
            UpdateBodiesCallback(batch);
        }
    }

    private void RemoveBrokenArbiters()
    {
        for (int i = 0; i < brokenArbiters.Count; i++)
        {
            var handle = brokenArbiters[i];
            if (handle.Data.UsageMask == 0)
            {
                Arbiter arb = arbiters[handle.Data.Key];

                AddToActiveList(arb.Body1.island);
                AddToActiveList(arb.Body2.island);

                memContacts.Free(handle);
                IslandHelper.ArbiterRemoved(islands, arb);
                arbiters.Remove(handle.Data.Key);
                Arbiter.Pool.Push(arb);
                arb.Handle = JHandle<ContactData>.Zero;
            }
        }

        brokenArbiters.Clear();
    }

    private void UpdateContactsCallback(Parallel.Batch batch)
    {
        var span = memContacts.Active[batch.Start..batch.End];

        for (int i = 0; i < span.Length; i++)
        {
            // get rid of broken contacts
            ref ContactData cq = ref span[i];
            cq.UpdatePosition();

            if (cq.UsageMask == 0)
            {
                var h = memContacts.GetHandle(ref cq);
                lock (brokenArbiters)
                {
                    brokenArbiters.Add(h);
                }
            }
        }
    }

    private void HandleDeferredArbiters()
    {
        while (deferredArbiters.Count > 0)
        {
            Arbiter arb = deferredArbiters.Pop();
            IslandHelper.ArbiterCreated(islands, arb);

            AddToActiveList(arb.Body1.island);
            AddToActiveList(arb.Body2.island);
        }
    }

    /// <summary>
    /// Spin-wait loop to prevent accessing a body from multiple threads.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void LockTwoBody(ref RigidBodyData b1, ref RigidBodyData b2)
    {
        if (Unsafe.IsAddressGreaterThan(ref b1, ref b2))
        {
            if (!b1.IsStatic)
                while (Interlocked.CompareExchange(ref b1._lockFlag, 1, 0) != 0)
                {
                    Thread.SpinWait(10);
                }

            if (!b2.IsStatic)
                while (Interlocked.CompareExchange(ref b2._lockFlag, 1, 0) != 0)
                {
                    Thread.SpinWait(10);
                }
        }
        else
        {
            if (!b2.IsStatic)
                while (Interlocked.CompareExchange(ref b2._lockFlag, 1, 0) != 0)
                {
                    Thread.SpinWait(10);
                }

            if (!b1.IsStatic)
                while (Interlocked.CompareExchange(ref b1._lockFlag, 1, 0) != 0)
                {
                    Thread.SpinWait(10);
                }
        }
    }

    /// <summary>
    /// Spin-wait loop to prevent accessing a body from multiple threads.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UnlockTwoBody(ref RigidBodyData b1, ref RigidBodyData b2)
    {
        if (Unsafe.IsAddressGreaterThan(ref b1, ref b2))
        {
            if (!b2.IsStatic) Interlocked.Decrement(ref b2._lockFlag);
            if (!b1.IsStatic) Interlocked.Decrement(ref b1._lockFlag);
        }
        else
        {
            if (!b1.IsStatic) Interlocked.Decrement(ref b1._lockFlag);
            if (!b2.IsStatic) Interlocked.Decrement(ref b2._lockFlag);
        }
    }

    private void IntegrateForcesCallback(Parallel.Batch batch)
    {
        var span = memRigidBodies.Active[batch.Start..batch.End];

        for (int i = 0; i < span.Length; i++)
        {
            ref RigidBodyData rigidBody = ref span[i];
            if (rigidBody.IsStaticOrInactive) continue;

            rigidBody.AngularVelocity += rigidBody.DeltaAngularVelocity;
            rigidBody.Velocity += rigidBody.DeltaVelocity;
        }
    }

    private void IntegrateCallback(Parallel.Batch batch)
    {
        var span = memRigidBodies.Active[batch.Start..batch.End];

        for (int i = 0; i < span.Length; i++)
        {
            ref RigidBodyData rigidBody = ref span[i];

            if (rigidBody.IsStatic) continue;

            JVector lvel = rigidBody.Velocity;
            JVector avel = rigidBody.AngularVelocity;

            rigidBody.Position += lvel * substep_dt;

            float angle = avel.Length();
            JVector axis;

            if (angle < 0.001f)
            {
                // use Taylor's expansions of sync function
                // axis = body.angularVelocity * (0.5f * timestep - (timestep * timestep * timestep) * (0.020833333333f) * angle * angle);
                JVector.Multiply(avel,
                    0.5f * substep_dt - substep_dt * substep_dt * substep_dt * 0.020833333333f * angle * angle,
                    out axis);
            }
            else
            {
                // sync(fAngle) = sin(c*fAngle)/t
                JVector.Multiply(avel, (float)Math.Sin(0.5f * angle * substep_dt) / angle, out axis);
            }

            JQuaternion dorn = new(axis.X, axis.Y, axis.Z, (float)Math.Cos(angle * substep_dt * 0.5f));
            JQuaternion.CreateFromMatrix(rigidBody.Orientation, out JQuaternion ornA);

            JQuaternion.Multiply(dorn, ornA, out dorn);

            dorn.Normalize();
            JMatrix.CreateFromQuaternion(dorn, out rigidBody.Orientation);
        }
    }

    private void Solve(bool multiThread, int iterations)
    {
        if (multiThread)
        {
            memContacts.ParallelForBatch(64, prepareContacts, false);
            memConstraints.ParallelForBatch(64, prepareConstraints, false);
            memSmallConstraints.ParallelForBatch(64, prepareSmallConstraints, false);

            ThreadPool.Instance.Execute();

            for (int iter = 0; iter < iterations; iter++)
            {
                memContacts.ParallelForBatch(64, iterateContacts, false);
                memConstraints.ParallelForBatch(64, iterateConstraints, false);
                memSmallConstraints.ParallelForBatch(64, iterateSmallConstraints, false);

                ThreadPool.Instance.Execute();
            }
        }
        else
        {
            Parallel.Batch batchContacts = new(0, memContacts.Active.Length);
            Parallel.Batch batchConstraints = new(0, memConstraints.Active.Length);
            Parallel.Batch batchSmallConstraints = new(0, memSmallConstraints.Active.Length);

            PrepareContactsCallback(batchContacts);
            PrepareConstraintsCallback(batchConstraints);
            PrepareSmallConstraintsCallback(batchSmallConstraints);

            for (int iter = 0; iter < iterations; iter++)
            {
                IterateContactsCallback(batchContacts);
                IterateConstraintsCallback(batchConstraints);
                IterateSmallConstraintsCallback(batchSmallConstraints);
            }
        }
    }

    private void UpdateContacts(bool multiThread)
    {
        if (multiThread)
        {
            memContacts.ParallelForBatch(256, updateContacts);
        }
        else
        {
            Parallel.Batch batch = new(0, memContacts.Active.Length);
            UpdateContactsCallback(batch);
        }
    }

    private void IntegrateForces(bool multiThread)
    {
        if (multiThread)
        {
            memRigidBodies.ParallelForBatch(256, integrateForces);
        }
        else
        {
            IntegrateForcesCallback(new Parallel.Batch(0, memRigidBodies.Active.Length));
        }
    }

    private void Integrate(bool multiThread)
    {
        if (multiThread)
        {
            memRigidBodies.ParallelForBatch(256, integrate);
        }
        else
        {
            IntegrateCallback(new Parallel.Batch(0, memRigidBodies.Active.Length));
        }
    }

    private void DetectCollisions(bool multiThread)
    {
        const int taskThreshold = 1024;

        int numTasks = DynamicTree.PotentialPairs.Slots.Length / taskThreshold + 1;
        numTasks = Math.Min(numTasks, ThreadPool.Instance.ThreadCount);

        if (numTasks > 1 && multiThread)
        {
            Parallel.ForBatch(0, DynamicTree.PotentialPairs.Slots.Length,
                ThreadPool.Instance.ThreadCount, detectCollisions);
        }
        else
        {
            DetectCollisionsCallback(new Parallel.Batch(0, DynamicTree.PotentialPairs.Slots.Length));
        }
    }

    private readonly Stack<Island> inactivateIslands = new();

    private void CheckDeactivation()
    {
        for (int i = 0; i < islands.Active; i++)
        {
            Island island = islands[i];

            bool deactivateIsland = !island.MarkedAsActive;
            if (!AllowDeactivation) deactivateIsland = false;

            // Mark the island as inactive
            // Next frame one active body will be enough to set
            // MarkedAsActive back to true;
            island.MarkedAsActive = false;

            if (!deactivateIsland && !island.NeedsUpdate) continue;

            island.NeedsUpdate = false;

            foreach (RigidBody body in island.bodies)
            {
                ref RigidBodyData rigidBody = ref body.Data;

                if (rigidBody.IsActive != deactivateIsland) continue;

                if (deactivateIsland)
                {
                    rigidBody.IsActive = false;

                    memRigidBodies.MoveToInactive(body.handle);
                    bodies.MoveToInactive(body);

                    if (!body.Data.IsStatic)
                    {
                        foreach (var c in body.Contacts)
                        {
                            memContacts.MoveToInactive(c.Handle);
                        }

                        foreach (var c in body.Constraints)
                        {
                            if (c.IsSmallConstraint)
                            {
                                memSmallConstraints.MoveToInactive(c.SmallHandle);
                            }
                            else
                            {
                                memConstraints.MoveToInactive(c.Handle);
                            }
                        }
                    }

                    foreach (var s in body.shapes)
                    {
                        shapes.MoveToInactive(s);
                    }
                }
                else
                {
                    if (rigidBody.IsStatic) continue;

                    rigidBody.IsActive = true;

                    body.sleepTime = 0;

                    memRigidBodies.MoveToActive(body.handle);
                    bodies.MoveToActive(body);

                    foreach (var c in body.Contacts)
                    {
                        memContacts.MoveToActive(c.Handle);
                    }

                    foreach (var c in body.Constraints)
                    {
                        if (c.IsSmallConstraint)
                        {
                            memSmallConstraints.MoveToActive(c.SmallHandle);
                        }
                        else
                        {
                            memConstraints.MoveToActive(c.Handle);
                        }
                    }

                    foreach (var s in body.shapes)
                    {
                        shapes.MoveToActive(s);
                        DynamicTree.ForceUpdate(s);
                    }
                }
            }

            if (deactivateIsland)
            {
                inactivateIslands.Push(island);
            }
        }

        while (inactivateIslands.Count > 0)
        {
            islands.MoveToInactive(inactivateIslands.Pop());
        }
    }
}