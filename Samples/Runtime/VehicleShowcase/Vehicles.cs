// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using static Prowl.Samples.Sample;

namespace VehicleShowcase;

/// <summary>A wheel's size, suspension and grip.</summary>
public readonly record struct WheelSetup(float Radius, float Width, float Travel, float Frequency, float Damping, float ForwardGrip = 2.5f, float SidewaysGrip = 1.6f);

/// <summary>A wheel model in the Assets folder and the size it was made at, so it can be scaled to any wheel.</summary>
public readonly record struct WheelModel(string Path, float Radius, float Width);

public sealed partial class VehicleShowcaseGame
{
    private static readonly WheelModel CarWheel = new("Models/Wheel", 0.42f, 0.32f);
    private static readonly WheelModel MonsterWheel = new("Models/Monster Wheel", 0.85f, 0.65f);
    private static readonly WheelModel BikeWheel = new("Models/Bike Wheel", 0.33f, 0.15f);
    private static readonly WheelModel DualWheel = new("Models/Dual Wheel", 0.52f, 0.62f);

    private Material _trim = null!, _chrome = null!, _glass = null!, _tyre = null!, _headlight = null!, _helmet = null!;

    private void CreateVehicleMaterials()
    {
        _trim = Lit(new Color(0.015f, 0.015f, 0.018f, 1f), 0.2f, 0.5f);
        _chrome = Lit(new Color(0.6f, 0.6f, 0.62f, 1f), 1f, 0.3f);
        _glass = Lit(new Color(0.01f, 0.015f, 0.025f, 1f), 0.9f, 0.05f);
        _tyre = Lit(new Color(0.02f, 0.02f, 0.02f, 1f), 0f, 0.9f);
        _headlight = Lit(Color.White).Emissive(new Color(1f, 0.95f, 0.85f, 1f), 3f);
        _helmet = Lit(new Color(0.8f, 0.8f, 0.82f, 1f), 0.3f, 0.3f);
    }

    private void BuildVehicles()
    {
        Register(SportsCar(new Float3(0f, 1.2f, -2f)));
        Register(RallyBuggy(new Float3(-7f, 1.2f, -2f)));
        Register(MonsterTruck(new Float3(-15f, 2f, -2f)));
        Register(GoKart(OnTrailer(0.3f, -0.3f)));
        Register(ArmyTruck(new Float3(-25f, 1.6f, -2f)));
        Register(Motorcycle(new Float3(10f, 0.8f, -2f)));
        Register(SemiTruck(new Float3(-45f, 1.2f, 14f)));
    }

    private Vehicle SportsCar(Float3 position)
    {
        GameObject chassis = Chassis("Sports Car", position, 1100f, out Rigidbody3D body, (new(1.9f, 0.5f, 4.3f), Float3.Zero), (new(1.6f, 0.5f, 2f), new(0f, 0.5f, -0.3f)));
        Material taillight = Taillight();
        Dress(chassis, "Models/Car", Lit(new Color(0.55f, 0.02f, 0.02f, 1f), 0.7f, 0.25f), taillight);

        var car = chassis.AddComponent<CarController>();
        car.Torque = 2800f;
        car.TopSpeed = 50f;
        car.Brake = 3500f;
        car.BrakeLights = taillight;
        car.Headlights.AddRange(Lamps(chassis, new(-0.6f, 0.2f, 2.3f), new(0.6f, 0.2f, 2.3f)));

        var wheels = new WheelSetup(0.42f, 0.32f, 0.3f, 2f, 0.7f);
        car.Axles.Add(Axle(chassis, wheels, CarWheel, 0.95f, -0.15f, 1.35f, steer: 1f, driven: false, handbrake: false));
        car.Axles.Add(Axle(chassis, wheels, CarWheel, 0.95f, -0.15f, -1.35f, steer: 0f, driven: true, handbrake: true));

        Add(chassis);
        return new Vehicle
        {
            Name = "Sports car",
            Description = "A road car on four WheelColliders. Each wheel finds the ground with a grid of rays, sits exactly on it, and grips along a friction curve that peaks and then eases off as the tyre slides. Rear wheel drive with traction and stability control; hold the handbrake through a corner to drift. Back up to the trailer and press T to tow it, kart and all.",
            Body = body, Car = car, Spawn = position, Hitch = new Float3(0f, -0.2f, -2.4f),
            BaseTorque = car.Torque, BaseBrake = car.Brake, BaseMass = body.Mass,
        };
    }

    private Vehicle RallyBuggy(Float3 position)
    {
        GameObject chassis = Chassis("Rally Buggy", position, 800f, out Rigidbody3D body, (new(1.6f, 0.4f, 3.4f), new(0f, -0.05f, 0.1f)), (new(1.1f, 0.9f, 1f), new(0f, 0.5f, -0.3f)));
        Material taillight = Taillight();
        Dress(chassis, "Models/Buggy", Lit(new Color(0.7f, 0.45f, 0.02f, 1f), 0.4f, 0.4f), taillight);

        var car = chassis.AddComponent<CarController>();
        car.Torque = 2600f;
        car.TopSpeed = 42f;
        car.Brake = 3000f;
        car.HandbrakeGrip = 0.35f;
        car.BrakeLights = taillight;
        car.Headlights.AddRange(Lamps(chassis, new(-0.27f, 1.04f, 0.3f), new(0.27f, 1.04f, 0.3f)));

        var wheels = new WheelSetup(0.45f, 0.3f, 0.45f, 1.6f, 0.6f, 2.4f);
        CarController.Axle front = Axle(chassis, wheels, CarWheel, 0.95f, -0.1f, 1.3f, steer: 1f, driven: true, handbrake: false);
        CarController.Axle rear = Axle(chassis, wheels, CarWheel, 0.95f, -0.1f, -1.3f, steer: 0f, driven: true, handbrake: true);
        rear.Grip = 1.3f;
        car.Axles.Add(front);
        car.Axles.Add(rear);

        Add(chassis);
        return new Vehicle
        {
            Name = "Rally buggy",
            Description = "Light, all wheel drive, with long soft suspension and a loose rear end. Built for the rough ground and the jumps, and for sliding: the rear has less grip than the front, so a flick of the handbrake swings it round.",
            Body = body, Car = car, Spawn = position, CameraDistance = 8f,
            BaseTorque = car.Torque, BaseBrake = car.Brake, BaseMass = body.Mass,
        };
    }

    private Vehicle MonsterTruck(Float3 position)
    {
        GameObject chassis = Chassis("Monster Truck", position, 2600f, out Rigidbody3D body,
            (new(2.1f, 0.75f, 4.6f), new(0f, 0.45f, 0f)), (new(1.8f, 0.6f, 1f), new(0f, 1.15f, 0.2f)), (new(1.1f, 0.25f, 4.2f), new(0f, -0.33f, 0f)));
        Material taillight = Taillight();
        Dress(chassis, "Models/Monster Truck", Lit(new Color(0.03f, 0.12f, 0.6f, 1f), 0.6f, 0.3f), taillight);

        var car = chassis.AddComponent<CarController>();
        car.Torque = 11000f;
        car.TopSpeed = 30f;
        car.Brake = 9000f;
        car.MaxSteer = 28f;
        car.BrakeLights = taillight;
        car.Headlights.AddRange(Lamps(chassis, new(-0.78f, 0.5f, 2.4f), new(0.78f, 0.5f, 2.4f)));

        var wheels = new WheelSetup(0.85f, 0.65f, 0.8f, 1.3f, 0.55f, 2.4f, 1.5f);
        car.Axles.Add(Axle(chassis, wheels, MonsterWheel, 1.3f, -0.35f, 1.55f, steer: 1f, driven: true, handbrake: false));
        car.Axles.Add(Axle(chassis, wheels, MonsterWheel, 1.3f, -0.35f, -1.55f, steer: -0.6f, driven: true, handbrake: true));
        foreach (CarController.Axle axle in car.Axles) axle.Grip = 1.5f;

        Add(chassis);
        return new Vehicle
        {
            Name = "Monster truck",
            Description = "Huge wheels on very long, soft suspension, with the rear wheels steering against the front for a tight turn. It climbs straight over the bumps and crates, and its high body rolls hard in corners, so take them gently or it goes over.",
            Body = body, Car = car, Spawn = position, CameraDistance = 13f,
            BaseTorque = car.Torque, BaseBrake = car.Brake, BaseMass = body.Mass,
        };
    }

    private Vehicle GoKart(Float3 position)
    {
        GameObject chassis = Chassis("Go Kart", position, 170f, out Rigidbody3D body, (new(1f, 0.22f, 1.7f), new(0f, -0.05f, 0.05f)), (new(0.4f, 0.5f, 0.12f), new(0f, 0.25f, -0.45f)));
        Dress(chassis, "Models/Kart", Lit(new Color(0.9f, 0.25f, 0.02f, 1f), 0.3f, 0.4f), _trim);

        var car = chassis.AddComponent<CarController>();
        car.Torque = 420f;
        car.TopSpeed = 26f;
        car.Brake = 900f;
        car.MaxSteer = 24f;
        car.HandbrakeGrip = 0.5f;
        car.Downforce = 0.5f;

        var front = new WheelSetup(0.2f, 0.2f, 0.06f, 4f, 0.8f, 2.8f, 2f);
        var rear = front with { Radius = 0.22f, Width = 0.26f };
        car.Axles.Add(Axle(chassis, front, CarWheel, 0.58f, -0.02f, 0.55f, steer: 1f, driven: false, handbrake: false));
        car.Axles.Add(Axle(chassis, rear, CarWheel, 0.6f, 0f, -0.55f, steer: 0f, driven: true, handbrake: true));
        car.Axles[0].Grip = 2f;
        car.Axles[1].Grip = 2.3f;

        Add(chassis);
        return new Vehicle
        {
            Name = "Go-kart",
            Description = "Tiny, light and nearly rigid: a few centimetres of stiff suspension and lots of grip. It starts parked on the trailer: reverse it down the ramps, or leave it there and tow it with the sports car or the army truck, and it rides along held by its tyres alone.",
            Body = body, Car = car, Spawn = position, CameraDistance = 5f,
            BaseTorque = car.Torque, BaseBrake = car.Brake, BaseMass = body.Mass,
        };
    }

    private Vehicle ArmyTruck(Float3 position)
    {
        GameObject chassis = Chassis("Army Truck", position, 6500f, out Rigidbody3D body,
            (new(1.4f, 0.85f, 1.45f), new(0f, 0.68f, 2.9f)), (new(2.4f, 1.7f, 1.3f), new(0f, 1.1f, 1.58f)), (new(2.5f, 2.25f, 4.35f), new(0f, 1.15f, -1.33f)),
            (new(1.2f, 0.3f, 7.1f), new(0f, -0.35f, 0.05f)), (new(2.6f, 0.3f, 0.3f), new(0f, 0f, 3.8f)));
        Material taillight = Taillight();
        Dress(chassis, "Models/Army Truck", Lit(new Color(0.1f, 0.13f, 0.05f, 1f), 0.1f, 0.7f), taillight, canvas: Lit(new Color(0.16f, 0.18f, 0.08f, 1f), 0f, 0.95f));

        var car = chassis.AddComponent<CarController>();
        car.Torque = 16000f;
        car.TopSpeed = 24f;
        car.Brake = 16000f;
        car.MaxSteer = 30f;
        car.Downforce = 0f;
        car.BrakeLights = taillight;
        car.Headlights.AddRange(Lamps(chassis, new(-0.98f, 0.52f, 3.45f), new(0.98f, 0.52f, 3.45f)));

        var wheels = new WheelSetup(0.6f, 0.42f, 0.4f, 1.5f, 0.6f, 2.4f);
        car.Axles.Add(Axle(chassis, wheels, CarWheel, 1.2f, -0.2f, 2.75f, steer: 1f, driven: true, handbrake: false));
        car.Axles.Add(Axle(chassis, wheels, CarWheel, 1.2f, -0.2f, -1.25f, steer: 0f, driven: true, handbrake: true));
        car.Axles.Add(Axle(chassis, wheels, CarWheel, 1.2f, -0.2f, -2.6f, steer: 0f, driven: true, handbrake: true));

        Add(chassis);
        return new Vehicle
        {
            Name = "Army truck",
            Description = "A six wheeled cargo truck: a steered front axle and a driven tandem pair under the bed, all six wheels pulling. Heavy and slow, but it climbs anything and tows the trailer: back up to it and press T.",
            Body = body, Car = car, Spawn = position, CameraDistance = 15f, Hitch = new Float3(0f, -0.14f, -3.95f),
            BaseTorque = car.Torque, BaseBrake = car.Brake, BaseMass = body.Mass,
        };
    }

    private Vehicle Motorcycle(Float3 position)
    {
        GameObject chassis = Chassis("Motorcycle", position, 230f, out Rigidbody3D body, (new(0.4f, 0.55f, 1.7f), new(0f, 0.2f, 0f)));
        Material paint = Lit(new Color(0.5f, 0.02f, 0.04f, 1f), 0.6f, 0.3f);
        Material taillight = Taillight();
        Material suit = Lit(new Color(0.02f, 0.02f, 0.025f, 1f), 0.2f, 0.6f);
        Dress(chassis, "Models/Motorcycle", paint, taillight, suit);

        // The forks and bars turn about the steering head, which leans back toward the front axle.
        Float3 head = new(0f, 0.52f, 0.55f), axle = new(0f, -0.05f, 0.72f);
        var steering = new GameObject("Steering");
        Part(chassis, steering, head);
        steering.Transform.LocalRotation = Quaternion.FromToRotation(-Float3.UnitY, Float3.Normalize(axle - head));
        Dress(steering, "Models/Fork", paint, taillight);

        var bike = chassis.AddComponent<MotorcycleController>();
        bike.Torque = 600f;
        bike.TopSpeed = 55f;
        bike.Brake = 1200f;
        bike.Steering = steering.Transform;
        bike.BrakeLights = taillight;
        bike.Headlights.AddRange(Lamps(chassis, new Float3(0f, 0.47f, 0.92f)));

        var wheels = new WheelSetup(0.33f, 0.15f, 0.15f, 2.2f, 0.7f, 2.6f, 2f);
        bike.Front = Wheel(chassis, axle, wheels, BikeWheel);
        bike.Rear = Wheel(chassis, new Float3(0f, -0.05f, -0.72f), wheels, BikeWheel);

        Add(chassis);
        return new Vehicle
        {
            Name = "Motorcycle",
            Description = "Two WheelColliders and a MotorcycleController that balances it. Steering asks for a lean, a balance torque leans the bike over, and the front wheel turns as far as that lean needs at the current speed, as on a real bike. At walking pace it steers directly and stays upright.",
            Body = body, Bike = bike, Spawn = position, CameraDistance = 6f,
            BaseTorque = bike.Torque, BaseBrake = bike.Brake, BaseMass = body.Mass,
        };
    }

    private Vehicle SemiTruck(Float3 position)
    {
        GameObject chassis = Chassis("Semi Truck", position, 9000f, out Rigidbody3D body,
            (new(1.7f, 0.95f, 2.1f), new(0f, 0.5f, 2.25f)), (new(2.44f, 2.45f, 2.7f), new(0f, 1.27f, -0.1f)),
            (new(1.1f, 0.4f, 7.1f), new(0f, -0.23f, -0.2f)), (new(2.6f, 0.37f, 0.27f), new(0f, -0.16f, 3.48f)));
        Material taillight = Taillight();
        Dress(chassis, "Models/Semi Truck", Lit(new Color(0.5f, 0.06f, 0.02f, 1f), 0.6f, 0.3f), taillight);

        var car = chassis.AddComponent<CarController>();
        car.Torque = 45000f;
        car.TopSpeed = 26f;
        car.Brake = 18000f;
        car.MaxSteer = 32f;
        car.Downforce = 0f;
        car.BrakeLights = taillight;
        car.Headlights.AddRange(Lamps(chassis, new(-1.07f, 0.19f, 3.4f), new(1.07f, 0.19f, 3.4f)));

        var front = new WheelSetup(0.52f, 0.32f, 0.3f, 1.5f, 0.6f, 2.4f);
        var dual = new WheelSetup(0.52f, 0.62f, 0.3f, 1.5f, 0.6f, 2.4f);
        car.Axles.Add(Axle(chassis, front, CarWheel, 1.05f, -0.3f, 2.4f, steer: 1f, driven: false, handbrake: false));
        car.Axles.Add(Axle(chassis, dual, DualWheel, 0.95f, -0.3f, -1.75f, steer: 0f, driven: true, handbrake: true));
        car.Axles.Add(Axle(chassis, dual, DualWheel, 0.95f, -0.3f, -3.05f, steer: 0f, driven: true, handbrake: true));

        var vehicle = new Vehicle
        {
            Name = "Semi truck",
            Description = "An eighteen wheeler pulling two flat deck trailers, the second on a converter dolly. The trailers brake with the truck, so the whole rig parks solid. Switch to another car and the ramps behind the last trailer come down: drive up them, along the deck and over the dolly onto the front trailer, then take the truck and haul everything away.",
            Body = body, Car = car, Spawn = position, CameraDistance = 24f,
            BaseTorque = car.Torque, BaseBrake = car.Brake, BaseMass = body.Mass,
        };
        Add(chassis);

        // The front trailer rests its kingpin on the fifth wheel, over the tractor's drive axles.
        Material trailerPaint = Lit(new Color(0.12f, 0.13f, 0.15f, 1f), 0.5f, 0.45f);
        Float3 kingpin = new(0f, -0.1f, 5.25f);
        Float3 fifthWheel = position + new Float3(0f, 0.04f, -2.4f);
        Rigidbody3D first = SemiTrailer(vehicle, "Semi Trailer", fifthWheel - kingpin, trailerPaint, taillight, true);
        Couple(first, kingpin, body);

        // The dolly's drawbar hooks under the back of the front trailer, and its own fifth wheel carries the second.
        Float3 eye = fifthWheel - kingpin + new Float3(0f, -0.375f, -6.5f);
        Rigidbody3D dolly = Dolly(vehicle, eye - new Float3(0f, 0f, 2.3f), trailerPaint, dual with { Radius = 0.5f });
        Couple(dolly, new Float3(0f, 0f, 2.3f), first);

        Float3 secondAt = eye - new Float3(0f, 0f, 2.3f) + new Float3(0f, 0.3f, 0f) - kingpin;
        Rigidbody3D second = SemiTrailer(vehicle, "Semi Trailer Open", secondAt, trailerPaint, taillight, false);
        Couple(second, kingpin, dolly);

        // Loading ramps from the back of the last deck down to the ground the truck stands on.
        vehicle.Ramps = new GameObject("Ramps");
        Part(second.GameObject, vehicle.Ramps, Float3.Zero);
        float drop = secondAt.Y + 0.1f - (position.Y - 1.02f);
        Float3 down = Float3.Normalize(new Float3(0f, -drop, -4.3f));
        float length = Float3.Length(new Float3(0f, drop, 4.3f));
        foreach (float x in new[] { -0.72f, 0.72f })
        {
            var ramp = new GameObject("Ramp");
            Part(vehicle.Ramps, ramp, new Float3(x, 0.07f, -6.25f) + down * (length * 0.5f));
            ramp.Transform.LocalRotation = Quaternion.FromToRotation(Float3.UnitZ, down);
            ramp.AddComponent<BoxCollider>().Size = new Float3(1.1f, 0.06f, length);
            Part(ramp, Model("Ramp Model", Mesh.CreateCube(new Float3(1.1f, 0.06f, length)), _chrome, Float3.Zero), Float3.Zero);
        }

        // Every towed axle brakes with the truck.
        car.Axles.AddRange(_towedAxles);
        _towedAxles.Clear();
        return vehicle;
    }

    // Axles of the trailers being built, handed to the truck that brakes them once the rig is complete.
    private readonly List<CarController.Axle> _towedAxles = new();

    private Rigidbody3D SemiTrailer(Vehicle vehicle, string model, Float3 position, Material paint, Material taillight, bool headboard)
    {
        var trailer = new GameObject(model);
        trailer.Transform.Position = position;
        var body = trailer.AddComponent<Rigidbody3D>();
        body.Mass = 4000f;
        AddBox(trailer, new Float3(2.6f, 0.2f, 12.5f), Float3.Zero);
        AddBox(trailer, new Float3(0.1f, 0.1f, 12.5f), new Float3(-1.27f, 0.15f, 0f));
        AddBox(trailer, new Float3(0.1f, 0.1f, 12.5f), new Float3(1.27f, 0.15f, 0f));
        if (headboard) AddBox(trailer, new Float3(2.6f, 1.2f, 0.15f), new Float3(0f, 0.7f, 6.175f));
        Dress(trailer, $"Models/{model}", paint, taillight);

        var wheels = new WheelSetup(0.5f, 0.62f, 0.3f, 1.5f, 0.6f, 2.4f);
        TowedAxle(trailer, wheels, -0.44f, -4.3f);
        TowedAxle(trailer, wheels, -0.44f, -5.6f);
        Add(trailer);
        vehicle.Towed.Add((body, position));
        return body;
    }

    private Rigidbody3D Dolly(Vehicle vehicle, Float3 position, Material paint, WheelSetup wheels)
    {
        var dolly = new GameObject("Dolly");
        dolly.Transform.Position = position;
        var body = dolly.AddComponent<Rigidbody3D>();
        body.Mass = 800f;
        AddBox(dolly, new Float3(1f, 0.2f, 1.6f), new Float3(0f, -0.02f, 0f));
        AddBox(dolly, new Float3(2.5f, 0.08f, 1.5f), new Float3(0f, 0.46f, 1.8f));
        Dress(dolly, "Models/Dolly", paint, _trim);
        TowedAxle(dolly, wheels, -0.045f, -0.15f);
        Add(dolly);
        vehicle.Towed.Add((body, position));
        return body;
    }

    private void TowedAxle(GameObject body, WheelSetup wheels, float y, float z)
    {
        CarController.Axle axle = Axle(body, wheels, DualWheel, 0.95f, y, z, steer: 0f, driven: false, handbrake: true);
        axle.Towed = true;
        _towedAxles.Add(axle);
    }

    /// <summary>Pins a point of <paramref name="body"/> to wherever it touches <paramref name="to"/>, free to turn every way.</summary>
    private static void Couple(Rigidbody3D body, Float3 anchor, Rigidbody3D to)
    {
        BallSocketConstraint joint = body.GameObject.AddComponent<BallSocketConstraint>();
        joint.Anchor = anchor;
        joint.ConnectedBody = to;
    }

    // ----------------------------------------------------------------
    //  Building blocks
    // ----------------------------------------------------------------

    private Material Taillight() => Lit(new Color(0.3f, 0f, 0f, 1f)).Emissive(new Color(1f, 0.02f, 0.02f, 1f), 1f);

    /// <summary>A body with box colliders, each a size and a centre in the body's space.</summary>
    private static GameObject Chassis(string name, Float3 position, float mass, out Rigidbody3D body, params (Float3 Size, Float3 Center)[] boxes)
    {
        var chassis = new GameObject(name);
        chassis.Transform.Position = position;
        body = chassis.AddComponent<Rigidbody3D>();
        body.Mass = mass;
        foreach ((Float3 size, Float3 center) in boxes)
        {
            var box = chassis.AddComponent<BoxCollider>();
            box.Size = size;
            box.Center = center;
        }
        return chassis;
    }

    private static void Part(GameObject parent, GameObject part, Float3 local)
    {
        part.SetParent(parent);
        part.Transform.LocalPosition = local;
        part.Transform.LocalRotation = Quaternion.Identity;
    }

    /// <summary>An instance of a model from the Assets folder, placed under <paramref name="parent"/>.</summary>
    private static GameObject Spawn(string model, GameObject parent, Float3 local, Float3? scale = null)
    {
        GameObject go = GameObject.InstantiateDetached(Load<PrefabAsset>(model))!;
        Part(parent, go, local);
        if (scale.HasValue) go.Transform.LocalScale = scale.Value;
        return go;
    }

    /// <summary>Gives each part of a model its material. The models name every part after the material it takes.</summary>
    private static void Paint(GameObject model, Dictionary<string, Material> materials)
    {
        foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>())
            if (materials.TryGetValue(renderer.GameObject.Name, out Material? material))
                renderer.Material = material;
    }

    /// <summary>Puts a vehicle model on a body, in its paint, with the shared trim, glass and lights.</summary>
    private void Dress(GameObject parent, string model, Material paint, Material taillight, Material? suit = null, Material? canvas = null)
    {
        Paint(Spawn(model, parent, Float3.Zero), new()
        {
            ["Body"] = paint,
            ["Trim"] = _trim,
            ["Chrome"] = _chrome,
            ["Glass"] = _glass,
            ["Headlights"] = _headlight,
            ["Taillights"] = taillight,
            ["Suit"] = suit ?? _trim,
            ["Helmet"] = _helmet,
            ["Canvas"] = canvas ?? paint,
        });
    }

    private static List<Light> Lamps(GameObject parent, params Float3[] positions)
    {
        var lamps = new List<Light>();
        foreach (Float3 position in positions)
        {
            var lamp = new GameObject("Headlight");
            Part(parent, lamp, position);
            lamp.Transform.LocalEulerAngles = new Float3(8f, 0f, 0f);
            SpotLight spot = lamp.AddComponent<SpotLight>();
            spot.Color = new Color(1f, 0.93f, 0.8f, 1f);
            spot.Intensity = 40f;
            spot.Range = 35f;
            spot.SpotAngle = 50f;
            spot.InnerSpotAngle = 25f;
            lamps.Add(spot);
        }
        return lamps;
    }

    /// <summary>A pair of wheels <paramref name="halfTrack"/> either side of the middle, <paramref name="z"/> along the body.</summary>
    private CarController.Axle Axle(GameObject body, WheelSetup setup, WheelModel model, float halfTrack, float y, float z, float steer, bool driven, bool handbrake)
    {
        var axle = new CarController.Axle { Steer = steer, Driven = driven, Handbrake = handbrake, Grip = setup.SidewaysGrip };
        axle.Wheels.Add(Wheel(body, new Float3(-halfTrack, y, z), setup, model));
        axle.Wheels.Add(Wheel(body, new Float3(halfTrack, y, z), setup, model));
        return axle;
    }

    private WheelCollider Wheel(GameObject body, Float3 mount, WheelSetup setup, WheelModel model)
    {
        var wheelMount = new GameObject("Wheel");
        Part(body, wheelMount, mount);

        var spin = new GameObject("Spin");
        Part(wheelMount, spin, Float3.Zero);
        float scale = setup.Radius / model.Radius;
        Paint(Spawn(model.Path, spin, Float3.Zero, new Float3(setup.Width / model.Width, scale, scale)), new() { ["Tyre"] = _tyre, ["Chrome"] = _chrome, ["Trim"] = _trim });

        var wheel = wheelMount.AddComponent<WheelCollider>();
        wheel.Radius = setup.Radius;
        wheel.Width = setup.Width;
        wheel.SuspensionDistance = setup.Travel;
        wheel.SuspensionFrequency = setup.Frequency;
        wheel.SuspensionDampingRatio = setup.Damping;
        wheel.ForwardFriction = setup.ForwardGrip;
        wheel.SidewaysFriction = setup.SidewaysGrip;
        wheel.VisualTransform = spin.Transform;
        return wheel;
    }
}
