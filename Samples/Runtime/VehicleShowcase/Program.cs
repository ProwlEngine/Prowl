// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// Vehicle Showcase
//
// Six vehicles built on WheelColliders, all parked in one world: a sports car, a rally buggy, a monster truck,
// a go-kart, a six wheeled army truck and a motorcycle. The bar along the bottom picks which one you drive.
// Around the car park are an asphalt circuit and a dirt rally track with lap timers, rough ground, a hill climb,
// an ice rink, four jumps side by side, a turntable, a ferry and a lift.
//
// The vehicle controllers live in their own files and depend on nothing else here, so they can be copied into
// a project as they are:
//   CarController.cs         any number of axles, each steered, driven or handbraked as it likes
//   MotorcycleController.cs  two wheels, balanced by leaning into the turn
//   Skidmarks.cs             marks on the ground, and smoke, dust or ice chips from slipping wheels
//   Props.cs                 moving and spinning platforms
//
// Controls:
//   1 to 6      Drive another vehicle
//   WASD        Drive, Space handbrake, Shift boost
//   T           Hitch or unhitch the trailer
//   R           Put the vehicle back where it was parked
//   Right Mouse Orbit the camera, the wheel zooms
//   Left Mouse  Drag any body around
//   F1          Hide the HUD
//

using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Samples;
using Prowl.Scribe;
using Prowl.Vector;

using static Prowl.Samples.Sample;

namespace VehicleShowcase;

internal class Program
{
    static void Main(string[] args)
    {
        new VehicleShowcaseGame().Run("Vehicle Showcase", 1600, 900);
    }
}

/// <summary>One drivable vehicle in the world, and what the showcase needs to know about it.</summary>
public sealed class Vehicle
{
    public required string Name;
    public required string Description;
    public required Rigidbody3D Body;
    public CarController? Car;
    public MotorcycleController? Bike;
    public Float3 Spawn;
    public float CameraDistance = 9f;

    /// <summary>Where the trailer hitches on, in the vehicle's space, for vehicles that can tow.</summary>
    public Float3? Hitch;

    /// <summary>Bodies coupled behind it for good, and where each was parked, so a reset puts the whole rig back.</summary>
    public readonly List<(Rigidbody3D Body, Float3 Position)> Towed = new();

    /// <summary>Loading ramps that lie down while another vehicle is driven, so it can drive aboard.</summary>
    public GameObject? Ramps;

    public float BaseTorque, BaseBrake, BaseMass;

    public IEnumerable<WheelCollider> Wheels => Car.IsValid() ? Car!.Wheels : Bike!.Wheels;
    public float Speed => Car.IsValid() ? Car!.Speed : Bike!.Speed;
    public string GearLabel => Car.IsValid() ? Car!.GearLabel : Bike!.GearLabel;
    public bool Boosting => Car.IsValid() && Car!.Boosting;

    public bool Controlled
    {
        set
        {
            if (Car.IsValid()) Car!.Controlled = value;
            if (Bike.IsValid()) Bike!.Controlled = value;
        }
    }
}

public sealed partial class VehicleShowcaseGame : StationGame
{
    private static readonly Color Orange = new(1f, 0.32f, 0.04f, 1f);
    private static readonly Color[] Palette =
    [
        Orange, new(0.02f, 0.4f, 0.35f, 1f), new(0.03f, 0.12f, 0.6f, 1f), new(0.6f, 0.03f, 0.03f, 1f),
        new(0.7f, 0.5f, 0.02f, 1f), new(0.05f, 0.45f, 0.05f, 1f), new(0.3f, 0.05f, 0.5f, 1f),
    ];

    private readonly Random _rng = new(11);
    private Material _dark = null!;
    private Material _stone = null!;
    private ChaseCamera _chase = null!;
    private readonly List<Vehicle> _vehicles = new();
    private Vehicle _current = null!;
    private Skidmarks _skidmarks = null!;

    protected override string MoveKeys => "WASD  drive    Space  handbrake    Shift  boost    Right Mouse  orbit";
    protected override string ExtraKeys => "T  trailer    R  reset    Left Mouse  drag";

    public override string Stats =>
        $"{_current.Speed * 3.6f:0} km/h    {_current.Wheels.Count(w => w.IsGrounded)} of {_current.Wheels.Count()} wheels down    {Laps.Track} lap {LapTimer.Format(Laps.Current)}    last {LapTimer.Format(Laps.Last)}    best {LapTimer.Format(Laps.Best)}    {(_hitch.IsValid() ? "trailer hitched" : "no trailer")}";

    // The timer of the track last driven onto, or the circuit's when on neither.
    private LapTimer Laps
    {
        get
        {
            LapTimer latest = _lapTimers[0];
            foreach (LapTimer timer in _lapTimers)
                if (timer.Running && (!latest.Running || timer.StartedAt > latest.StartedAt)) latest = timer;
            return latest;
        }
    }

    protected override void Build()
    {
        _dark = Lit(new Color(0.03f, 0.032f, 0.04f, 1f), 0f, 0.7f);
        _stone = Lit(new Color(0.09f, 0.09f, 0.1f, 1f), 0f, 0.85f);
        CreateVehicleMaterials();

        var sun = new GameObject("Sun");
        DirectionalLight light = sun.AddComponent<DirectionalLight>();
        light.ShadowDistance = 90f;
        light.Intensity = 0.6f;
        sun.Transform.LocalEulerAngles = new Float3(50f, 30f, 0f);
        Add(sun);

        MainCamera.FarClipPlane = 1200f;
        SampleScene.Fog.Density = 0.005f;
        _chase = CameraObject.AddComponent<ChaseCamera>();
        _chase.FollowHeading = true;

        BuildWorld();

        BuildTrailer(new Float3(0f, 1f, -8.6f));
        BuildVehicles();

        var marks = new GameObject("Skidmarks");
        marks.AddComponent<MeshRenderer>().Material = Lit(new Color(0.02f, 0.02f, 0.02f, 0.6f), 0f, 0.9f, DefaultShader.StandardTransparent);
        _skidmarks = marks.AddComponent<Skidmarks>();
        foreach (Vehicle vehicle in _vehicles) _skidmarks.Track(vehicle.Body, vehicle.Wheels);
        Add(marks);

        var spray = new GameObject("Tyre Spray");
        TyreSpray tyreSpray = spray.AddComponent<TyreSpray>();
        tyreSpray.SmokeTexture = Load<Texture2D>("Textures/Smoke Sheet");
        tyreSpray.ChipTexture = Load<Texture2D>("Textures/Soft Dot");
        foreach (Vehicle vehicle in _vehicles) tyreSpray.Track(vehicle.Wheels);
        Add(spray);

        var speedometer = new GameObject("Speedometer");
        speedometer.AddComponent<SpeedometerHud>().Game = this;
        Add(speedometer);
    }

    /// <summary>Parks a vehicle in the world and gives it a slot in the bar along the bottom.</summary>
    private Vehicle Register(Vehicle vehicle)
    {
        _vehicles.Add(vehicle);
        AddStation(vehicle.Name, vehicle.Description, vehicle.Spawn, new Float3(0f, 4f, -vehicle.CameraDistance), 1f);
        return vehicle;
    }

    protected override void OnStationChanged(int index)
    {
        CameraObject.GetComponent<FlyCamera>()!.Enabled = false;

        _current = _vehicles[index];
        foreach (Vehicle vehicle in _vehicles)
        {
            vehicle.Controlled = vehicle == _current;
            if (vehicle.Ramps.IsValid()) vehicle.Ramps!.Enabled = vehicle != _current;
        }

        Transform target = _current.Body.Transform;
        Float3 forward = target.Forward;
        _chase.Target = target;
        _chase.Distance = _current.CameraDistance;
        _chase.Yaw = MathF.Atan2(forward.X, forward.Z) * 180f / MathF.PI;
        CameraObject.Transform.Position = target.Position - forward * _current.CameraDistance + new Float3(0f, _current.CameraDistance * 0.4f, 0f);

        foreach (LapTimer timer in _lapTimers)
        {
            timer.Car = _current.Body;
            timer.Stop();
        }
    }

    public override void EndUpdate()
    {
        if (Input.GetKeyDown(KeyCode.R)) Reset(_current);
        if (Input.GetKeyDown(KeyCode.T)) ToggleHitch();
    }

    /// <summary>Puts a vehicle back where it was parked, upright and still.</summary>
    private void Reset(Vehicle vehicle)
    {
        if (vehicle == _current)
            foreach (LapTimer timer in _lapTimers) timer.Stop();
        _skidmarks.Lift();
        if (_hitch.IsValid() && _hitch!.ConnectedBody == vehicle.Body) ToggleHitch();
        Park(vehicle.Body, vehicle.Spawn);
        foreach ((Rigidbody3D body, Float3 position) in vehicle.Towed)
            Park(body, position);
    }

    private static void Park(Rigidbody3D body, Float3 position)
    {
        body.MoveRotation(Quaternion.Identity);
        body.MovePosition(position);
        body.LinearVelocity = Float3.Zero;
        body.AngularVelocity = Float3.Zero;
    }

    // ----------------------------------------------------------------
    //  Trailer
    // ----------------------------------------------------------------

    private static readonly Float3 TrailerHitch = new(0f, -0.12f, 2.75f);
    private const float DeckTop = 0.06f;
    private Rigidbody3D _trailer = null!;
    private Float3 _trailerStart;
    private GameObject _trailerJack = null!;
    private GameObject _ramps = null!;
    private GameObject _tailgate = null!;
    private BallSocketConstraint? _hitch;

    /// <summary>
    /// A flatbed on a tandem axle for carrying the kart. Standing alone it rests on a jack with its ramps down, so
    /// the kart can drive off the back and up again; hitched, the ramps stow and a tailgate holds the load in.
    /// </summary>
    private void BuildTrailer(Float3 position)
    {
        _trailerStart = position;
        var trailer = new GameObject("Trailer");
        trailer.Transform.Position = position;
        _trailer = trailer.AddComponent<Rigidbody3D>();
        _trailer.Mass = 500f;
        AddBox(trailer, new Float3(2f, 0.12f, 3.6f), Float3.Zero);
        AddBox(trailer, new Float3(0.08f, 0.12f, 3.6f), new Float3(-0.98f, 0.12f, 0f));
        AddBox(trailer, new Float3(0.08f, 0.12f, 3.6f), new Float3(0.98f, 0.12f, 0f));
        AddBox(trailer, new Float3(2f, 0.4f, 0.08f), new Float3(0f, 0.25f, 1.76f));
        Dress(trailer, "Models/Car Trailer", Lit(new Color(0.02f, 0.15f, 0.4f, 1f), 0.3f, 0.5f), Taillight());

        _trailerJack = new GameObject("Jack");
        Part(trailer, _trailerJack, new Float3(0f, -0.45f, 2.5f));
        _trailerJack.AddComponent<BoxCollider>().Size = new Float3(0.2f, 0.7f, 0.2f);
        Part(_trailerJack, Model("Jack Model", Mesh.CreateCube(new Float3(0.12f, 0.7f, 0.12f)), _trim, Float3.Zero), Float3.Zero);

        // Two ramps from the back of the deck down to the ground, as wide apart as the kart's wheels.
        _ramps = new GameObject("Ramps");
        Part(trailer, _ramps, Float3.Zero);
        Float3 down = Float3.Normalize(new Float3(0f, -0.8f, -1.75f));
        foreach (float x in new[] { -0.6f, 0.6f })
        {
            var ramp = new GameObject("Ramp");
            Part(_ramps, ramp, new Float3(x, DeckTop, -1.8f) + down * 0.95f);
            ramp.Transform.LocalRotation = Quaternion.FromToRotation(Float3.UnitZ, down);
            ramp.AddComponent<BoxCollider>().Size = new Float3(0.45f, 0.05f, 1.9f);
            Part(ramp, Model("Ramp Model", Mesh.CreateCube(new Float3(0.45f, 0.05f, 1.9f)), _chrome, Float3.Zero), Float3.Zero);
        }

        _tailgate = new GameObject("Tailgate");
        Part(trailer, _tailgate, new Float3(0f, 0.25f, -1.76f));
        _tailgate.AddComponent<BoxCollider>().Size = new Float3(2f, 0.4f, 0.08f);
        Part(_tailgate, Model("Tailgate Model", Mesh.CreateCube(new Float3(2f, 0.4f, 0.08f)), _chrome, Float3.Zero), Float3.Zero);
        _tailgate.Enabled = false;

        var wheels = new WheelSetup(0.3f, 0.22f, 0.25f, 2.2f, 0.7f);
        foreach (float z in new[] { -0.42f, 0.42f })
            foreach (float x in new[] { -1.09f, 1.09f })
                Wheel(trailer, new Float3(x, -0.2f, z), wheels, CarWheel);

        Add(trailer);
    }

    private static void AddBox(GameObject body, Float3 size, Float3 center)
    {
        var box = body.AddComponent<BoxCollider>();
        box.Size = size;
        box.Center = center;
    }

    /// <summary>Where a vehicle sits parked on the trailer's deck, for one whose body rests this high over its wheels' ground.</summary>
    private Float3 OnTrailer(float rideHeight, float along = 0f) => _trailerStart + new Float3(0f, 0.75f + DeckTop + rideHeight, along);

    private void ToggleHitch()
    {
        if (_hitch.IsValid())
        {
            // Destroy only lands at the end of the frame, and the vehicle may be teleported before then, so
            // the joint is switched off now.
            _hitch!.Enabled = false;
            _hitch.Destroy();
            _hitch = null;
            Stow(false);
            return;
        }

        if (_current.Hitch is not Float3 hitch) return;
        Rigidbody3D body = _current.Body;
        Float3 hitchWorld = body.Transform.TransformPoint(hitch);
        if (Float3.Distance(hitchWorld, _trailer.Transform.TransformPoint(TrailerHitch)) > 3f) return;

        // Line the trailer up behind the vehicle before pinning the two hitch points together, carrying whatever
        // is parked on the deck along with it.
        Quaternion rotation = body.Transform.Rotation;
        MoveTrailer(hitchWorld - rotation * TrailerHitch, rotation);
        _trailer.LinearVelocity = body.LinearVelocity;

        Stow(true);
        _hitch = _trailer.GameObject.AddComponent<BallSocketConstraint>();
        _hitch.Anchor = TrailerHitch;
        _hitch.ConnectedBody = body;
    }

    // Ramps and jack down while the trailer stands alone, stowed with the tailgate up while it is towed.
    private void Stow(bool towing)
    {
        _trailerJack.Enabled = !towing;
        _ramps.Enabled = !towing;
        _tailgate.Enabled = towing;
    }

    private void MoveTrailer(Float3 position, Quaternion rotation)
    {
        Transform deck = _trailer.Transform;
        var riders = new List<(Vehicle Vehicle, Float3 Position, Quaternion Rotation)>();
        foreach (Vehicle vehicle in _vehicles)
            if (vehicle.Wheels.Any(wheel => wheel.GetGroundHit(out WheelHit hit) && hit.Rigidbody == _trailer))
                riders.Add((vehicle, deck.InverseTransformPoint(vehicle.Body.Transform.Position), Quaternion.Inverse(deck.Rotation) * vehicle.Body.Transform.Rotation));

        _trailer.MoveRotation(rotation);
        _trailer.MovePosition(position);
        _trailer.AngularVelocity = Float3.Zero;
        foreach ((Vehicle vehicle, Float3 local, Quaternion localRotation) in riders)
        {
            vehicle.Body.MoveRotation(rotation * localRotation);
            vehicle.Body.MovePosition(position + rotation * local);
            vehicle.Body.LinearVelocity = Float3.Zero;
            vehicle.Body.AngularVelocity = Float3.Zero;
        }
    }

    private void BringTrailerBack()
    {
        if (_hitch.IsValid()) ToggleHitch();
        MoveTrailer(_trailerStart, Quaternion.Identity);
        _trailer.LinearVelocity = Float3.Zero;
    }

    // ----------------------------------------------------------------
    //  Panel
    // ----------------------------------------------------------------

    public override void DrawControls(Paper paper, FontFile font)
    {
        if (_current.Car.IsValid()) CarControls(paper, font, _current, _current.Car!);
        else BikeControls(paper, font, _current, _current.Bike!);

        Header(paper, font, "Vehicle", 2);
        Slider(paper, font, "Camera distance", _chase.Distance, 3f, 25f, v => _chase.Distance = v, "0.0");
        if (_current.Hitch.HasValue)
            Button(paper, font, _hitch.IsValid() ? "Unhitch the trailer (T)" : "Hitch the trailer (T)", ToggleHitch);
        Button(paper, font, "Back where it was parked (R)", () => Reset(_current));
        Button(paper, font, "Bring the trailer back", BringTrailerBack);
    }

    private static void CarControls(Paper paper, FontFile font, Vehicle vehicle, CarController car)
    {
        Header(paper, font, "Drive");
        Slider(paper, font, "Engine torque", car.Torque, 0f, vehicle.BaseTorque * 2.5f, v => car.Torque = v, "0");
        Slider(paper, font, "Top speed (km/h)", car.TopSpeed * 3.6f, 20f, 300f, v => car.TopSpeed = v / 3.6f, "0");
        Slider(paper, font, "Brakes", car.Brake, 0f, vehicle.BaseBrake * 2.5f, v => car.Brake = v, "0");
        Slider(paper, font, "Steering angle", car.MaxSteer, 5f, 45f, v => car.MaxSteer = v, "0");
        Slider(paper, font, "Downforce", car.Downforce, 0f, 6f, v => car.Downforce = v, "0.0");
        Slider(paper, font, "Air stability", car.AirStability, 0f, 2f, v => car.AirStability = v);
        Toggle(paper, font, "Traction control", car.TractionControl, v => car.TractionControl = v);
        Toggle(paper, font, "Stability control", car.StabilityControl, v => car.StabilityControl = v);
        Toggle(paper, font, "Headlights", car.LightsOn, v => car.LightsOn = v);

        Header(paper, font, "Tyres and suspension", 1);
        string[] names = car.Axles.Count == 2 ? ["Front", "Rear"] : ["Front", "Middle", "Rear"];
        for (int i = 0; i < car.Axles.Count && i < names.Length; i++)
        {
            CarController.Axle axle = car.Axles[i];
            Slider(paper, font, $"{names[i]} grip", axle.Grip, 0.2f, 3f, v => axle.Grip = v);
        }
        SuspensionControls(paper, font, vehicle);
    }

    private static void BikeControls(Paper paper, FontFile font, Vehicle vehicle, MotorcycleController bike)
    {
        Header(paper, font, "Ride");
        Slider(paper, font, "Engine torque", bike.Torque, 0f, vehicle.BaseTorque * 2.5f, v => bike.Torque = v, "0");
        Slider(paper, font, "Top speed (km/h)", bike.TopSpeed * 3.6f, 20f, 300f, v => bike.TopSpeed = v / 3.6f, "0");
        Slider(paper, font, "Brakes", bike.Brake, 0f, vehicle.BaseBrake * 2.5f, v => bike.Brake = v, "0");
        Slider(paper, font, "Most lean", bike.MaxLean, 10f, 60f, v => bike.MaxLean = v, "0");
        Slider(paper, font, "Lean response", bike.LeanResponse, 2f, 15f, v => bike.LeanResponse = v, "0.0");
        Slider(paper, font, "Turn assist", bike.TurnAssist, 1f, 3f, v => bike.TurnAssist = v);
        Slider(paper, font, "Steering at walking pace", bike.MaxSteer, 10f, 45f, v => bike.MaxSteer = v, "0");
        Toggle(paper, font, "Traction control", bike.TractionControl, v => bike.TractionControl = v);
        Toggle(paper, font, "Headlight", bike.LightsOn, v => bike.LightsOn = v);
        Label(paper, font, $"Leaning {MathF.Abs(bike.Lean):0} degrees {(bike.Lean > 0.5f ? "right" : bike.Lean < -0.5f ? "left" : "")}");

        Header(paper, font, "Tyres and suspension", 1);
        Slider(paper, font, "Grip", bike.Rear.SidewaysFriction, 0.2f, 3f, v => { foreach (WheelCollider w in bike.Wheels) w.SidewaysFriction = v; });
        SuspensionControls(paper, font, vehicle);
    }

    private static void SuspensionControls(Paper paper, FontFile font, Vehicle vehicle)
    {
        WheelCollider first = vehicle.Wheels.First();
        Slider(paper, font, "Spring stiffness (Hz)", first.SuspensionFrequency, 0.8f, 6f, v => { foreach (WheelCollider w in vehicle.Wheels) w.SuspensionFrequency = v; });
        Slider(paper, font, "Damping", first.SuspensionDampingRatio, 0.1f, 1.5f, v => { foreach (WheelCollider w in vehicle.Wheels) w.SuspensionDampingRatio = v; });
        Slider(paper, font, "Suspension travel", first.SuspensionDistance, 0.03f, 1f, v => { foreach (WheelCollider w in vehicle.Wheels) w.SuspensionDistance = v; });
        Slider(paper, font, "Mass", vehicle.Body.Mass, vehicle.BaseMass * 0.4f, vehicle.BaseMass * 2.5f, v => { vehicle.Body.Mass = v; foreach (WheelCollider w in vehicle.Wheels) w.Recalculate(); }, "0");
    }

    /// <summary>Floats the speedometer in the bottom left corner, above the vehicle bar, while the HUD shows.</summary>
    internal void DrawSpeedometer(Paper paper)
    {
        FontFile? font = FontAsset.LoadDefault().FontFile;
        if (font == null || !Hud.Visible) return;

        using (paper.Box("speedometer")
            .PositionType(PositionType.SelfDirected)
            .AnchorLeft(20).AnchorBottom(84).Width(220).Height(190)
            .IsNotInteractable()
            .Enter())
            paper.Draw((canvas, rect) => DrawSpeedometer(canvas, rect, font));
    }

    private void DrawSpeedometer(Prowl.Quill.Canvas canvas, Rect rect, FontFile font)
    {
        float cx = rect.Min.X + rect.Size.X * 0.5f, cy = rect.Min.Y + rect.Size.Y * 0.56f;
        float radius = MathF.Min(rect.Size.X * 0.5f, rect.Size.Y * 0.56f) - 6f;
        const float MaxKmh = 240f;
        const float Start = MathF.PI * 0.75f, Sweep = MathF.PI * 1.5f;
        float kmh = _current.Speed * 3.6f;
        float fraction = Math.Clamp(kmh / MaxKmh, 0f, 1f);
        Color dim = new(1f, 1f, 1f, 0.15f);

        canvas.CircleFilled(cx, cy, radius + 4f, new Color(0f, 0f, 0f, 0.35f));

        canvas.BeginPath();
        canvas.Arc(cx, cy, radius - 8f, Start, Start + Sweep);
        canvas.SetStrokeColor(dim);
        canvas.SetStrokeWidth(10f);
        canvas.Stroke();

        if (fraction > 0.001f)
        {
            canvas.BeginPath();
            canvas.Arc(cx, cy, radius - 8f, Start, Start + Sweep * fraction);
            canvas.SetStrokeColor(_current.Boosting ? new Color(0.4f, 0.8f, 1f, 1f) : SampleHud.Accent);
            canvas.SetStrokeWidth(10f);
            canvas.Stroke();
        }

        for (int kmhTick = 0; kmhTick <= MaxKmh; kmhTick += 10)
        {
            float a = Start + Sweep * kmhTick / MaxKmh;
            bool major = kmhTick % 40 == 0;
            float inner = radius - (major ? 30f : 22f), outer = radius - 16f;
            canvas.BeginPath();
            canvas.MoveTo(cx + MathF.Cos(a) * inner, cy + MathF.Sin(a) * inner);
            canvas.LineTo(cx + MathF.Cos(a) * outer, cy + MathF.Sin(a) * outer);
            canvas.SetStrokeColor(new Color(1f, 1f, 1f, major ? 0.8f : 0.35f));
            canvas.SetStrokeWidth(major ? 2f : 1f);
            canvas.Stroke();

            if (major)
                canvas.DrawText(kmhTick.ToString(), cx + MathF.Cos(a) * (radius - 42f), cy + MathF.Sin(a) * (radius - 42f), new Color(1f, 1f, 1f, 0.6f), 11f, font, origin: new Float2(0.5f, 0.5f));
        }

        float needle = Start + Sweep * fraction;
        canvas.BeginPath();
        canvas.MoveTo(cx - MathF.Cos(needle) * 10f, cy - MathF.Sin(needle) * 10f);
        canvas.LineTo(cx + MathF.Cos(needle) * (radius - 20f), cy + MathF.Sin(needle) * (radius - 20f));
        canvas.SetStrokeColor(new Color(1f, 0.3f, 0.2f, 1f));
        canvas.SetStrokeWidth(3f);
        canvas.SetStrokeJoint(Prowl.Quill.JointStyle.Round);
        canvas.Stroke();
        canvas.CircleFilled(cx, cy, 7f, new Color(0.9f, 0.9f, 0.92f, 1f));

        canvas.DrawText($"{kmh:0}", cx, cy + radius * 0.42f, Color.White, 26f, font, origin: new Float2(0.5f, 0.5f));
        canvas.DrawText($"km/h  {_current.GearLabel}", cx, cy + radius * 0.42f + 20f, new Color(1f, 1f, 1f, 0.55f), 11f, font, origin: new Float2(0.5f, 0.5f));
    }
}

/// <summary>Times laps as the vehicle being driven crosses the start line.</summary>
public sealed class LapTimer : MonoBehaviour
{
    public string Track = "";
    public Rigidbody3D? Car;
    public float Current;
    public float Last;
    public float Best;
    public int Laps;
    private bool _running;
    private float _sinceCrossing;

    public bool Running => _running;
    public float StartedAt { get; private set; }

    public override void Update()
    {
        _sinceCrossing += Time.DeltaTime;
        if (_running) Current += Time.DeltaTime;
    }

    public override void OnTriggerEnter(Rigidbody3D other)
    {
        if (other != Car || _sinceCrossing < 5f) return;
        _sinceCrossing = 0f;

        if (_running && Current > 10f)
        {
            Last = Current;
            Best = Best <= 0f ? Current : MathF.Min(Best, Current);
            Laps++;
        }

        _running = true;
        StartedAt = Time.TimeSinceStartup;
        Current = 0f;
    }

    public void Stop()
    {
        _running = false;
        Current = 0f;
    }

    public static string Format(float seconds) => seconds <= 0f ? "--" : $"{(int)(seconds / 60f)}:{seconds % 60f:00.00}";
}

/// <summary>Draws the showcase's speedometer over the scene.</summary>
public sealed class SpeedometerHud : MonoBehaviour
{
    public VehicleShowcaseGame Game = null!;

    public override void OnGui(Paper paper) => Game.DrawSpeedometer(paper);
}
