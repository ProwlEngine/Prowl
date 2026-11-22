// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

using ColliderShape = Prowl.Runtime.CharacterController.ColliderShape;

namespace Prowl.Runtime.Test.Controller;

/// <summary>Gravity that changes while the controller moves: round a planet, flipped, slowly tilted.</summary>
public class ChangingGravityTests() : ControllerTestBase(Gravity.Upright)
{
    private const float Fall = 20f;

    private CharacterController Place(Scene scene, Float3 at, ColliderShape shape)
    {
        GameObject go = CreateGameObject("Player");
        go.Transform.Position = at;
        CharacterController controller = go.AddComponent<CharacterController>();
        controller.Shape = shape;
        controller.Radius = 0.4f;
        controller.Height = 1.8f;
        scene.Add(go);
        controller.Teleport(at);
        return controller;
    }

    /// <summary>One frame of a player loop with gravity along <paramref name="up"/>, keeping the speed along up between frames.</summary>
    private static void Step(CharacterController controller, Float3 up, Float3 walk, ref float rising)
    {
        controller.GameObject.Scene!.Update();
        controller.Up = up;

        if (controller.IsGrounded && rising <= 0f) rising = -1f;
        else rising -= Fall * Dt;

        controller.Move((walk + up * rising) * Dt);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void WalkingRoundAPlanetComesBackToTheStart(ColliderShape shape)
    {
        const float Radius = 6f;
        Scene scene = World();
        GameObject planet = CreateGameObject("Planet");
        planet.AddComponent<SphereCollider>().Radius = Radius;
        scene.Add(planet);

        Float3 start = new(0f, Radius, 0f);
        CharacterController controller = Place(scene, start, shape);
        float rising = 0f;
        int frames = (int)MathF.Ceiling(2f * MathF.PI * Radius / WalkSpeed / Dt);
        int grounded = 0;
        float worstAltitude = 0f;
        float furthest = 0f;

        for (int i = 0; i < frames; i++)
        {
            Float3 position = controller.GameObject.Transform.Position;
            Float3 up = Float3.Normalize(position);
            Float3 forward = Float3.Normalize(Float3.Cross(Float3.UnitX, up));
            Step(controller, up, forward * WalkSpeed, ref rising);

            if (controller.IsGrounded) grounded++;
            float altitude = Float3.Length(controller.GameObject.Transform.Position) - Radius;
            worstAltitude = MathF.Max(worstAltitude, MathF.Abs(altitude));
            furthest = MathF.Max(furthest, Float3.Length(controller.GameObject.Transform.Position - start));
        }

        Assert.True(furthest > Radius * 1.8f, $"never got round the far side, the furthest it got was {furthest:0.00} m");
        Assert.True(Float3.Length(controller.GameObject.Transform.Position - start) < 1f, $"ended at {controller.GameObject.Transform.Position} instead of back at the start");
        Assert.True(grounded >= frames - 2, $"was off the ground for {frames - grounded} of {frames} frames");
        Assert.True(worstAltitude < 0.25f, $"drifted {worstAltitude:0.000} m off the surface");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void FlippingGravityDropsItOntoTheCeilingFeetFirst(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Box(scene, new Float3(0f, 4.5f, 0f), new Float3(20f, 1f, 20f), name: "Ceiling");
        CharacterController controller = Place(scene, Float3.Zero, shape);
        float rising = 0f;

        for (int i = 0; i < 30; i++) Step(controller, Float3.UnitY, Float3.Zero, ref rising);
        rising = 0f;
        for (int i = 0; i < 90; i++) Step(controller, -Float3.UnitY, Float3.Zero, ref rising);

        Assert.True(controller.IsGrounded, "never stood on the ceiling");
        Assert.True(MathF.Abs(controller.GameObject.Transform.Position.Y - 4f) < 0.05f, $"its feet are at {controller.GameObject.Transform.Position.Y}, the ceiling is at 4");
        Assert.True(controller.Top.Y < 4f - 1.7f, $"its head is at {controller.Top.Y}, it should hang down from the ceiling");
        Assert.True(DeepestOverlap(controller) < 0.01f);
    }

    /// <summary>Gravity tilted under flat ground turns the ground into a slope, which stands still while walkable and slides once it is not.</summary>
    [Theory]
    [InlineData(ColliderShape.Capsule, 30f, true)]
    [InlineData(ColliderShape.Capsule, 70f, false)]
    [InlineData(ColliderShape.Cylinder, 30f, true)]
    [InlineData(ColliderShape.Cylinder, 70f, false)]
    public void TiltingGravityTurnsTheFloorIntoASlope(ColliderShape shape, float tilt, bool holds)
    {
        Scene scene = WorldWithFloor(200f);
        CharacterController controller = Place(scene, Float3.Zero, shape);
        float rising = 0f;
        for (int i = 0; i < 30; i++) Step(controller, Float3.UnitY, Float3.Zero, ref rising);

        for (int i = 1; i <= 60; i++)
        {
            float degrees = tilt * i / 60f * MathF.PI / 180f;
            Step(controller, new Float3(MathF.Sin(degrees), MathF.Cos(degrees), 0f), Float3.Zero, ref rising);
        }

        Float3 tilted = controller.GameObject.Transform.Position;
        Float3 up = new(MathF.Sin(tilt * MathF.PI / 180f), MathF.Cos(tilt * MathF.PI / 180f), 0f);
        for (int i = 0; i < 60; i++) Step(controller, up, Float3.Zero, ref rising);
        float slid = Float3.Length(controller.GameObject.Transform.Position - tilted);

        if (holds)
        {
            Assert.True(controller.IsGrounded, "lost the ground on a walkable tilt");
            Assert.True(slid < 0.02f, $"slid {slid:0.000} m on a walkable tilt");
        }
        else
        {
            Assert.True(slid > 1f, $"only slid {slid:0.00} m once the floor was too steep to stand on");
        }
    }
}
