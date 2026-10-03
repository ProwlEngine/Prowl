// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Runtime.Utils;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests for the WheelCollider vehicle model: the suspension settles at its spring equilibrium, a parked
/// car neither jitters nor creeps (including on a side slope), drive torque moves the car, braking stops
/// and holds it, driving over kerbs/onto rigidbodies or landing from a jump doesn't launch it, and a wheel
/// works in an arbitrary orientation.
/// </summary>
public class WheelTests : RuntimeTestBase
{
    public override void Dispose()
    {
        CollisionMatrix.Reset();
        base.Dispose();
    }

    private Scene CreatePhysicsScene()
    {
        var scene = CreateScene(enable: true);
        scene.Physics.UseMultithreading = false; // deterministic stepping
        return scene;
    }

    private GameObject AddStaticBox(Scene scene, Float3 position, Float3 size, int layer = 0)
    {
        var go = CreateGameObject("StaticBox");
        go.Transform.Position = position;
        go.LayerIndex = layer;
        go.AddComponent<BoxCollider>().Size = size;
        scene.Add(go);
        return go;
    }

    private Rigidbody3D AddDynamicBox(Scene scene, Float3 position, Float3 size, float mass = 50f)
    {
        var go = CreateGameObject("DynamicBox");
        go.Transform.Position = position;
        var rb = go.AddComponent<Rigidbody3D>();
        go.AddComponent<BoxCollider>().Size = size;
        scene.Add(go);
        rb.Mass = mass;
        return rb;
    }

    private const float Radius = 0.35f;
    private const float SuspDist = 0.3f;
    private const float Freq = 2.0f;
    private const float Mass = 1200f;

    /// <summary>Builds a 4-wheel car at <paramref name="pos"/>/<paramref name="rot"/>. Wheel mounts sit
    /// in the chassis plane; wheels hang along the chassis -up.</summary>
    private (Rigidbody3D chassis, WheelCollider[] wheels) BuildCar(Scene scene, Float3 pos, Quaternion rot, Float3? chassisSize = null)
    {
        var chassis = CreateGameObject("Chassis");
        chassis.Transform.Position = pos;
        chassis.Transform.Rotation = rot;
        var rb = chassis.AddComponent<Rigidbody3D>();
        chassis.AddComponent<BoxCollider>().Size = chassisSize ?? new Float3(1.6f, 0.4f, 3f);

        var wheels = new WheelCollider[4];
        Float3[] mounts =
        {
            new(0.7f, 0, 1.2f), new(-0.7f, 0, 1.2f),
            new(0.7f, 0, -1.2f), new(-0.7f, 0, -1.2f),
        };
        for (int i = 0; i < 4; i++)
        {
            var w = CreateGameObject("Wheel" + i);
            w.SetParent(chassis);
            w.Transform.LocalPosition = mounts[i];
            w.Transform.LocalRotation = Quaternion.Identity; // align with the chassis frame (SetParent preserves world pose)
            var wc = w.AddComponent<WheelCollider>();
            wc.Radius = Radius;
            wc.Width = 0.25f;
            wc.SuspensionDistance = SuspDist;
            wc.SuspensionFrequency = Freq;
            wc.SuspensionDampingRatio = 0.6f;
            wheels[i] = wc;
        }

        scene.Add(chassis);
        rb.Mass = Mass;
        return (rb, wheels);
    }

    [Fact]
    public void Car_SettlesAtSpringEquilibrium_AndStaysGrounded()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50)); // ground top at y=0
        var (_, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);

        Tick(scene, 400);

        // At equilibrium spring load = weight per wheel, and since auto sprung-mass = bodyMass/wheels,
        // the compression is exactly g/omega^2, independent of mass.
        float omega = 2.0f * Maths.PI * Freq;
        float expected = 9.81f / (omega * omega);
        foreach (var w in wheels)
        {
            Assert.True(w.IsGrounded, "wheel should be grounded at rest");
            float compression = w.SuspensionCompression * SuspDist;
            Assert.InRange(compression, expected - 0.02f, expected + 0.02f);
        }
    }

    [Fact]
    public void Car_AtRest_DoesNotJitterOrCreep()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
        var (rb, _) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);

        Tick(scene, 400); // settle
        Float3 restPos = rb.GameObject.Transform.Position;

        float maxLinVel = 0f, maxAngVel = 0f;
        for (int i = 0; i < 120; i++)
        {
            Tick(scene, 1);
            maxLinVel = Maths.Max(maxLinVel, (float)Float3.Length(rb.LinearVelocity));
            maxAngVel = Maths.Max(maxAngVel, (float)Float3.Length(rb.AngularVelocity));
        }
        Float3 endPos = rb.GameObject.Transform.Position;

        Assert.True(maxLinVel < 0.05f, $"chassis should be still (no jitter), max linear vel was {maxLinVel}");
        Assert.True(maxAngVel < 0.05f, $"chassis should not rock (no jitter), max angular vel was {maxAngVel}");
        Assert.True(Float3.Length(endPos - restPos) < 0.01f, "chassis should not creep on flat ground");
    }

    [Fact]
    public void Car_DriveTorque_AcceleratesForward()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);

        Tick(scene, 200); // settle
        float zStart = rb.GameObject.Transform.Position.Z;

        foreach (var w in wheels) w.MotorTorque = 600f; // drive all four
        Tick(scene, 180);

        float dz = rb.GameObject.Transform.Position.Z - zStart;
        Assert.True(rb.LinearVelocity.Z > 0.3f, $"car should accelerate forward, vz={rb.LinearVelocity.Z}");
        Assert.True(dz > 0.3f, $"car should have moved forward, dz={dz}, vz={rb.LinearVelocity.Z}, spin0={wheels[0].AngularVelocity}");
    }

    [Fact]
    public void Slip_IsSmallWhileRolling_AndLargeWhileSlidingSideways()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(200, 1, 200));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);
        Tick(scene, 200);

        rb.LinearVelocity = new Float3(0f, 0f, 15f);
        foreach (var w in wheels) w.MotorTorque = 40f;
        Tick(scene, 120);
        foreach (var w in wheels)
        {
            Assert.InRange(MathF.Abs(w.ForwardSlip), 0f, 0.5f);
            Assert.InRange(MathF.Abs(w.SidewaysSlip), 0f, 0.5f);
        }

        foreach (var w in wheels) w.MotorTorque = 0f;
        rb.LinearVelocity = new Float3(12f, 0f, 0f);
        Tick(scene, 2);
        foreach (var w in wheels)
            Assert.True(MathF.Abs(w.SidewaysSlip) > 5f, $"a car shoved sideways slides, slip {w.SidewaysSlip}");
    }

    [Fact]
    public void Car_CoastingOnAHugeFloor_RidesSmoothly()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(4000, 1, 4000));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, -100f), Quaternion.Identity);
        Tick(scene, 200);

        rb.LinearVelocity = new Float3(0f, 0f, 15f);
        Tick(scene, 60);
        for (int i = 0; i < 120; i++)
        {
            Tick(scene, 1);
            Assert.True(MathF.Abs(rb.LinearVelocity.Y) < 0.05f, $"the car bounced, vertical speed {rb.LinearVelocity.Y}");
            foreach (var w in wheels) Assert.True(w.IsGrounded, "a wheel left the ground");
        }
    }

    [Fact]
    public void Car_AtSpeed_StaysLevel()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(100, 1, 3000));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, -1400f), Quaternion.Identity);
        Tick(scene, 200);
        float restFront = wheels[0].SuspensionCompression, restRear = wheels[2].SuspensionCompression;

        rb.LinearVelocity = new Float3(0f, 0f, 50f);
        Tick(scene, 240);

        float pitch = MathF.Asin((rb.Rotation * Float3.UnitY).Z) * 180f / MathF.PI;
        Assert.InRange(pitch, -0.3f, 0.3f);
        Assert.InRange(wheels[0].SuspensionCompression, restFront - 0.05f, restFront + 0.05f);
        Assert.InRange(wheels[2].SuspensionCompression, restRear - 0.05f, restRear + 0.05f);
        foreach (var w in wheels) Assert.InRange(w.ContactNormal.Z, -0.001f, 0.001f);
    }

    [Fact]
    public void HeavyTruck_CruisesSmoothly_AndBrakesHard()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(100, 1, 400));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 1.2f, -150f), Quaternion.Identity);
        rb.Mass = 6500f;
        foreach (var w in wheels)
        {
            w.Radius = 0.6f;
            w.ForwardFriction = 2.4f;
            w.Recalculate();
        }
        Tick(scene, 200);

        rb.LinearVelocity = new Float3(0f, 0f, 15f);
        foreach (var w in wheels) w.MotorTorque = 300f;
        Tick(scene, 60);
        float worstSlip = 0f;
        for (int i = 0; i < 60; i++)
        {
            Tick(scene, 1);
            foreach (var w in wheels) worstSlip = MathF.Max(worstSlip, MathF.Abs(w.ForwardSlip));
        }
        Assert.True(worstSlip < 0.5f, $"the wheels chatter while cruising, slip up to {worstSlip}");

        float startZ = rb.Transform.Position.Z;
        float speed = rb.LinearVelocity.Z;
        foreach (var w in wheels) { w.MotorTorque = 0f; w.BrakeTorque = 16000f; }
        Tick(scene, 120);

        Assert.True(MathF.Abs(rb.LinearVelocity.Z) < 0.3f, $"still moving at {rb.LinearVelocity.Z} after two seconds of braking from {speed}");
        Assert.True(rb.Transform.Position.Z - startZ < 15f, $"took {rb.Transform.Position.Z - startZ} m to stop");
    }

    [Fact]
    public void Car_HardDrive_StaysPhysical()
    {
        // Heavier car, big wheels, dropped from a height, then driven with high torque: speed must stay
        // physical, not diverge.
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(200, 1, 200));

        var chassis = CreateGameObject("Car");
        chassis.Transform.Position = new Float3(0, 2f, 0); // start above the ground so it drops in
        var rb = chassis.AddComponent<Rigidbody3D>();
        chassis.AddComponent<BoxCollider>().Size = new Float3(2, 0.5f, 4);
        var wheels = new WheelCollider[4];
        Float3[] mounts = { new(-1f, -0.25f, 1.5f), new(1f, -0.25f, 1.5f), new(-1f, -0.25f, -1.5f), new(1f, -0.25f, -1.5f) };
        for (int i = 0; i < 4; i++)
        {
            var w = CreateGameObject("W" + i);
            w.SetParent(chassis);
            w.Transform.LocalPosition = mounts[i];
            w.Transform.LocalRotation = Quaternion.Identity;
            var wc = w.AddComponent<WheelCollider>();
            wc.Radius = 0.5f; wc.Width = 0.3f;
            wc.SuspensionDistance = 0.25f; wc.SuspensionFrequency = 2f; wc.SuspensionDampingRatio = 0.7f;
            wc.ForwardFriction = 2.5f; wc.SidewaysFriction = 1.2f;
            wheels[i] = wc;
        }
        scene.Add(chassis);
        rb.Mass = 1000f;

        Tick(scene, 200); // settle after the drop
        Assert.True((float)Float3.Length(rb.LinearVelocity) < 1f, $"should settle after drop, vel={rb.LinearVelocity}");

        float maxSpeed = 0f;
        for (int i = 0; i < 600 && maxSpeed < 300f; i++)
        {
            wheels[2].MotorTorque = 1900f; wheels[3].MotorTorque = 1900f;
            Tick(scene, 1);
            maxSpeed = Maths.Max(maxSpeed, (float)Float3.Length(rb.LinearVelocity));
        }
        Assert.True(maxSpeed < 150f, $"car speed should stay physical, peaked at {maxSpeed} m/s");
    }

    [Fact]
    public void Car_LandsFromJumpWithSpunWheels_DoesNotLurch()
    {
        // A car goes airborne, its free wheels spin up under throttle, then it lands: the large
        // rim-vs-ground slip on landing must not lurch the car. It must land and stay physical.
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(200, 1, 200));

        var chassis = CreateGameObject("Car");
        chassis.Transform.Position = new Float3(0, 4f, 0); // start high -> long airborne phase
        var rb = chassis.AddComponent<Rigidbody3D>();
        chassis.AddComponent<BoxCollider>().Size = new Float3(2, 0.5f, 4);
        var wheels = new WheelCollider[4];
        Float3[] mounts = { new(-1f, -0.25f, 1.5f), new(1f, -0.25f, 1.5f), new(-1f, -0.25f, -1.5f), new(1f, -0.25f, -1.5f) };
        for (int i = 0; i < 4; i++)
        {
            var w = CreateGameObject("W" + i);
            w.SetParent(chassis);
            w.Transform.LocalPosition = mounts[i];
            w.Transform.LocalRotation = Quaternion.Identity;
            var wc = w.AddComponent<WheelCollider>();
            wc.Radius = 0.5f; wc.Width = 0.3f;
            wc.SuspensionDistance = 0.25f; wc.SuspensionFrequency = 2f; wc.SuspensionDampingRatio = 0.7f;
            wc.ForwardFriction = 2.5f; wc.SidewaysFriction = 1.2f;
            wheels[i] = wc;
        }
        scene.Add(chassis);
        rb.Mass = 1000f;

        float maxSpeed = 0f;
        for (int i = 0; i < 600 && maxSpeed < 500f; i++)
        {
            // Hold full throttle the whole time, including while airborne, so the wheels spin up freely.
            wheels[2].MotorTorque = 1900f; wheels[3].MotorTorque = 1900f;
            Tick(scene, 1);
            maxSpeed = Maths.Max(maxSpeed, (float)Float3.Length(rb.LinearVelocity));
        }

        Assert.True(maxSpeed < 200f, $"car should not lurch on landing, peaked at {maxSpeed} m/s");
    }

    [Fact]
    public void Wheel_Airborne_IsNotGrounded()
    {
        var scene = CreatePhysicsScene();
        // No ground; the car free-falls. Wheels find nothing.
        var (_, wheels) = BuildCar(scene, new Float3(0, 5f, 0), Quaternion.Identity);

        Tick(scene, 5);

        foreach (var w in wheels)
            Assert.False(w.IsGrounded, "wheel with no ground beneath it must not be grounded");
    }

    [Fact]
    public void Wheel_SidewaysOrientation_SettlesAgainstWall()
    {
        var scene = CreatePhysicsScene();
        // "Down" is -X: a wall at x=0 acts as the floor, and the car's up points +X (rotate -90 about Z
        // takes local +Y to world +X). The car falls onto the wall and its sideways wheels hold it.
        scene.Physics.Gravity = new Float3(-9.81f, 0, 0);
        AddStaticBox(scene, new Float3(-0.5f, 0, 0), new Float3(1, 50, 50)); // +X face at x=0
        var rot = Quaternion.AxisAngle(Float3.UnitZ, -Maths.PI / 2f);
        var (rb, wheels) = BuildCar(scene, new Float3(0.7f, 0, 0), rot);

        Tick(scene, 400);
        Float3 restPos = rb.GameObject.Transform.Position;
        Tick(scene, 120);
        Float3 endPos = rb.GameObject.Transform.Position;

        foreach (var w in wheels)
            Assert.True(w.IsGrounded, "sideways wheel should rest against the wall");
        Assert.True((float)Float3.Length(rb.LinearVelocity) < 0.1f, $"sideways car should come to rest, vel={rb.LinearVelocity}");
        // No creep perpendicular to gravity (Y/Z held by static friction).
        Assert.True(Maths.Abs(endPos.Y - restPos.Y) < 0.05f && Maths.Abs(endPos.Z - restPos.Z) < 0.05f,
            $"sideways car should not creep along the wall, drift=({endPos.Y - restPos.Y},{endPos.Z - restPos.Z})");
    }

    [Fact]
    public void Wheel_UpsideDown_SettlesUnderCeiling()
    {
        var scene = CreatePhysicsScene();
        // Gravity points UP; a ceiling above (its -Y face at y=0) is the "floor". The car is flipped so
        // its up axis is -Y, hanging its wheels upward against the ceiling.
        scene.Physics.Gravity = new Float3(0, 9.81f, 0);
        AddStaticBox(scene, new Float3(0, 0.5f, 0), new Float3(50, 1, 50)); // -Y face at y=0
        var rot = Quaternion.AxisAngle(Float3.UnitZ, Maths.PI); // up -> -Y
        var (rb, wheels) = BuildCar(scene, new Float3(0, -0.7f, 0), rot);

        Tick(scene, 400);

        foreach (var w in wheels)
            Assert.True(w.IsGrounded, "upside-down wheel should rest against the ceiling");
        Assert.True((float)Float3.Length(rb.LinearVelocity) < 0.1f, $"upside-down car should come to rest, vel={rb.LinearVelocity}");
    }

    [Fact]
    public void Wheel_OnSphericalPlanet_StaysOnCurvedSurface()
    {
        var scene = CreatePhysicsScene();
        // A big static sphere is the planet; the car sits at the top pole with normal downward gravity.
        // This exercises the spherecast against a curved surface (driving a planet is then the same
        // physics rotated, which the sideways/upside-down cases already cover).
        const float R = 20f;
        var planet = CreateGameObject("Planet");
        planet.Transform.Position = new Float3(0, -R, 0); // top of the sphere at y=0
        planet.AddComponent<SphereCollider>().Radius = R;
        scene.Add(planet);

        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);
        // The pole is an unstable equilibrium (balancing on top of a ball), so brake the wheels - a
        // parked car must hold its spot on the curved surface via static friction rather than roll off.
        foreach (var w in wheels) w.BrakeTorque = 4000f;

        Tick(scene, 400);

        foreach (var w in wheels)
            Assert.True(w.IsGrounded, "wheel should rest on the planet surface");
        Assert.True((float)Float3.Length(rb.LinearVelocity) < 0.1f, $"car should settle on the planet, vel={rb.LinearVelocity}");
    }

    [Fact]
    public void EightWheelFlipCar_ManualSprungMass_WorksBothWaysUp()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50)); // ground top at y=0

        // An 8-wheel car: 4 wheels hang down (up = +Y) and 4 hang up (up = -Y, rotated 180). Sprung mass
        // is set MANUALLY to bodyMass/4 per wheel (not auto, which would divide by all 8), so whichever
        // 4 are in contact carry the full weight; the other 4 are airborne and apply nothing.
        var chassis = CreateGameObject("Chassis");
        chassis.Transform.Position = new Float3(0, 0.6f, 0);
        var rb = chassis.AddComponent<Rigidbody3D>();
        chassis.AddComponent<BoxCollider>().Size = new Float3(1.6f, 0.4f, 3f);

        var bottom = new WheelCollider[4];
        var top = new WheelCollider[4];
        Float3[] corners = { new(0.7f, 0, 1.2f), new(-0.7f, 0, 1.2f), new(0.7f, 0, -1.2f), new(-0.7f, 0, -1.2f) };
        WheelCollider MakeWheel(string name, Float3 localPos, Quaternion localRot)
        {
            var w = CreateGameObject(name);
            w.SetParent(chassis);
            w.Transform.LocalPosition = localPos;
            w.Transform.LocalRotation = localRot;
            var wc = w.AddComponent<WheelCollider>();
            wc.Radius = Radius; wc.Width = 0.25f;
            wc.SuspensionDistance = SuspDist; wc.SuspensionFrequency = Freq; wc.SuspensionDampingRatio = 0.6f;
            wc.SprungMass = Mass / 4f; // manual: only 4 wheels carry the car at a time
            return wc;
        }
        for (int i = 0; i < 4; i++)
        {
            bottom[i] = MakeWheel("Bottom" + i, corners[i], Quaternion.Identity);                       // up = +Y
            top[i] = MakeWheel("Top" + i, corners[i], Quaternion.AxisAngle(Float3.UnitZ, Maths.PI));    // up = -Y
        }
        scene.Add(chassis);
        rb.Mass = Mass;

        Tick(scene, 400);

        // Right-side up: the bottom wheels carry the car; the top wheels point at the sky (airborne).
        foreach (var w in bottom) Assert.True(w.IsGrounded, "bottom wheels should carry the car");
        foreach (var w in top) Assert.False(w.IsGrounded, "top wheels should be airborne");
        Assert.True((float)Float3.Length(rb.LinearVelocity) < 0.05f, "8-wheel car should settle");
        // Settles at the same spring equilibrium as a 4-wheel car (manual sprung mass = bodyMass/4).
        float omega = 2.0f * Maths.PI * Freq;
        float expected = 9.81f / (omega * omega);
        Assert.InRange(bottom[0].SuspensionCompression * SuspDist, expected - 0.02f, expected + 0.02f);
    }

    // ---- Anti-creep (gravity feed-forward) ----

    [Fact]
    public void Car_OnSideSlope_DoesNotCreepSideways()
    {
        var scene = CreatePhysicsScene();
        // ~10 degree side tilt: gravity has a lateral (+X) component. The car's lateral axis is X, so this
        // is the "slides sideways on a slope" case - lateral grip + gravity feed-forward must hold it.
        float g = 9.81f, ang = Maths.PI / 180f * 10f;
        scene.Physics.Gravity = new Float3(g * Maths.Sin(ang), -g * Maths.Cos(ang), 0);
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
        var (rb, _) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);

        Tick(scene, 200); // settle
        float xSettle = rb.GameObject.Transform.Position.X;
        Tick(scene, 360); // 6 seconds - any creep would accumulate here
        float drift = Maths.Abs(rb.GameObject.Transform.Position.X - xSettle);

        Assert.True(drift < 0.05f, $"car crept sideways {drift} m on a 10-degree slope over 6s");
    }

    // ---- Stability under driving, braking and collisions ----

    [Fact]
    public void Car_DriveThenBrake_StopsAndHoldsWithoutSpinningOrJittering()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(200, 1, 200));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);

        Tick(scene, 60); // settle

        // Drive forward for ~2s so the car is actually moving and the wheels are spinning.
        for (int i = 0; i < 120; i++)
        {
            wheels[2].MotorTorque = 800f; wheels[3].MotorTorque = 800f;
            Tick(scene, 1);
        }
        Assert.True(rb.LinearVelocity.Z > 1f, $"car should be moving before braking, vz={rb.LinearVelocity.Z}");

        // Release throttle, slam the brakes, and keep them on.
        foreach (var w in wheels) { w.MotorTorque = 0f; w.BrakeTorque = 4000f; }
        for (int i = 0; i < 240; i++) Tick(scene, 1); // ~4s braking

        // Now it must be stopped and dead - no residual speed, no spinning wheels, no jitter, no creep.
        Float3 restPos = rb.GameObject.Transform.Position;
        float maxLinVel = 0f, maxSpin = 0f;
        for (int i = 0; i < 120; i++)
        {
            Tick(scene, 1);
            maxLinVel = Maths.Max(maxLinVel, (float)Float3.Length(rb.LinearVelocity));
            foreach (var w in wheels) maxSpin = Maths.Max(maxSpin, Maths.Abs(w.AngularVelocity));
        }
        Float3 endPos = rb.GameObject.Transform.Position;

        Assert.True(maxLinVel < 0.05f, $"braked car should be still (no jitter), max vel {maxLinVel}");
        Assert.True(maxSpin < 0.2f, $"braked wheels should not spin at rest, max |omega| {maxSpin}");
        Assert.True(Float3.Length(endPos - restPos) < 0.02f, "braked car should hold its position");
    }

    [Fact]
    public void Car_DrivesOverKerb_DoesNotLaunch()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(200, 1, 200));
        AddStaticBox(scene, new Float3(0, 0.2f, 8f), new Float3(20, 0.4f, 1f)); // a low kerb across the path
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);

        float maxSpeed = 0f, maxUp = 0f;
        for (int i = 0; i < 360 && maxSpeed < 300f; i++)
        {
            wheels[2].MotorTorque = 1200f; wheels[3].MotorTorque = 1200f; // drive forward into the kerb
            Tick(scene, 1);
            maxSpeed = Maths.Max(maxSpeed, (float)Float3.Length(rb.LinearVelocity));
            maxUp = Maths.Max(maxUp, Maths.Abs(rb.LinearVelocity.Y));
        }
        Assert.True(maxSpeed < 60f, $"car launched off the kerb, peak speed {maxSpeed} m/s");
        Assert.True(maxUp < 15f, $"car launched upward off the kerb, peak vy {maxUp} m/s");
    }

    [Fact]
    public void Car_DrivesIntoRigidbody_DoesNotLaunch()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(200, 1, 200));
        AddDynamicBox(scene, new Float3(0, 0.5f, 6f), new Float3(1, 1, 1), mass: 40f); // a crate in the path
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);

        float maxSpeed = 0f, maxUp = 0f;
        for (int i = 0; i < 360 && maxSpeed < 300f; i++)
        {
            wheels[2].MotorTorque = 1200f; wheels[3].MotorTorque = 1200f; // drive into the crate
            Tick(scene, 1);
            maxSpeed = Maths.Max(maxSpeed, (float)Float3.Length(rb.LinearVelocity));
            maxUp = Maths.Max(maxUp, Maths.Abs(rb.LinearVelocity.Y));
        }
        Assert.True(maxSpeed < 60f, $"car launched off the rigidbody, peak speed {maxSpeed} m/s");
        Assert.True(maxUp < 15f, $"car launched upward off the rigidbody, peak vy {maxUp} m/s");
    }

    // The wheel pushes back on a dynamic body it stands on. A light crate used to take the whole
    // car's load and tyre friction and be launched across the map.
    [Theory]
    [InlineData(0.5f)]
    [InlineData(2f)]
    public void Car_DrivingOverLightCrates_DoesNotFlingThem(float crateMass)
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(200, 1, 200));
        var crates = new List<Rigidbody3D>();
        for (int i = 0; i < 6; i++)
            crates.Add(AddDynamicBox(scene, new Float3((i % 2 == 0 ? -0.7f : 0.7f), 0.2f, 5f + i * 1.5f), new Float3(0.4f, 0.4f, 0.4f), crateMass));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);

        float maxCrate = 0f;
        for (int i = 0; i < 300; i++)
        {
            wheels[2].MotorTorque = 1200f; wheels[3].MotorTorque = 1200f;
            Tick(scene, 1);
            foreach (var crate in crates)
                maxCrate = Maths.Max(maxCrate, (float)Float3.Length(crate.LinearVelocity));
        }

        float carSpeed = (float)Float3.Length(rb.LinearVelocity);
        Assert.True(maxCrate < Maths.Max(carSpeed * 2.5f, 8f), $"a crate was flung at {maxCrate} m/s, car at {carSpeed} m/s");
    }

    // ---- Holding still: slopes, pushes and gravity ----

    private static float Gravity => 9.81f;

    private static Float3 Tilted(float degrees, bool sideways)
    {
        float a = degrees * Maths.PI / 180f;
        return sideways ? new Float3(Gravity * Maths.Sin(a), -Gravity * Maths.Cos(a), 0f) : new Float3(0f, -Gravity * Maths.Cos(a), Gravity * Maths.Sin(a));
    }

    private static void Brake(WheelCollider[] wheels, float torque) { foreach (var w in wheels) w.BrakeTorque = torque; }

    [Theory]
    [InlineData(5f)]
    [InlineData(10f)]
    [InlineData(20f)]
    public void Braked_OnASlope_HoldsStill(float degrees)
    {
        var scene = CreatePhysicsScene();
        scene.Physics.Gravity = Tilted(degrees, sideways: false);
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);
        Brake(wheels, 8000f);
        Tick(scene, 200);

        Float3 start = rb.Transform.Position;
        Tick(scene, 300);
        float moved = Float3.Length(rb.Transform.Position - start);
        Assert.True(moved < 0.03f, $"a braked car slid {moved} m in 5 s on a {degrees} degree slope");
    }

    [Fact]
    public void Parked_PushedSideways_DoesNotSlide()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);
        Brake(wheels, 8000f);
        Tick(scene, 200);

        float x = rb.Transform.Position.X;
        for (int i = 0; i < 180; i++)
        {
            rb.AddForce(new Float3(1500f, 0f, 0f));
            Tick(scene, 1);
        }
        float moved = Maths.Abs(rb.Transform.Position.X - x);
        Assert.True(moved < 0.03f, $"a 1500 N push slid the parked car {moved} m");
    }

    [Fact]
    public void SprungMassSetByHand_DoesNotPushTheCarUpASlope()
    {
        var scene = CreatePhysicsScene();
        scene.Physics.Gravity = Tilted(10f, sideways: true);
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);
        foreach (var w in wheels) w.SprungMass = Mass / 2f;
        Tick(scene, 200);

        float x = rb.Transform.Position.X;
        Tick(scene, 300);
        float moved = Maths.Abs(rb.Transform.Position.X - x);
        Assert.True(moved < 0.05f, $"the car moved {moved} m across the slope with a hand set sprung mass");
    }

    [Fact]
    public void WithoutGravity_ThereIsNoPhantomSidewaysPush()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
        var (rb, _) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);
        rb.AffectedByGravity = false;
        scene.Physics.Gravity = Tilted(15f, sideways: true);

        float x = rb.Transform.Position.X;
        for (int i = 0; i < 180; i++)
        {
            rb.AddForce(new Float3(0f, -Mass * Gravity, 0f));
            Tick(scene, 1);
        }
        float moved = Maths.Abs(rb.Transform.Position.X - x);
        Assert.True(moved < 0.02f, $"a car ignoring gravity drifted {moved} m sideways");
    }

    [Fact]
    public void WithAlmostNoGrip_ACarSlidesDownASlope()
    {
        var scene = CreatePhysicsScene();
        scene.Physics.Gravity = Tilted(20f, sideways: true);
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(100, 1, 100));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);
        foreach (var w in wheels) { w.SidewaysFriction = 0.01f; w.ForwardFriction = 0.01f; }

        float x = rb.Transform.Position.X;
        Tick(scene, 300);
        float moved = rb.Transform.Position.X - x;
        Assert.True(moved > 5f, $"a car with no grip only slid {moved} m in 5 s down a 20 degree slope");
    }

    [Fact]
    public void ASurfaceWithLittleGrip_LetsABrakedCarSlide()
    {
        var scene = CreatePhysicsScene();
        scene.Physics.Gravity = Tilted(20f, sideways: false);
        GameObject ground = AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(100, 1, 100));
        ground.AddComponent<WheelSurface>().Grip = 0.1f;
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);
        Brake(wheels, 8000f);

        float z = rb.Transform.Position.Z;
        Tick(scene, 180);
        Assert.True(rb.Transform.Position.Z - z > 1f, $"on ice the braked car only slid {rb.Transform.Position.Z - z} m");
    }

    // ---- Moving ground ----

    private Rigidbody3D AddPlatform(Scene scene, Float3 position, Float3 size)
    {
        var go = CreateGameObject("Platform");
        go.Transform.Position = position;
        var body = go.AddComponent<Rigidbody3D>();
        body.MotionType = Jitter2.Dynamics.MotionType.Kinematic;
        go.AddComponent<BoxCollider>().Size = size;
        scene.Add(go);
        return body;
    }

    [Fact]
    public void Braked_OnAnAcceleratingPlatform_RidesAlong()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -5f, 0), new Float3(50, 1, 50));
        Rigidbody3D platform = AddPlatform(scene, new Float3(0, -0.25f, 0), new Float3(10, 0.5f, 10));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);
        Brake(wheels, 8000f);
        Tick(scene, 200);

        Float3 offset = rb.Transform.Position - platform.Transform.Position;
        for (int i = 0; i < 120; i++)
        {
            platform.LinearVelocity += new Float3(0f, 0f, 3f / 60f);
            Tick(scene, 1);
        }
        Float3 drift = rb.Transform.Position - platform.Transform.Position - offset;
        Assert.True(Maths.Abs(drift.Z) < 0.1f, $"the car slipped {drift.Z} m on a platform speeding up to {platform.LinearVelocity.Z} m/s");
    }

    [Fact]
    public void Braked_OnATurntable_KeepsItsRadius()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -5f, 0), new Float3(50, 1, 50));
        Rigidbody3D table = AddPlatform(scene, new Float3(0, -0.25f, 0), new Float3(16, 0.5f, 16));
        var (rb, wheels) = BuildCar(scene, new Float3(4f, 0.6f, 0), Quaternion.Identity);
        Brake(wheels, 8000f);
        Tick(scene, 200);

        table.AngularVelocity = new Float3(0f, 0.5f, 0f);
        Tick(scene, 600);
        Float3 p = rb.Transform.Position;
        float r = Maths.Sqrt(p.X * p.X + p.Z * p.Z);
        Assert.InRange(r, 3.8f, 4.2f);
    }

    [Fact]
    public void ACarParkedOnAnotherCar_StaysOnItWhileItDrives()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(100, 1, 100));
        var (bottom, bottomWheels) = BuildCar(scene, new Float3(0, 0.6f, -20f), Quaternion.Identity);
        Tick(scene, 120);
        var (top, topWheels) = BuildCar(scene, bottom.Transform.Position + new Float3(0f, 1.0f, 0f), Quaternion.Identity, new Float3(1.2f, 0.3f, 2.6f));
        top.Mass = 300f;
        Brake(topWheels, 4000f);
        Tick(scene, 200);

        Float3 offset = top.Transform.Position - bottom.Transform.Position;
        foreach (var w in bottomWheels) w.MotorTorque = 250f;
        Tick(scene, 180);
        Float3 drift = top.Transform.Position - bottom.Transform.Position - offset;
        Assert.True(bottom.LinearVelocity.Z > 1f, "the bottom car should be driving");
        Assert.True(Maths.Abs(drift.Z) < 0.2f && Maths.Abs(drift.Y) < 0.1f, $"the top car shifted {drift} on the moving car");
        foreach (var w in topWheels) Assert.True(w.IsGrounded, "a wheel of the top car left the bottom car");
    }

    [Fact]
    public void ACarParkedOnATowedTrailer_RidesThroughACorner()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(200, 1, 200));
        var (car, carWheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);

        var trailer = CreateGameObject("Trailer");
        trailer.Transform.Position = new Float3(0f, 0.75f, -5.5f);
        var deck = trailer.AddComponent<Rigidbody3D>();
        trailer.AddComponent<BoxCollider>().Size = new Float3(2f, 0.12f, 3.6f);
        foreach (float z in new[] { -0.42f, 0.42f })
            foreach (float x in new[] { -1.1f, 1.1f })
            {
                var w = CreateGameObject("Trailer Wheel");
                w.SetParent(trailer);
                w.Transform.LocalPosition = new Float3(x, -0.2f, z);
                w.Transform.LocalRotation = Quaternion.Identity;
                var wc = w.AddComponent<WheelCollider>();
                wc.Radius = 0.3f;
                wc.SuspensionDistance = 0.25f;
            }
        scene.Add(trailer);
        deck.Mass = 500f;
        var hitch = trailer.AddComponent<BallSocketConstraint>();
        hitch.Anchor = new Float3(0f, 0f, 2.5f);
        hitch.ConnectedBody = car;

        var (rider, riderWheels) = BuildCar(scene, new Float3(0f, 1.9f, -5.5f), Quaternion.Identity, new Float3(1f, 0.25f, 1.6f));
        rider.Mass = 170f;
        foreach (var w in riderWheels) { w.Radius = 0.2f; w.SuspensionDistance = 0.08f; w.BrakeTorque = 2000f; }
        Tick(scene, 200);
        Assert.All(riderWheels, w => Assert.True(w.IsGrounded));

        Float3 start = deck.Transform.InverseTransformPoint(rider.Transform.Position);
        foreach (var w in carWheels) w.MotorTorque = 500f;
        Tick(scene, 120);
        carWheels[0].SteerAngle = carWheels[1].SteerAngle = 0.3f;
        Tick(scene, 180);

        Float3 shifted = deck.Transform.InverseTransformPoint(rider.Transform.Position) - start;
        Assert.True(Float3.Length(car.LinearVelocity) > 2f, "the car should be towing");
        Assert.True(Maths.Abs(shifted.X) < 0.2f && Maths.Abs(shifted.Z) < 0.2f, $"the parked car shifted {shifted} on the trailer");
        Assert.All(riderWheels, w => Assert.True(w.IsGrounded, "the parked car came off the trailer"));
    }

    [Fact]
    public void DrivingOffASleepingPlank_KicksItBack()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
        var plankGo = CreateGameObject("Plank");
        plankGo.Transform.Position = new Float3(0f, 0.05f, -1.2f);
        var plank = plankGo.AddComponent<Rigidbody3D>();
        plankGo.AddComponent<BoxCollider>().Size = new Float3(2f, 0.1f, 1f);
        scene.Add(plankGo);
        plank.Mass = 20f;
        TickUntil(scene, () => !plank.IsActive, 600);
        Assert.False(plank.IsActive, "the plank should be asleep before the car arrives");

        var (_, wheels) = BuildCar(scene, new Float3(0, 0.7f, 0), Quaternion.Identity);
        Tick(scene, 60);
        float z = plankGo.Transform.Position.Z;
        foreach (var w in wheels) w.MotorTorque = 600f;
        Tick(scene, 30);
        Assert.True(plankGo.Transform.Position.Z < z - 0.05f, $"the plank under the driven wheels moved {plankGo.Transform.Position.Z - z} m");
    }

    [Fact]
    public void ACarParkedOnALooseSlab_AndTheSlab_BothFallAsleep()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
        var slabGo = CreateGameObject("Slab");
        slabGo.Transform.Position = new Float3(0f, 0.1f, 0f);
        var slab = slabGo.AddComponent<Rigidbody3D>();
        slabGo.AddComponent<BoxCollider>().Size = new Float3(4f, 0.2f, 5f);
        scene.Add(slabGo);
        slab.Mass = 400f;
        var (rb, _) = BuildCar(scene, new Float3(0, 0.9f, 0), Quaternion.Identity);

        TickUntil(scene, () => !rb.IsActive && !slab.IsActive, 1200);
        Assert.False(rb.IsActive, "the parked car never fell asleep");
        Assert.False(slab.IsActive, "the slab under the parked car never fell asleep");
    }

    [Fact]
    public void ACarAsleepOnAPlatform_WakesWhenThePlatformMoves()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -5f, 0), new Float3(50, 1, 50));
        Rigidbody3D platform = AddPlatform(scene, new Float3(0, -0.25f, 0), new Float3(10, 0.5f, 10));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);
        Brake(wheels, 8000f);
        TickUntil(scene, () => !rb.IsActive, 900);
        Assert.False(rb.IsActive, "the car never fell asleep");

        float gap = rb.Transform.Position.Y - platform.Transform.Position.Y;
        platform.LinearVelocity = new Float3(0f, 1f, 0f);
        Tick(scene, 90);
        float newGap = rb.Transform.Position.Y - platform.Transform.Position.Y;
        Assert.InRange(newGap, gap - 0.1f, gap + 0.1f);
    }

    // ---- Contact ----

    [Fact]
    public void Wheels_DoNotClimbAWall()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
        AddStaticBox(scene, new Float3(0, 1.5f, 8f), new Float3(10, 3, 1));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity, new Float3(1.6f, 0.4f, 2f));
        Tick(scene, 120);
        float y = rb.Transform.Position.Y;

        foreach (var w in wheels) w.MotorTorque = 500f;
        float highest = y;
        for (int i = 0; i < 240; i++)
        {
            Tick(scene, 1);
            highest = Maths.Max(highest, rb.Transform.Position.Y);
        }
        Assert.True(highest - y < 0.15f, $"the car rose {highest - y} m driving into a wall");
    }

    [Fact]
    public void OnASlope_TheWheelTouchesTheSurfaceExactly()
    {
        var scene = CreatePhysicsScene();
        var ground = CreateGameObject("Slope");
        Quaternion tilt = Quaternion.FromEuler(new Float3(0f, 0f, 15f));
        ground.Transform.Rotation = tilt;
        ground.Transform.Position = tilt * new Float3(0, -0.5f, 0);
        ground.AddComponent<BoxCollider>().Size = new Float3(50, 1, 50);
        scene.Add(ground);
        var (_, wheels) = BuildCar(scene, new Float3(0, 0.8f, 0), Quaternion.Identity);
        foreach (var w in wheels) w.ForwardRayCount = 3;
        Brake(wheels, 8000f);
        Tick(scene, 240);

        Float3 normal = tilt * Float3.UnitY;
        foreach (var w in wheels)
        {
            Assert.True(w.IsGrounded);
            float off = Float3.Dot(w.ContactPoint, normal);
            Assert.True(Maths.Abs(off) < 0.003f, $"the contact sits {off} m off the slope's surface");
        }
    }

    [Fact]
    public void OnFlatGround_TheContactSitsUnderTheMiddleOfTheTyre()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
        var (_, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);
        Tick(scene, 200);

        foreach (var w in wheels)
        {
            Float3 offset = w.ContactPoint - w.Transform.Position;
            Assert.InRange(offset.X, -0.001f, 0.001f);
            Assert.InRange(offset.Z, -0.001f, 0.001f);
        }
    }

    [Fact]
    public void AnEvenRayCount_DoesNotSinkTheWheel()
    {
        float Rest(int rays)
        {
            var scene = CreatePhysicsScene();
            AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
            var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);
            foreach (var w in wheels) w.ForwardRayCount = rays;
            Tick(scene, 300);
            return rb.Transform.Position.Y;
        }
        Assert.InRange(Rest(2), Rest(9) - 0.002f, Rest(9) + 0.002f);
    }

    [Fact]
    public void ATinySuspensionTravel_StillGripsAndDrives()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(100, 1, 100));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.4f, 0), Quaternion.Identity);
        foreach (var w in wheels) w.SuspensionDistance = 0f;
        Tick(scene, 120);
        foreach (var w in wheels) w.MotorTorque = 400f;
        Tick(scene, 120);
        Assert.True(rb.LinearVelocity.Z > 2f, $"a car with no suspension travel only reached {rb.LinearVelocity.Z} m/s");
    }

    [Theory]
    [InlineData(15f)]
    [InlineData(25f)]
    public void StiffSprings_SettleWithoutHopping(float hertz)
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);
        foreach (var w in wheels) { w.SuspensionFrequency = hertz; w.SuspensionDampingRatio = 0.3f; }
        Tick(scene, 120);

        float worst = 0f;
        for (int i = 0; i < 120; i++)
        {
            Tick(scene, 1);
            worst = Maths.Max(worst, Maths.Abs(rb.LinearVelocity.Y));
        }
        Assert.True(worst < 0.05f, $"{hertz} Hz springs bounce the car at {worst} m/s");
    }

    [Fact]
    public void UnderHeavyGravity_ABottomedOutWheelDoesNotSink()
    {
        var scene = CreatePhysicsScene();
        scene.Physics.Gravity = new Float3(0f, -30f, 0f);
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);
        foreach (var w in wheels) w.SuspensionFrequency = 0.5f;
        Tick(scene, 240);
        Assert.True(rb.Transform.Position.Y > Radius - 0.03f, $"the mount sank to {rb.Transform.Position.Y}");
    }

    [Fact]
    public void Wheels_FollowTheLayerRules_AndTheirOwnMask()
    {
        const int Car = 5, Plate = 6;
        float RestingHeight(bool matrixIgnoresPlate, bool maskIgnoresPlate)
        {
            CollisionMatrix.Reset();
            if (matrixIgnoresPlate) CollisionMatrix.SetLayerCollision(Car, Plate, false);
            var scene = CreatePhysicsScene();
            AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
            AddStaticBox(scene, new Float3(0, 0.5f, 0), new Float3(50, 1, 50), Plate);
            var chassis = CreateGameObject("Chassis");
            chassis.LayerIndex = Car;
            chassis.Transform.Position = new Float3(0, 1.6f, 0);
            var rb = chassis.AddComponent<Rigidbody3D>();
            chassis.AddComponent<BoxCollider>().Size = new Float3(1.6f, 0.4f, 3f);
            foreach (Float3 m in new Float3[] { new(0.7f, 0, 1.2f), new(-0.7f, 0, 1.2f), new(0.7f, 0, -1.2f), new(-0.7f, 0, -1.2f) })
            {
                var w = CreateGameObject("Wheel");
                w.SetParent(chassis);
                w.Transform.LocalPosition = m;
                w.Transform.LocalRotation = Quaternion.Identity;
                var wc = w.AddComponent<WheelCollider>();
                wc.Radius = Radius;
                if (maskIgnoresPlate) { LayerMask mask = LayerMask.Everything; mask.RemoveLayer(Plate); wc.LayerMask = mask; }
            }
            scene.Add(chassis);
            rb.Mass = Mass;
            Tick(scene, 300);
            return rb.Transform.Position.Y;
        }

        float onPlate = RestingHeight(false, false);
        Assert.True(onPlate > 1.3f, $"the wheels should stand on the plate, car at {onPlate}");
        float throughPlate = RestingHeight(true, false);
        Assert.True(throughPlate < 0.7f, $"with the plate's layer ignored the car should fall through to the ground, car at {throughPlate}");
        float chassisOnPlate = RestingHeight(false, true);
        Assert.InRange(chassisOnPlate, 1.15f, 1.25f);
    }

    // ---- The body a wheel drives ----

    [Fact]
    public void WheelsOnAChildBody_DoNotShareTheParentsWeight()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);

        var child = CreateGameObject("Child Body");
        child.SetParent(rb.GameObject);
        child.Transform.LocalPosition = new Float3(0f, 0f, -4f);
        child.AddComponent<Rigidbody3D>().Mass = 100f;
        child.AddComponent<BoxCollider>().Size = new Float3(1f, 0.3f, 1f);
        foreach (float x in new[] { -0.5f, 0.5f })
        {
            var w = CreateGameObject("Child Wheel");
            w.SetParent(child);
            w.Transform.LocalPosition = new Float3(x, 0f, 0f);
            w.Transform.LocalRotation = Quaternion.Identity;
            w.AddComponent<WheelCollider>().Radius = Radius;
        }
        Tick(scene, 400);

        float omega = 2.0f * Maths.PI * wheels[0].SuspensionFrequency;
        float expected = 9.81f / (omega * omega);
        foreach (var w in wheels) Assert.InRange(w.SuspensionCompression * w.SuspensionDistance, expected - 0.02f, expected + 0.02f);
    }

    [Fact]
    public void AWheelMovedToAnotherBody_DrivesThatBody()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
        var (a, wheelsA) = BuildCar(scene, new Float3(-4f, 0.6f, 0), Quaternion.Identity);
        var (b, _) = BuildCar(scene, new Float3(4f, 0.6f, 0), Quaternion.Identity);
        Tick(scene, 60);

        WheelCollider moved = wheelsA[0];
        moved.GameObject.SetParent(b.GameObject);
        moved.Transform.LocalPosition = new Float3(0f, 0f, 0f);
        moved.Transform.LocalRotation = Quaternion.Identity;
        Tick(scene, 2);

        Assert.Same(b, moved.Body);
        Assert.True(moved.IsGrounded);
        Assert.True(moved.ContactPoint.Y < 0.05f, $"the moved wheel should stand on the ground under its new body, contact at {moved.ContactPoint.Y}");
    }

    [Fact]
    public void Camber_LeansTheSameWayWhateverTheSteering()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);
        WheelCollider frontRight = wheels[0];
        var visual = CreateGameObject("Visual");
        visual.SetParent(frontRight.GameObject);
        frontRight.VisualTransform = visual.Transform;
        frontRight.Camber = 5f;
        Tick(scene, 60);

        float Lean() => Float3.Dot(visual.Transform.Up, rb.Transform.Right);
        float straight = Lean();
        frontRight.SteerAngle = 0.6f;
        Tick(scene, 2);
        float steered = Lean();
        Assert.Equal(Maths.Sign(straight), Maths.Sign(steered));
        Assert.True(Maths.Abs(straight) > 0.05f);
    }

    // ---- Sleep ----

    [Fact]
    public void AParkedCar_FallsAsleep_AndWakesWhenDriven()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);
        TickUntil(scene, () => !rb.IsActive, 900);
        Assert.False(rb.IsActive, "a parked car never fell asleep");

        float y = rb.Transform.Position.Y;
        Tick(scene, 60);
        Assert.InRange(rb.Transform.Position.Y, y - 0.001f, y + 0.001f);

        foreach (var w in wheels) w.MotorTorque = 400f;
        Tick(scene, 60);
        Assert.True(rb.IsActive && rb.LinearVelocity.Z > 0.5f, "the car did not wake to drive");
    }

    // ---- Spin ----

    [Fact]
    public void Spin_IsCapped_AndANegativeBrakeDoesNothing()
    {
        var scene = CreatePhysicsScene();
        var (rb, wheels) = BuildCar(scene, new Float3(0, 500f, 0), Quaternion.Identity);
        rb.AffectedByGravity = false;
        wheels[0].MotorTorque = 5000f;
        wheels[1].BrakeTorque = -100f;
        Tick(scene, 240);

        Assert.InRange(wheels[0].AngularVelocity, wheels[0].MaxAngularVelocity * 0.99f, wheels[0].MaxAngularVelocity);
        Assert.InRange(wheels[1].AngularVelocity, -1e-3f, 1e-3f);
        Assert.InRange(wheels[0].WheelRotation, -2f * Maths.PI, 2f * Maths.PI);
    }

    [Fact]
    public void Parked_WithOnlyTheRearBraked_TheFrontWheelsStayStill()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);
        rb.Mass = 6500f;
        foreach (var w in wheels) { w.Radius = 0.6f; w.ForwardFriction = 2.4f; }
        Tick(scene, 120);
        wheels[2].BrakeTorque = wheels[3].BrakeTorque = 16000f;

        float worst = 0f;
        for (int i = 0; i < 240; i++)
        {
            Tick(scene, 1);
            worst = Maths.Max(worst, Maths.Max(Maths.Abs(wheels[0].AngularVelocity), Maths.Abs(wheels[1].AngularVelocity)));
        }
        Assert.True(worst < 0.02f, $"a free front wheel of a parked car spun at {worst} rad/s");
    }

    [Fact]
    public void ANoseHeavyCar_CoastingOnTheFlat_NeverSpeedsUp()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 600));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, -250f), Quaternion.Identity);
        wheels[2].Transform.LocalPosition = new Float3(0.7f, 0f, -2.2f);
        wheels[3].Transform.LocalPosition = new Float3(-0.7f, 0f, -2.2f);
        foreach (var w in wheels) { w.DragTorque = 0f; w.SpinDamping = 0f; }
        Tick(scene, 200);
        Assert.True((rb.Transform.Up).Z > 0.002f, "the car should sit nose down for this test to mean anything");

        rb.LinearVelocity = new Float3(0f, 0f, 10f);
        Tick(scene, 30);
        float before = rb.LinearVelocity.Z;
        Tick(scene, 300);
        Assert.True(rb.LinearVelocity.Z < before + 0.01f, $"a coasting nose heavy car sped up from {before} to {rb.LinearVelocity.Z} m/s");
    }

    [Fact]
    public void AGentleBrake_LetsAWheelKeepRollingAtWalkingPace()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 200));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, -50f), Quaternion.Identity);
        rb.Mass = 6500f;
        Tick(scene, 120);

        rb.LinearVelocity = new Float3(0f, 0f, 0.3f);
        Brake(wheels, 300f);
        Tick(scene, 10);
        float rolling = rb.LinearVelocity.Z / Radius;
        Assert.True(rolling > 0.2f, "the car should still be rolling");
        Assert.InRange(wheels[0].AngularVelocity, rolling * 0.9f, rolling * 1.1f);
    }

    [Fact]
    public void Rpm_MatchesTheRollingSpeed()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 400));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, -150f), Quaternion.Identity);
        Tick(scene, 120);
        rb.LinearVelocity = new Float3(0f, 0f, 10f);
        Tick(scene, 60);

        float expected = rb.LinearVelocity.Z / Radius * 60f / (2f * Maths.PI);
        Assert.InRange(wheels[0].Rpm, expected * 0.97f, expected * 1.03f);
    }

    // ---- Slip, load and what the wheel touches ----

    [Fact]
    public void Slips_HaveTheDocumentedSigns()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(100, 1, 400));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, -150f), Quaternion.Identity);
        Tick(scene, 120);

        rb.LinearVelocity = new Float3(3f, 0f, 0f);
        Tick(scene, 1);
        Assert.True(wheels[0].SidewaysSlip > 0f, $"sliding toward the right should read positive, was {wheels[0].SidewaysSlip}");

        rb.LinearVelocity = new Float3(0f, 0f, 10f);
        Brake(wheels, 20000f);
        Tick(scene, 2);
        Assert.True(wheels[0].ForwardSlip > 0f, $"a locked wheel should read positive, was {wheels[0].ForwardSlip}");

        Brake(wheels, 0f);
        Tick(scene, 240);
        foreach (var w in wheels) w.MotorTorque = 3000f;
        Tick(scene, 2);
        Assert.True(wheels[0].ForwardSlip < 0f, $"wheelspin should read negative, was {wheels[0].ForwardSlip}");
    }

    [Fact]
    public void GroundHit_ReportsTheColliderAndTheLoad()
    {
        var scene = CreatePhysicsScene();
        GameObject ground = AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
        var (_, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);
        Tick(scene, 300);

        Assert.True(wheels[0].GetGroundHit(out WheelHit hit));
        Assert.Same(ground.GetComponent<BoxCollider>(), hit.Collider);
        Assert.Same(ground, hit.GameObject);
        Assert.Null(hit.Rigidbody);
        Assert.InRange(hit.Force, Mass * 9.81f / 4f * 0.9f, Mass * 9.81f / 4f * 1.1f);
        Assert.True(hit.Normal.Y > 0.999f);
        Assert.InRange(wheels[0].Load, hit.Force - 1f, hit.Force + 1f);
    }

    [Fact]
    public void GroundHit_ReportsHowFastTheGroundMoves()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -5f, 0), new Float3(50, 1, 50));
        Rigidbody3D platform = AddPlatform(scene, new Float3(0, -0.25f, 0), new Float3(10, 0.5f, 10));
        var (_, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);
        Brake(wheels, 8000f);
        Tick(scene, 120);

        platform.LinearVelocity = new Float3(0f, 0f, 4f);
        Tick(scene, 30);
        Assert.True(wheels[0].GetGroundHit(out WheelHit hit));
        Assert.InRange(hit.GroundVelocity.Z, 3.9f, 4.1f);
    }

    [Fact]
    public void GroundedAndAirTime_Count()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
        var (_, wheels) = BuildCar(scene, new Float3(0, 6f, 0), Quaternion.Identity);
        Tick(scene, 30);
        Assert.True(wheels[0].AirTime > 0.4f && wheels[0].GroundedTime == 0f);
        Tick(scene, 180);
        Assert.True(wheels[0].GroundedTime > 1f && wheels[0].AirTime == 0f);
    }

    [Fact]
    public void Preload_CarriesTheWeightBeforeTheSpringCompresses()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
        var (_, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);
        foreach (var w in wheels) w.Preload = 1f;
        Tick(scene, 300);
        foreach (var w in wheels) Assert.InRange(w.SuspensionCompression * w.SuspensionDistance, 0f, 0.01f);
    }

    // ---- Grip curves and where grip pushes ----

    [Fact]
    public void FrictionCurve_RisesToItsPeak_ThenEasesToTheAsymptote()
    {
        var curve = new WheelFrictionCurve(0.2f, 1f, 0.6f, 0.7f);
        Assert.Equal(0f, curve.Evaluate(0f));
        Assert.Equal(1f, curve.Evaluate(0.2f), 4);
        Assert.Equal(0.7f, curve.Evaluate(1f), 4);
        Assert.Equal(curve.Evaluate(0.1f), curve.Evaluate(-0.1f));
        float previous = 0f;
        for (float s = 0.01f; s <= 0.2f; s += 0.01f)
        {
            Assert.True(curve.Evaluate(s) >= previous);
            previous = curve.Evaluate(s);
        }
        Assert.True(curve.Evaluate(0.4f) < 1f && curve.Evaluate(0.4f) > 0.7f);
    }

    [Fact]
    public void ASidewaysSlide_IsHeldBackByTheSlidingGrip_NotThePeak()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(200, 1, 200));
        var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);
        Tick(scene, 120);

        rb.LinearVelocity = new Float3(10f, 0f, 0f);
        Tick(scene, 3);
        float v0 = rb.LinearVelocity.X;
        Tick(scene, 12);
        float decel = (v0 - rb.LinearVelocity.X) / (12f / 60f);
        float mu = wheels[0].SidewaysFriction * 9.81f;
        Assert.InRange(decel, mu * 0.6f, mu * 0.9f);
    }

    [Fact]
    public void WeightTransfer_PitchesTheCarUnderBraking()
    {
        float Dive(float transfer)
        {
            var scene = CreatePhysicsScene();
            AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 400));
            var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, -150f), Quaternion.Identity);
            foreach (var w in wheels) w.WeightTransfer = transfer;
            Tick(scene, 120);
            rb.LinearVelocity = new Float3(0f, 0f, 15f);
            Tick(scene, 30);
            Brake(wheels, 6000f);
            Tick(scene, 15);
            return (wheels[0].SuspensionCompression - wheels[2].SuspensionCompression) * wheels[0].SuspensionDistance;
        }
        Assert.InRange(Dive(0f), -0.01f, 0.01f);
        Assert.True(Dive(1f) > 0.03f, $"full weight transfer should dive the nose under braking, front minus rear compression {Dive(1f)}");
    }

    [Fact]
    public void AnAntiRollBar_RollsTheBodyLess()
    {
        float Roll(bool bar)
        {
            var scene = CreatePhysicsScene();
            AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 50));
            var (rb, wheels) = BuildCar(scene, new Float3(0, 0.6f, 0), Quaternion.Identity);
            if (bar)
            {
                foreach ((int left, int right) in new[] { (1, 0), (3, 2) })
                {
                    var rollBar = rb.GameObject.AddComponent<AntiRollBar>();
                    rollBar.Left = wheels[left];
                    rollBar.Right = wheels[right];
                    rollBar.Stiffness = 40000f;
                }
            }
            Tick(scene, 120);
            for (int i = 0; i < 60; i++)
            {
                rb.AddForceAtPosition(new Float3(6000f, 0f, 0f), rb.Transform.Position + new Float3(0f, 1f, 0f));
                Tick(scene, 1);
            }
            return Maths.Abs(Float3.Dot(rb.Transform.Up, Float3.UnitX));
        }
        float without = Roll(false), with = Roll(true);
        Assert.True(with < without * 0.7f, $"roll with the bar {with}, without {without}");
    }

    [Fact]
    public void CamberThrust_PushesALeaningWheelTowardItsLean()
    {
        float Sideways(float thrust)
        {
            var scene = CreatePhysicsScene();
            AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(50, 1, 400));
            var body = CreateGameObject("Bike");
            Quaternion lean = Quaternion.FromEuler(new Float3(0f, 0f, -20f));
            body.Transform.Position = new Float3(0f, 0.6f, -150f);
            body.Transform.Rotation = lean;
            var rb = body.AddComponent<Rigidbody3D>();
            body.AddComponent<BoxCollider>().Size = new Float3(0.3f, 0.4f, 1.6f);
            var wheels = new List<WheelCollider>();
            foreach (float z in new[] { -0.7f, 0.7f })
            {
                var w = CreateGameObject("Wheel");
                w.SetParent(body);
                w.Transform.LocalPosition = new Float3(0f, 0f, z);
                w.Transform.LocalRotation = Quaternion.Identity;
                var wc = w.AddComponent<WheelCollider>();
                wc.Radius = Radius;
                wc.CamberThrust = thrust;
                wheels.Add(wc);
            }
            scene.Add(body);
            rb.Mass = 200f;
            for (int i = 0; i < 40; i++)
            {
                rb.MoveRotation(lean);
                rb.AngularVelocity = Float3.Zero;
                if (i == 10) rb.LinearVelocity = new Float3(0f, rb.LinearVelocity.Y, 10f);
                Tick(scene, 1);
            }
            Float3 leanSide = lean * Float3.UnitY;
            leanSide.Y = 0f;
            return Float3.Dot(rb.LinearVelocity, Float3.Normalize(leanSide));
        }
        float gained = Sideways(1f) - Sideways(0f);
        Assert.True(gained > 0.1f, $"camber thrust should push a leaning wheel toward its lean, gained {gained} m/s");
    }
}
