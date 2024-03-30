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

#pragma warning disable CS8618 // InitParallelCallbacks() - https://github.com/dotnet/roslyn/issues/32358

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.DataStructures;
using Jitter2.Dynamics;
using Jitter2.Dynamics.Constraints;
using Jitter2.LinearMath;
using Jitter2.UnmanagedMemory;

namespace Jitter2;

/// <summary>
/// Represents a simulation environment that holds and manages the state of all simulation objects.
/// </summary>
public partial class World
{
    public enum ThreadModelType
    {
        Regular,
        Persistent
    }

    /// <summary>
    /// Provides access to objects in unmanaged memory. This operation is potentially unsafe.
    /// </summary>
    public readonly struct SpanData
    {
        private readonly World world;

        public SpanData(World world)
        {
            this.world = world;
        }

        public readonly Span<RigidBodyData> ActiveRigidBodies => world.memRigidBodies.Active;
        public readonly Span<RigidBodyData> RigidBodies => world.memRigidBodies.Elements;

        public readonly Span<ContactData> ActiveContacts => world.memContacts.Active;
        public readonly Span<ContactData> Contacts => world.memContacts.Elements;

        public readonly Span<ConstraintData> ActiveConstraints => world.memConstraints.Active;
        public readonly Span<ConstraintData> Constraints => world.memConstraints.Elements;

        public readonly Span<SmallConstraintData> ActiveSmallConstraints => world.memSmallConstraints.Active;
        public readonly Span<SmallConstraintData> SmallConstraints => world.memSmallConstraints.Elements;
    }

    private readonly UnmanagedActiveList<ContactData> memContacts;
    private readonly UnmanagedActiveList<RigidBodyData> memRigidBodies;
    private readonly UnmanagedActiveList<ConstraintData> memConstraints;
    private readonly UnmanagedActiveList<SmallConstraintData> memSmallConstraints;

    public delegate void WorldStep(float dt);

    // Post- and Pre-step
    public event WorldStep? PreStep;
    public event WorldStep? PostStep;

    /// <summary>
    /// Grants access to objects residing in unmanaged memory. This operation can be potentially unsafe. Utilize
    /// the corresponding native properties where possible to mitigate risk.
    /// </summary>
    public SpanData RawData => new(this);

    private readonly Dictionary<ArbiterKey, Arbiter> arbiters = new();

    private readonly ActiveList<Island> islands = new();
    private readonly ActiveList<RigidBody> bodies = new();
    private readonly ActiveList<Shape> shapes = new();

    private static ulong _idCounter;

    /// <summary>
    /// Generates a unique ID.
    /// </summary>
    public static ulong RequestId()
    {
        return Interlocked.Increment(ref _idCounter);
    }

    /// <summary>
    /// Generates a range of unique IDs.
    /// </summary>
    /// <param name="count">The number of IDs to generate.</param>
    /// <returns>A tuple containing the minimum and maximum request IDs in the generated range. The upper
    /// bound is exclusive.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when count is less than 1.</exception>
    public static (ulong min, ulong max) RequestId(int count)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater zero.");
        ulong count64 = (ulong)count;
        ulong max = Interlocked.Add(ref _idCounter, count64) + 1;
        return (max - count64, max);
    }

    /// <summary>
    /// Defines the two available thread models. The <see cref="ThreadModelType.Persistent"/> model keeps the worker
    /// threads active continuously, even when the <see cref="World.Step(float, bool)"/> is not in operation, which might
    /// consume more CPU cycles and possibly affect the performance of other operations such as rendering. However, it ensures that the threads
    /// remain 'warm' for the next invocation of <see cref="World.Step(float, bool)"/>. Conversely, the <see cref="ThreadModelType.Regular"/> model allows
    /// the worker threads to yield and undertake other tasks.
    /// </summary>
    public ThreadModelType ThreadModel { get; set; } = ThreadModelType.Regular;

    /// <summary>
    /// All collision islands in this world.
    /// </summary>
    public ReadOnlyActiveList<Island> Islands { get; private set; }

    /// <summary>
    /// All rigid bodies in this world.
    /// </summary>
    public ReadOnlyActiveList<RigidBody> RigidBodies { get; private set; }

    /// <summary>
    /// All shapes in this world.
    /// </summary>
    public ReadOnlyActiveList<Shape> Shapes { get; private set; }

    /// <summary>
    /// Access to the <see cref="DynamicTree"/> instance. The instance
    /// should only be modified by Jitter.
    /// </summary>
    public readonly DynamicTree<Shape> DynamicTree;

    /// <summary>
    /// A fixed body, pinned to the world. Can be used to create constraints with.
    /// </summary>
    public RigidBody NullBody { get; }

    /// <summary>
    /// Specifies whether the deactivation mechanism of Jitter is enabled.
    /// Does not activate inactive objects if set to false.
    /// </summary>
    public bool AllowDeactivation { get; set; } = true;

    /// <summary>
    /// Number of iterations per substep (see <see cref="World.NumberSubsteps"/>).
    /// </summary>
    /// <value></value>
    public int SolverIterations
    {
        get => solverIterations;
        set
        {
            if (value < 1)
            {
                throw new ArgumentException("Value can not be smaller than 1.",
                    nameof(SolverIterations));
            }

            solverIterations = value;
        }
    }

    /// <summary>
    /// The number of substeps for each call to <see cref="World.Step(float, bool)"/>.
    /// Substepping is deactivated when set to one.
    /// </summary>
    public int NumberSubsteps
    {
        get => substeps;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value),
                    "The number of substeps has to be larger than zero.");
            }

            substeps = value;
        }
    }

    private JVector gravity = new(0, -9.81f, 0);

    /// <summary>
    /// Default gravity, see also <see cref="RigidBody.AffectedByGravity"/>.
    /// </summary>
    public JVector Gravity
    {
        get => gravity;
        set => gravity = value;
    }

    // Make this global since it is used by nearly every method called
    // in World.Step.
    private volatile int solverIterations = 8;
    private volatile int substeps = 1;

    private volatile float substep_dt = 1.0f / 100.0f;
    private volatile float step_dt = 1.0f / 100.0f;

    /// <summary>
    /// Uses a slower alternative narrow phase collision detection method, instead
    /// of the default Minkowski Portal Refinement solver. Used mainly as a reference
    /// to rule out that bugs are caused by the narrow phase collision.
    /// </summary>
    public bool UseFullEPASolver { get; set; }

    /// <summary>
    /// Creates an instance of the World class. As Jitter utilizes a distinct memory model, it is necessary to specify
    /// the maximum number of instances for <see cref="RigidBody"/>, <see cref="ContactData"/>, and <see cref="Constraint"/>.
    /// </summary>
    public World(int numBodies = 32768, int numContacts = 65536, int numConstraints = 32768)
    {
        // int numBodies = 32768, int numContacts = 65536, int numConstraints = 32768
        // with this choice (32768 + 65536 + 2 x 32768) x 8 Bytes = 1280 KB are allocated on the heap.
        memRigidBodies = new UnmanagedActiveList<RigidBodyData>(numBodies);
        memContacts = new UnmanagedActiveList<ContactData>(numContacts);
        memConstraints = new UnmanagedActiveList<ConstraintData>(numConstraints);
        memSmallConstraints = new UnmanagedActiveList<SmallConstraintData>(numConstraints);

        InitParallelCallbacks();

        Islands = new ReadOnlyActiveList<Island>(islands);
        RigidBodies = new ReadOnlyActiveList<RigidBody>(bodies);

        NullBody = CreateRigidBody();
        NullBody.IsStatic = true;

        Shapes = new ReadOnlyActiveList<Shape>(shapes);

        DynamicTree = new DynamicTree<Shape>(shapes, DefaultDynamicTreeFilter);
    }

    public static bool DefaultDynamicTreeFilter(Shape shapeA, Shape shapeB)
    {
        return shapeA.RigidBody != shapeB.RigidBody;
    }

    /// <summary>
    /// Removes all entities from the simulation world.
    /// </summary>
    public void Clear()
    {
        // create a copy, since we are going to modify the list
        Stack<RigidBody> bodyStack = new(bodies);

        while (bodyStack.Count > 0)
        {
            Remove(bodyStack.Pop());
        }

        // Left-over shapes not associated with a rigid body.
        Stack<Shape> shapeStack = new Stack<Shape>(shapes);

        while (shapeStack.Count > 0)
        {
            Remove(shapeStack.Pop());
        }
    }

    /// <summary>
    /// Removes the specified body from the world. This operation also automatically discards any associated contacts
    /// and constraints.
    /// </summary>
    public void Remove(RigidBody body)
    {
        // No need to copy the hashset content first. Removing while iterating does not invalidate
        // the enumerator any longer, see https://github.com/dotnet/runtime/pull/37180
        // This comes in very handy for us.

        foreach (var constraint in body.Constraints)
        {
            Remove(constraint);
        }

        foreach (var shape in body.Shapes)
        {
            DynamicTree.RemoveProxy(shape);
            shapes.Remove(shape);
            shape.DetachRigidBody();
        }

        foreach (var contact in body.Contacts)
        {
            Remove(contact);
        }

        if (body == NullBody) return;

        memRigidBodies.Free(body.handle);

        // we must be our own island..
        Debug.Assert(body.island is { bodies.Count: 1 });

        body.handle = JHandle<RigidBodyData>.Zero;

        IslandHelper.BodyRemoved(islands, body);

        bodies.Remove(body);
    }

    /// <summary>
    /// Removes a specific constraint from the world. For temporary deactivation of constraints, consider using the
    /// <see cref="Constraint.IsEnabled"/> property.
    /// </summary>
    /// <param name="constraint">The constraint to be removed.</param>
    public void Remove(Constraint constraint)
    {
        ActivateBodyNextStep(constraint.Body1);
        ActivateBodyNextStep(constraint.Body2);

        IslandHelper.ConstraintRemoved(islands, constraint);

        if (constraint.IsSmallConstraint)
        {
            memSmallConstraints.Free(constraint.SmallHandle);
        }
        else
        {
            memConstraints.Free(constraint.Handle);
        }

        constraint.Handle = JHandle<ConstraintData>.Zero;
    }

    /// <summary>
    /// Removes a particular arbiter from the world.
    /// </summary>
    public void Remove(Arbiter arbiter)
    {
        ActivateBodyNextStep(arbiter.Body1);
        ActivateBodyNextStep(arbiter.Body2);

        IslandHelper.ArbiterRemoved(islands, arbiter);
        arbiters.Remove(arbiter.Handle.Data.Key);

        brokenArbiters.Remove(arbiter.Handle);
        memContacts.Free(arbiter.Handle);

        Arbiter.Pool.Push(arbiter);

        arbiter.Handle = JHandle<ContactData>.Zero;
    }

    internal void InternalRemoveShape(Shape shape)
    {
        shapes.Remove(shape);
    }

    internal void UpdateShape(Shape shape)
    {
        shape.UpdateWorldBoundingBox();
        DynamicTree.Update(shape);
    }

    /// <summary>
    /// Add a shape not associated with a rigid body to the world.
    /// </summary>
    public void AddShape(Shape shape, bool active = true)
    {
        if (shape.IsRegistered)
        {
            throw new ArgumentException("Shape can not be added. Is the shape already registered?");
        }

        shapes.Add(shape, active);
        shape.UpdateWorldBoundingBox();
        DynamicTree.AddProxy(shape);
    }

    public void Remove(Shape shape)
    {
        if (shape.RigidBody != null)
        {
            throw new ArgumentException("Shape can not be removed because it is attached to a rigid body.");
        }

        if ((shape as IListIndex).ListIndex == -1)
        {
            throw new ArgumentException("Shape can not be removed because it is not registered.");
        }

        DynamicTree.RemoveProxy(shape);
        shapes.Remove(shape);
    }

    public void DeactivateShape(Shape shape)
    {
        if (shape.RigidBody != null)
        {
            throw new InvalidOperationException(
                "Can not modify activation state if a shape which is attached to a rigid body.");
        }

        shapes.MoveToInactive(shape);
    }

    public void ActivateShape(Shape shape)
    {
        if (shape.RigidBody != null)
        {
            throw new InvalidOperationException(
                "Can not modify activation state of a shape which is attached to a rigid body.");
        }

        shapes.MoveToActive(shape);
        DynamicTree.ForceUpdate(shape);
    }

    internal void ActivateBodyNextStep(RigidBody body)
    {
        body.sleepTime = 0;
        AddToActiveList(body.island);
    }

    internal void DeactivateBodyNextStep(RigidBody body)
    {
        body.sleepTime = float.MaxValue / 2.0f;
    }

    /// <summary>
    /// Constructs a constraint of the specified type. After creation, it is mandatory to initialize the constraint using the Constraint.Initialize method.
    /// </summary>
    /// <typeparam name="T">The specific type of constraint to create.</typeparam>
    /// <param name="body1">The first rigid body involved in the constraint.</param>
    /// <param name="body2">The second rigid body involved in the constraint.</param>
    /// <returns>A new instance of the specified constraint type.</returns>
    public T CreateConstraint<T>(RigidBody body1, RigidBody body2) where T : Constraint, new()
    {
        T constraint = new();

        if (constraint.IsSmallConstraint)
        {
            constraint.Create(memSmallConstraints.Allocate(true, true), body1, body2);
        }
        else
        {
            constraint.Create(memConstraints.Allocate(true, true), body1, body2);
        }

        IslandHelper.ConstraintCreated(islands, constraint);

        AddToActiveList(body1.island);
        AddToActiveList(body2.island);

        return constraint;
    }

    private void AddToActiveList(Island island)
    {
        island.MarkedAsActive = true;
        island.NeedsUpdate = true;
        islands.MoveToActive(island);
    }

    /// <summary>
    /// Creates and adds a new rigid body to the simulation world.
    /// </summary>
    /// <returns>A newly created instance of <see cref="RigidBody"/>.</returns>
    public RigidBody CreateRigidBody()
    {
        RigidBody body = new(memRigidBodies.Allocate(true, true), this);
        body.Data.IsActive = true;

        bodies.Add(body, true);

        IslandHelper.BodyAdded(islands, body);

        AddToActiveList(body.island);

        return body;
    }
}