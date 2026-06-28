// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

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
        CollisionMatrix.s_collisionMatrix = new Boolean32Matrix(true);
        base.Dispose();
    }

    private Scene CreatePhysicsScene()
    {
        var scene = CreateScene(enable: true);
        scene.Physics.UseMultithreading = false; // deterministic stepping
        return scene;
    }

    private GameObject AddStaticBox(Scene scene, Float3 position, Float3 size)
    {
        var go = CreateGameObject("StaticBox");
        go.Transform.Position = position;
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
}
