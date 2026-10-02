// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

using ColliderShape = Prowl.Runtime.CharacterController.ColliderShape;

namespace Prowl.Runtime.Test.Controller;

/// <summary>
/// Builds small worlds out of boxes and drives a controller through them the way a game does: a
/// velocity with gravity, a walking speed, and the odd jump, one 60 Hz frame at a time.
/// </summary>
public abstract class ControllerTestBase : RuntimeTestBase
{
    protected const float Dt = 1f / 60f;
    protected const float WalkSpeed = 5f;

    protected static readonly Float3 North = new(0f, 0f, 1f);
    protected static readonly Float3 South = new(0f, 0f, -1f);
    protected static readonly Float3 East = new(1f, 0f, 0f);
    protected static readonly Float3 West = new(-1f, 0f, 0f);

    protected Scene World()
    {
        Scene scene = CreateScene(enable: true);
        scene.Physics.UseMultithreading = false;
        return scene;
    }

    protected Scene WorldWithFloor(float size = 80f)
    {
        Scene scene = World();
        Box(scene, new Float3(0f, -0.5f, 0f), new Float3(size, 1f, size));
        return scene;
    }

    protected GameObject Box(Scene scene, Float3 center, Float3 size, Float3 euler = default, string name = "Box")
        => Box(scene, center, size, Quaternion.FromEuler(euler), name);

    protected GameObject Box(Scene scene, Float3 center, Float3 size, Quaternion rotation, string name = "Box")
    {
        GameObject go = CreateGameObject(name);
        go.Transform.Position = center;
        go.Transform.Rotation = rotation;
        go.AddComponent<BoxCollider>().Size = size;
        scene.Add(go);
        return go;
    }

    protected GameObject Pillar(Scene scene, Float3 foot, float radius, float height)
    {
        GameObject go = CreateGameObject("Pillar");
        go.Transform.Position = foot + new Float3(0f, height * 0.5f, 0f);
        CylinderCollider cylinder = go.AddComponent<CylinderCollider>();
        cylinder.Radius = radius;
        cylinder.Height = height;
        scene.Add(go);
        return go;
    }

    /// <summary>A ramp rising from <paramref name="foot"/> on the floor along +Z turned by <paramref name="yaw"/>, <paramref name="length"/> long along its surface.</summary>
    protected GameObject Ramp(Scene scene, Float3 foot, float degrees, float length, float width = 6f, float thickness = 0.4f, float yaw = 0f)
    {
        float radians = degrees * MathF.PI / 180f;
        Quaternion turn = Quaternion.FromEuler(0f, yaw, 0f);
        Float3 along = turn * new Float3(0f, MathF.Sin(radians), MathF.Cos(radians));
        Float3 normal = turn * new Float3(0f, MathF.Cos(radians), -MathF.Sin(radians));
        Float3 center = foot + along * (length * 0.5f) - normal * (thickness * 0.5f);

        return Box(scene, center, new Float3(width, thickness, length), turn * Quaternion.FromEuler(-degrees, 0f, 0f), "Ramp");
    }

    /// <summary>
    /// A ceiling whose underside is <paramref name="height"/> above <paramref name="edge"/> and slopes
    /// down by <paramref name="degrees"/> going along +Z turned by <paramref name="yaw"/>, until it meets the floor.
    /// </summary>
    protected GameObject SlopedCeiling(Scene scene, Float3 edge, float height, float degrees, float width = 40f, float yaw = 0f, float thickness = 0.4f)
    {
        float radians = degrees * MathF.PI / 180f;
        float length = height / MathF.Sin(radians) + 0.5f;
        Quaternion turn = Quaternion.FromEuler(0f, yaw, 0f);
        Float3 along = turn * new Float3(0f, -MathF.Sin(radians), MathF.Cos(radians));
        Float3 up = turn * new Float3(0f, MathF.Cos(radians), MathF.Sin(radians));
        Float3 start = edge + new Float3(0f, height, 0f);
        Float3 center = start + along * (length * 0.5f) + up * (thickness * 0.5f);

        return Box(scene, center, new Float3(width, thickness, length), turn * Quaternion.FromEuler(degrees, 0f, 0f), "Sloped Ceiling");
    }

    /// <summary>The height of a ramp's surface at a distance along +Z from its foot.</summary>
    protected static float RampHeight(float degrees, float run) => run * MathF.Tan(degrees * MathF.PI / 180f);

    /// <summary>A solid staircase climbing along +Z from <paramref name="start"/>, each step a box down to the floor.</summary>
    protected void Stairs(Scene scene, Float3 start, int count, float rise, float run, float width = 3f, float yaw = 0f)
    {
        Quaternion turn = Quaternion.FromEuler(0f, yaw, 0f);
        for (int i = 0; i < count; i++)
        {
            float height = rise * (i + 1);
            Float3 local = new(0f, height * 0.5f, run * (i + 0.5f));
            Box(scene, start + turn * local, new Float3(width, height, run), new Float3(0f, yaw, 0f), "Step");
        }
    }

    protected Walker Spawn(Scene scene, Float3 at, ColliderShape shape = ColliderShape.Capsule, float radius = 0.4f, float height = 1.8f)
    {
        GameObject go = CreateGameObject("Player");
        go.Transform.Position = at;
        CharacterController controller = go.AddComponent<CharacterController>();
        controller.Shape = shape;
        controller.Radius = radius;
        controller.Height = height;
        scene.Add(go);
        controller.Teleport(at);

        var walker = new Walker(controller);
        walker.Settle();
        return walker;
    }

    /// <summary>Whether the controller is pushed into anything deeper than a rounding error.</summary>
    protected static float DeepestOverlap(CharacterController controller)
    {
        var overlaps = new List<ShapeCastHit>();
        controller.OverlapNow(overlaps);
        float deepest = 0f;
        foreach (ShapeCastHit hit in overlaps) deepest = MathF.Max(deepest, hit.Penetration);
        return deepest;
    }

    protected static void AssertNotInside(Walker walker)
    {
        float deepest = DeepestOverlap(walker.Controller);
        Assert.True(deepest < 0.01f, $"ended {deepest:0.000} m inside something at {walker.Position}");
    }

    protected static void AssertFinite(Walker walker)
    {
        Float3 p = walker.Position;
        Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z), $"position went to {p}");
    }

    /// <summary>
    /// The never stuck guarantee: from wherever it ended, walking at least one way of eight still
    /// makes real progress.
    /// </summary>
    protected static void AssertCanStillMove(Walker walker)
    {
        Float3 from = walker.Position;
        float best = 0f;
        for (int i = 0; i < 8; i++)
        {
            float angle = i * MathF.PI / 4f;
            Float3 direction = new(MathF.Sin(angle), 0f, MathF.Cos(angle));
            walker.Controller.Teleport(from);
            walker.Velocity = Float3.Zero;
            walker.Run(direction * WalkSpeed, 0.25f);
            Float3 moved = walker.Position - from;
            best = MathF.Max(best, MathF.Sqrt(moved.X * moved.X + moved.Z * moved.Z));
        }

        walker.Controller.Teleport(from);
        Assert.True(best > 0.3f, $"stuck at {from}: the furthest it got in any direction was {best:0.000} m");
    }

    /// <summary>
    /// A player loop: each frame runs the scene's update, which is what moves colliders whose
    /// Transform changed, then walks at a velocity, falls under gravity and jumps when asked.
    /// </summary>
    protected sealed class Walker
    {
        public readonly CharacterController Controller;
        public Float3 Velocity;
        public float Gravity = 20f;
        public float JumpSpeed = 6.5f;

        /// <summary>Takes up the vertical speed the controller managed while in the air, as a game should.</summary>
        public bool KeepsMomentum;
        public int GroundedFrames;
        public int Frames;

        public Walker(CharacterController controller) => Controller = controller;

        public Float3 Position => Controller.GameObject.Transform.Position;
        public bool Grounded => Controller.IsGrounded;

        public CharacterController.CollisionFlags Step(Float3 walk, bool jump = false)
        {
            Controller.GameObject.Scene.Update();
            EngineObject.ProcessDestroyed();

            float vertical = Velocity.Y;
            if (Controller.IsGrounded && vertical <= 0f) vertical = -1f;
            else vertical -= Gravity * Dt;
            if (jump && Controller.IsGrounded) vertical = JumpSpeed;

            Velocity = new Float3(walk.X, vertical, walk.Z);
            CharacterController.CollisionFlags flags = Controller.Move(Velocity * Dt);
            if (flags.HasFlag(CharacterController.CollisionFlags.Above) && Velocity.Y > 0f) Velocity.Y = 0f;
            if (KeepsMomentum && !Controller.IsGrounded) Velocity.Y = MathF.Max(Velocity.Y, Controller.Velocity.Y);

            Frames++;
            if (Controller.IsGrounded) GroundedFrames++;
            return flags;
        }

        public void Run(Float3 walk, float seconds, Action? eachFrame = null)
        {
            int frames = (int)MathF.Round(seconds / Dt);
            for (int i = 0; i < frames; i++)
            {
                eachFrame?.Invoke();
                Step(walk);
            }
        }

        public CharacterController.CollisionFlags Jump(Float3 walk = default) => Step(walk, jump: true);

        public void Settle(float seconds = 0.5f) => Run(Float3.Zero, seconds);

        public void ResetCounts()
        {
            Frames = 0;
            GroundedFrames = 0;
        }
    }
}
