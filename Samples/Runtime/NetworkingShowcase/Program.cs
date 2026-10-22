// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// Networking Showcase
//
// One window hosts; "Launch a client" opens another that joins it over KCP on this machine (or run
// NetworkingShowcase --join host:port). Each station shows one thing networking does, in every window at once:
//   1  Sync values      [Sync] properties, set by whoever controls the object, ignored anywhere else
//   2  Ownership        grab a puck with the mouse to take control of it; the host hands the rover around
//   3  RPCs             calls that run on everyone, or on the host, and know who made them
//   4  Change hooks     [Change] methods with the old and new value, wherever the value changes
//   5  Collections      NetList, NetDictionary and NetSet, sending each change rather than everything
//   6  Custom structs   the game's own struct as a [Sync] value and an RPC argument, with nothing to mark it
//   7  Spawning         the host creating networked objects at run time, for itself or for a client
//   8  Physics          rigidbodies simulated by whoever controls them; grab one to take it over and throw it
//   9  Stress test      hundreds of networked objects moving and changing every frame
//  10  Late join        everything above, there at once for whoever joins last
//
// Clients follow the host from station to station. The components in
// Stages are written as game scripts are, and compile as they do (ProwlCodeGen in the project).
//
// Controls:
//   1 to 9, 0     Jump to a station
//   WASD, Q/E     Fly, hold Right Mouse to look, Shift to go faster
//   Left Mouse    Grab and drag a puck or a physics body
//   F1            Hide the HUD
//   The panel on the right holds each station's controls; the one under it, the session's.
//

using System.Diagnostics;

using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Samples;
using Prowl.Scribe;
using Prowl.Vector;
using Prowl.Warp;
using Prowl.Warp.Transports.Kcp;

using static Prowl.Samples.Sample;

namespace NetworkingShowcase;

internal class Program
{
    static void Main(string[] args)
    {
        int join = Array.IndexOf(args, "--join");
        string? address = join >= 0 && join + 1 < args.Length ? args[join + 1] : null;
        new NetworkingShowcaseGame(address).Run(address == null ? "Networking Showcase (host)" : "Networking Showcase (client)", 1280, 760);
    }
}

public sealed class NetworkingShowcaseGame(string? joinAddress) : StationGame
{
    private const int Port = 7777;
    private const float StationSpacing = 18f;
    private const int OwnershipStation = 1;
    private const float PuckHeight = 0.3f;

    private bool _started;
    private string _status = "Starting";
    private Material _stone = null!;

    private Grabbable? _dragging;
    private int _stressCount = 200;

    // Bytes this window sent and received a second, measured over the last second.
    private long _lastSent, _lastReceived;
    private double _sentPerSecond, _receivedPerSecond, _nextRate;

    private bool IsClientWindow => joinAddress != null;

    // The host takes clients from station to station, so they have none to pick.
    public override bool ChoosesStations => !IsClientWindow;

    private static Float3 StationCenter(int index) => new(index * StationSpacing, 0f, 0f);

    protected override void Build()
    {
        // The camera and the HUD are this window's own: never sent, and kept when a client's scene is
        // replaced by the host's.
        foreach (GameObject local in new[] { CameraObject, Hud.GameObject })
        {
            local.Network.Mode = NetworkMode.Never;
            Scene.DontDestroyOnLoad(local);
        }

        AddStation("Sync values", "The lamp's color and height are [Sync] properties. The host controls the lamp, so it sets them and every window shows them at the next network tick. On a client, setting them does nothing.", StationCenter(0), new Float3(0f, 4f, -9f));
        AddStation("Ownership", "Grab a puck with the left mouse: it asks the host for control, and once it has it, this window moves the puck and the others follow, smoothed. The rover is driven by whoever the host hands it to. Colors are owners.", StationCenter(1), new Float3(0f, 9f, -9f), 0f);
        AddStation("RPCs", "Ring the bell and it rings in every window, yours too: a broadcast RPC. Ask the host and only the host runs the call, counting the request in a value only it may set. Each call knows who made it.", StationCenter(2), new Float3(0f, 3f, -7f));
        AddStation("Change hooks", "The reactor's heat is [Sync] with a [Change] hook, which runs with the old and new heat in every window the heat changes in. Anyone can stoke it through a host RPC; the host is the one that changes it.", StationCenter(3), new Float3(0f, 3f, -7f));
        AddStation("Collections", "The bars are a NetList: adding one sends one bar, not all of them. Votes go to a NetDictionary and voters to a NetSet, through a host RPC. Every window rebuilds its bars from the lists' Changed events.", StationCenter(4), new Float3(0f, 4f, -9f));
        AddStation("Custom structs", "The loadout is a struct of the game's own. Nothing marks it for the network: the script compiler sees it used and writes its serializer. It's a [Sync] value, and the argument of a broadcast RPC.", StationCenter(5), new Float3(0f, 3f, -7f));
        AddStation("Spawning", "Only the host creates networked objects. Ask for a crate and the host spawns one owned by you, which you then drive: it bobs in your color in every window. Close a client and its crates go with it.", StationCenter(6), new Float3(0f, 6f, -10f));
        AddStation("Physics", "Each rigidbody simulates only in the window that controls it; everywhere else it's driven by the transform that window sends. Grab one with the left mouse to take it over, then throw it: it keeps its speed as control changes hands. Bodies other windows control push yours without being pushed back.", StationCenter(7), new Float3(0f, 6f, -11f));
        AddStation("Stress test", "The host spawns drones, each a networked object moving every frame. Churn changes a [Sync] value on every drone every frame too, and spreading hands them out so every window drives its share. Watch the traffic and the frame rate.", StationCenter(8), new Float3(0f, 7f, -13f));
        AddStation("Late join", "Launch another client after changing things above. It gets the scene, every [Sync] value, every collection and every spawned object at once, without change hooks firing for any of it. The guestbook is a NetList the host signs for each arrival.", StationCenter(9), new Float3(0f, 3f, -8f));

        // A body another window controls is kinematic here; clicking it asks the host for it, and the grab takes
        // hold when control arrives and the body simulates here again.
        CameraObject.GetComponent<PhysicsGrabber>()!.UnheldClicked += body => body.GetComponent<Grabbable>()?.TakeControl();

        // A client's world is the host's, and arrives when it joins.
        if (!IsClientWindow)
            BuildWorld();
    }

    private void BuildWorld()
    {
        _stone = Lit(new Color(0.08f, 0.08f, 0.09f, 1f), 0f, 0.8f);

        GameObject sun = new("Sun");
        Light light = sun.AddComponent<Light>();
        light.Type = LightType.Directional;
        light.ShadowDistance = 60f;
        light.Intensity = 0.7f;
        sun.Transform.LocalEulerAngles = new Float3(50f, 30f, 0f);
        Add(sun);

        float length = StationSpacing * Stations.Count + 20f;
        Add(Block("Floor", new Float3(length, 1f, 30f), Floor(length, 30f), new Float3(StationSpacing * (Stations.Count - 1) * 0.5f, -0.5f, 0f)));
        Networked(new GameObject("Tour")).AddComponent<Tour>();

        Pedestal(StationCenter(0));
        GameObject lamp = Networked(Model("Lamp", Mesh.CreateSphere(0.6f, 16, 24), Lit(SyncLamp.Colors[0]), StationCenter(0) + new Float3(0f, 1.5f, 0f)));
        lamp.AddComponent<SyncLamp>();
        lamp.Network.Flags = NetworkFlags.NoTransformSync;

        Add(Block("Track", new Float3(6.6f, 0.05f, 6.6f), _stone, StationCenter(1) + new Float3(0f, 0.03f, 0f)));
        GameObject rover = Networked(Model("Rover", Mesh.CreateCube(new Float3(1f, 0.6f, 1.4f)), Lit(new Color(0.9f, 0.9f, 0.95f, 1f)), StationCenter(1) + new Float3(3f, 0.6f, 0f)));
        rover.AddComponent<Rover>().Center = StationCenter(1);
        for (int i = 0; i < 4; i++)
        {
            Float3 at = StationCenter(1) + new Float3(-1.5f + i, PuckHeight, -1.2f + (i % 2) * 2.4f);
            Networked(Model($"Puck {i + 1}", Mesh.CreateCylinder(0.4f, 0.2f, 24), Lit(new Color(0.9f, 0.9f, 0.95f, 1f)), at)).AddComponent<Grabbable>().Home = at;
        }

        Pedestal(StationCenter(2));
        Networked(Model("Bell", Mesh.CreateCone(0.7f, 1.2f), Lit(new Color(0.6f, 0.6f, 0.65f, 1f), 0.8f, 0.3f), StationCenter(2) + new Float3(0f, 1.8f, 0f), new Float3(180f, 0f, 0f)))
            .AddComponent<Bell>();

        Pedestal(StationCenter(3));
        Networked(Model("Reactor", Mesh.CreateCylinder(0.6f, 1.4f, 24), Lit(new Color(0.1f, 0.35f, 1f, 1f)), StationCenter(3) + new Float3(0f, 1.7f, 0f)))
            .AddComponent<Reactor>();

        Networked(new GameObject("Board") { Transform = { Position = StationCenter(4) } }).AddComponent<Board>();

        Pedestal(StationCenter(5));
        Networked(Model("Loadout", Mesh.CreateCube(new Float3(0.8f, 1f, 0.8f)), Lit(Armory.Weapons[0].Tint), StationCenter(5) + new Float3(0f, 1.5f, 0f)))
            .AddComponent<Armory>();

        Add(Block("Hangar Floor", new Float3(7f, 0.05f, 5f), _stone, StationCenter(6) + new Float3(0f, 0.03f, 0f)));
        Networked(new GameObject("Hangar") { Transform = { Position = StationCenter(6) } }).AddComponent<Hangar>();

        BuildPhysics(StationCenter(7));

        Add(Block("Swarm Floor", new Float3(13f, 0.05f, 9f), _stone, StationCenter(8) + new Float3(0f, 0.03f, 0f)));
        Networked(new GameObject("Swarm") { Transform = { Position = StationCenter(8) } }).AddComponent<Swarm>();

        Pedestal(StationCenter(9));
        Networked(new GameObject("Guestbook") { Transform = { Position = StationCenter(9) } }).AddComponent<Guestbook>();
    }

    /// <summary>A walled pen of boxes and balls, each a networked rigidbody anyone can grab.</summary>
    private void BuildPhysics(Float3 c)
    {
        Add(Block("Pen Wall", new Float3(10f, 1f, 0.3f), _stone, c + new Float3(0f, 0.5f, 4f)));
        Add(Block("Pen Wall", new Float3(10f, 1f, 0.3f), _stone, c + new Float3(0f, 0.5f, -4f)));
        Add(Block("Pen Wall", new Float3(0.3f, 1f, 8f), _stone, c + new Float3(5f, 0.5f, 0f)));
        Add(Block("Pen Wall", new Float3(0.3f, 1f, 8f), _stone, c + new Float3(-5f, 0.5f, 0f)));

        for (int i = 0; i < 16; i++)
        {
            bool ball = i % 3 == 2;
            Float3 at = c + new Float3(-3f + i % 4 * 2f, 0.5f + i / 4 * 0.9f, -1.5f + i / 4 % 2 * 1.5f);
            GameObject body = Networked(ball
                ? Model($"Ball {i + 1}", Mesh.CreateSphere(0.4f, 12, 18), Lit(new Color(0.9f, 0.9f, 0.95f, 1f)), at)
                : Model($"Box {i + 1}", Mesh.CreateCube(new Float3(0.8f, 0.8f, 0.8f)), Lit(new Color(0.9f, 0.9f, 0.95f, 1f)), at));
            body.AddComponent<Rigidbody3D>().Mass = ball ? 1f : 2f;
            if (ball)
                body.AddComponent<SphereCollider>().Radius = 0.4f;
            else
                body.AddComponent<BoxCollider>().Size = new Float3(0.8f, 0.8f, 0.8f);
            body.AddComponent<Grabbable>().Home = at;
        }
    }

    /// <summary>Host: every body back to the host and where it started, at rest.</summary>
    private static void RestackPhysics()
    {
        foreach (Grabbable? item in Scene.Current.FindObjectsOfType<Grabbable>())
        {
            if (item?.GetComponent<Rigidbody3D>() is not { } body)
                continue;
            item.GameObject.Network.AssignOwnership(null);
            item.Transform.Position = item.Home;
            item.Transform.Rotation = Quaternion.Identity;
            body.LinearVelocity = Float3.Zero;
            body.AngularVelocity = Float3.Zero;
        }
    }

    private void Pedestal(Float3 center) => Add(Block("Pedestal", new Float3(1.6f, 1f, 1.6f), _stone, center + new Float3(0f, 0.5f, 0f)));

    /// <summary>Placed in the scene as a networked object, which belongs to the host until it hands it out.</summary>
    private GameObject Networked(GameObject go)
    {
        go.Network.Mode = NetworkMode.Object;
        return Add(go);
    }

    // --------------------------------------------------------------------
    //  Session
    // --------------------------------------------------------------------

    protected override void Tick()
    {
        // Start after the initial scene activates, so the local camera and HUD are ready on either peer.
        if (!_started && Scene.Current == SampleScene)
        {
            _started = true;
            StartSession();
        }

        if (!Networking.IsActive)
            return;

        FollowTheHost();
        DragPucks();
        MeasureTraffic();
    }

    private void StartSession()
    {
        try
        {
            if (IsClientWindow)
            {
                NetSession session = Networking.Join(new KcpTransport(), joinAddress!, $"Client {Environment.ProcessId % 1000}");
                session.Ended += (reason, message) => _status = $"Session ended: {reason}{(message == null ? "" : ", " + message)}";
                _status = $"Joining {joinAddress}";
            }
            else
            {
                NetSession session = Networking.Host(new KcpTransport(new KcpTransportOptions { Port = Port }), "Host");
                session.PeerJoined += peer => Find<Guestbook>()?.Sign($"{peer.Name} joined at {DateTime.Now:HH:mm:ss}");
                session.PeerLeft += (peer, reason) => Find<Guestbook>()?.Sign($"{peer.Name} left ({reason})");
                Find<Guestbook>()?.Sign($"The host opened at {DateTime.Now:HH:mm:ss}");
                _status = $"Hosting on port {Networking.LocalPort}";
            }
        }
        catch (Exception e)
        {
            _status = $"Couldn't start the session: {e.Message}";
        }
    }

    /// <summary>The host tells everyone where it is, and clients go there.</summary>
    private void FollowTheHost()
    {
        if (Find<Tour>() is not { } tour)
            return;

        if (Networking.IsHost)
            tour.Station = CurrentStation;
        else if (tour.Station != CurrentStation)
            GoToStation(tour.Station);
    }

    protected override void OnStationChanged(int index) => _dragging = null;

    /// <summary>Left mouse on a puck asks for control of it; while this window has it, the puck follows the mouse along the floor.</summary>
    private void DragPucks()
    {
        if (CurrentStation != OwnershipStation)
        {
            _dragging = null;
            return;
        }

        Ray ray = MainCamera.ScreenPointToRay(new Float2(Input.MousePosition.X, Input.MousePosition.Y));
        if (Input.GetMouseButtonDown(0) && PaperInstance?.WantsCapturePointer != true)
        {
            _dragging = Scene.Current.FindObjectsOfType<Grabbable>().OfType<Grabbable>()
                .Where(p => p.GetComponent<Rigidbody3D>() == null && Hits(ray, p.Transform.Position, 0.5f))
                .OrderBy(p => Float3.Distance(ray.Origin, p.Transform.Position))
                .FirstOrDefault();
            if (_dragging != null && _dragging.GameObject.IsProxy)
                _dragging.TakeControl();
        }

        if (!Input.GetMouseButton(0))
            _dragging = null;

        // Until the host hands it over, the puck is a proxy here, and moving it would be undone.
        if (_dragging is { } puck && puck.IsValid() && !puck.GameObject.IsProxy && ray.Direction.Y < -0.01f)
            puck.Transform.Position = ray.Origin + ray.Direction * ((PuckHeight - ray.Origin.Y) / ray.Direction.Y);
    }

    private static bool Hits(Ray ray, Float3 center, float radius)
    {
        Float3 to = center - ray.Origin;
        float along = Float3.Dot(to, ray.Direction);
        return along > 0f && Float3.LengthSquared(to - ray.Direction * along) <= radius * radius;
    }

    private void MeasureTraffic()
    {
        double now = Time.TimeSinceStartup;
        if (now < _nextRate)
            return;

        long sent = 0, received = 0;
        foreach (NetPeer peer in Networking.Session!.Peers)
        {
            if (peer.Connection?.Stats is { } stats)
            {
                sent += stats.BytesSent;
                received += stats.BytesReceived;
            }
        }

        _sentPerSecond = sent - _lastSent;
        _receivedPerSecond = received - _lastReceived;
        (_lastSent, _lastReceived) = (sent, received);
        _nextRate = now + 1.0;
    }

    private static void LaunchClient()
    {
        string self = Environment.ProcessPath!;
        string arguments = $"--join 127.0.0.1:{Networking.LocalPort}";
        if (Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            arguments = $"\"{typeof(Program).Assembly.Location}\" {arguments}";
        Process.Start(new ProcessStartInfo(self, arguments) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory });
    }

    /// <summary>The stage's component in the replicated scene, which on a client is the host's, once it has arrived.</summary>
    private static T? Find<T>() where T : Component => Scene.Current.FindObjectsOfType<T>().FirstOrDefault(c => c.IsValid());

    private static string Who(NetPeer? peer) => peer == null ? "the host" : peer.IsLocal ? "you" : peer.Name;

    private static string Control(Component c) => c.GameObject.IsProxy ? "a proxy in this window" : "controlled from this window";

    private static string Kilobytes(double bytes) => $"{bytes / 1024.0:0.0} KB/s";

    public override string Stats
    {
        get
        {
            if (!Networking.IsActive)
                return _status;

            return CurrentStation switch
            {
                0 when Find<SyncLamp>() is { } lamp => $"Color {lamp.ColorIndex}, height {lamp.Height:0.0}, {Control(lamp)}",
                1 when Find<Rover>() is { } rover => $"Rover driven by {Who(rover.GameObject.Network.Owner)} at speed {rover.Speed:0.0}    "
                    + $"Pucks here: {Scene.Current.FindObjectsOfType<Grabbable>().Count(p => p != null && p.GetComponent<Rigidbody3D>() == null && !p.GameObject.IsProxy)} of 4",
                2 when Find<Bell>() is { } bell => $"The host has counted {bell.Requests} request(s)",
                3 when Find<Reactor>() is { } reactor => $"Heat {reactor.Heat}",
                4 when Find<Board>() is { } board => $"{board.Bars.Count} bar(s), {board.Votes.Values.Sum()} vote(s) from {board.Voters.Count} voter(s)",
                5 when Find<Armory>() is { } armory => $"{armory.Current.Weapon}, {armory.Current.Ammo} of {armory.Current.Magazine}{(armory.Current.Silenced ? ", silenced" : "")}",
                6 when Find<Hangar>() is { } hangar => $"{hangar.Crates} crate(s) in the hangar",
                7 => PhysicsStats(),
                8 when Find<Swarm>() is { } swarm => $"{swarm.Drones} drone(s), {Networking.NetworkObjectCount} networked object(s)    "
                    + $"Sent {Kilobytes(_sentPerSecond)}, received {Kilobytes(_receivedPerSecond)}",
                9 when Find<Guestbook>() is { } book => $"{book.Entries.Count} entr(ies) in the guestbook, {Networking.Session!.Peers.Count} peer(s) here now",
                _ => "Waiting for the host's scene",
            };
        }
    }

    // --------------------------------------------------------------------
    //  Controls
    // --------------------------------------------------------------------

    public override void DrawControls(Paper paper, FontFile font)
    {
        if (!Networking.IsActive)
        {
            Label(paper, font, _status);
            return;
        }

        switch (CurrentStation)
        {
            case 0 when Find<SyncLamp>() is { } lamp: LampControls(paper, font, lamp); break;
            case 1 when Find<Rover>() is { } rover: OwnershipControls(paper, font, rover); break;
            case 2 when Find<Bell>() is { } bell: BellControls(paper, font, bell); break;
            case 3 when Find<Reactor>() is { } reactor: ReactorControls(paper, font, reactor); break;
            case 4 when Find<Board>() is { } board: BoardControls(paper, font, board); break;
            case 5 when Find<Armory>() is { } armory: ArmoryControls(paper, font, armory); break;
            case 6 when Find<Hangar>() is { } hangar: HangarControls(paper, font, hangar); break;
            case 7: PhysicsControls(paper, font); break;
            case 8 when Find<Swarm>() is { } swarm: StressControls(paper, font, swarm); break;
            case 9 when Find<Guestbook>() is { } book: GuestbookControls(paper, font, book); break;
            default: Label(paper, font, "Waiting for the host's scene"); break;
        }
    }

    private static void LampControls(Paper paper, FontFile font, SyncLamp lamp)
    {
        Header(paper, font, "Lamp");
        if (lamp.GameObject.IsProxy)
            Label(paper, font, "The host controls the lamp. Try it: nothing changes.", 1);
        Button(paper, font, "Next color", lamp.NextColor);
        Slider(paper, font, "Height", lamp.Height, 0.8f, 4f, v => lamp.Height = v, "0.0");
    }

    private static void OwnershipControls(Paper paper, FontFile font, Rover rover)
    {
        Header(paper, font, "Pucks");
        Label(paper, font, "Drag one with the left mouse to take it over.", 1);

        Header(paper, font, "Rover", 2);
        Label(paper, font, $"Driven by {Who(rover.GameObject.Network.Owner)}", 3);
        if (rover.GameObject.IsProxy)
            Label(paper, font, "This window follows its transform.", 4);
        else
            Slider(paper, font, "Speed", rover.Speed, -3f, 3f, v => rover.Speed = v, "0.0", 5);

        if (!Networking.IsHost)
            return;

        Header(paper, font, "Hand the rover to", 6);
        Button(paper, font, "The host", () => rover.GameObject.Network.AssignOwnership(null), 7);
        int id = 8;
        foreach (NetPeer peer in Networking.Session!.Peers.Where(p => !p.IsLocal))
            Button(paper, font, peer.Name, () => rover.GameObject.Network.AssignOwnership(peer), id++);
    }

    private static void BellControls(Paper paper, FontFile font, Bell bell)
    {
        Header(paper, font, "Bell");
        Button(paper, font, "Ring it, everywhere", () => bell.Ring(Random.Shared.Next(SyncLamp.Colors.Length)), 1);
        Button(paper, font, "Ask the host", bell.AskHost, 2);
        Header(paper, font, "Calls in this window", 3);
        for (int i = 0; i < bell.Log.Count; i++)
            Label(paper, font, bell.Log[i], 10 + i);
    }

    private static void ReactorControls(Paper paper, FontFile font, Reactor reactor)
    {
        Header(paper, font, "Reactor");
        Button(paper, font, "Stoke it (+10)", () => reactor.Stoke(10), 1);
        Button(paper, font, "Cool it (-10)", () => reactor.Stoke(-10), 2);
        Header(paper, font, "Hook calls in this window", 3);
        for (int i = 0; i < reactor.Log.Count; i++)
            Label(paper, font, reactor.Log[i], 10 + i);
    }

    private static void BoardControls(Paper paper, FontFile font, Board board)
    {
        if (Networking.IsHost)
        {
            Header(paper, font, "Bars");
            Button(paper, font, "Add a bar", board.AddBar, 1);
            Button(paper, font, "Grow the last", board.GrowLast, 2);
            Button(paper, font, "Remove the first", board.RemoveFirst, 3);
            Button(paper, font, "Clear", board.Bars.Clear, 4);
        }

        Header(paper, font, "Vote", 5);
        foreach ((string option, int id) in new[] { ("Red", 6), ("Green", 7), ("Blue", 8) })
            Button(paper, font, $"{option}  ({(board.Votes.TryGetValue(option, out int votes) ? votes : 0)})", () => board.Vote(option), id);
        Label(paper, font, board.Voters.Count == 0 ? "Nobody has voted" : "Voters: " + string.Join(", ", board.Voters), 9);

        Header(paper, font, "Changes seen here", 10);
        for (int i = 0; i < board.Events.Count; i++)
            Label(paper, font, board.Events[i], 20 + i);
    }

    private static void ArmoryControls(Paper paper, FontFile font, Armory armory)
    {
        Header(paper, font, "Loadout");
        if (Networking.IsHost)
        {
            Button(paper, font, "Next weapon", armory.NextWeapon, 1);
            Button(paper, font, "Fire", armory.Fire, 2);
            Button(paper, font, "Reload", armory.Reload, 3);
            Toggle(paper, font, "Silencer", armory.Current.Silenced, _ => armory.ToggleSilencer(), 4);
        }
        else
        {
            Label(paper, font, "The host equips it; the whole struct arrives as one value.", 1);
        }

        Button(paper, font, "Show it off to everyone", () => armory.ShowOff(armory.Current), 5);
        Label(paper, font, armory.LastShown, 6);
    }

    private static void HangarControls(Paper paper, FontFile font, Hangar hangar)
    {
        Header(paper, font, "Hangar");
        Button(paper, font, Networking.IsHost ? "Spawn a crate" : "Ask for a crate of my own", hangar.RequestCrate, 1);
        if (Networking.IsHost)
            Button(paper, font, "Clear the crates", hangar.ClearCrates, 2);
    }

    private static string PhysicsStats()
    {
        List<Rigidbody3D> bodies = [.. Scene.Current.FindObjectsOfType<Rigidbody3D>().OfType<Rigidbody3D>().Where(b => b.GameObject.Network.Active)];
        return $"{bodies.Count(b => !b.GameObject.IsProxy)} of {bodies.Count} bodies simulated in this window; the rest follow the windows that control them";
    }

    private static void PhysicsControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Physics");
        Label(paper, font, "Grab a body with the left mouse and throw it.", 1);
        Label(paper, font, "Bodies in your color simulate here.", 2);
        if (Networking.IsHost)
            Button(paper, font, "Restack the pile", RestackPhysics, 3);
    }

    private void StressControls(Paper paper, FontFile font, Swarm swarm)
    {
        Header(paper, font, "Swarm");
        if (!Networking.IsHost)
        {
            Label(paper, font, "The host runs the swarm. Spread, it gives this window drones to drive.", 1);
            return;
        }

        Slider(paper, font, "Drones to spawn", _stressCount, 10f, 1000f, v => _stressCount = (int)v, "0", 1);
        Button(paper, font, $"Spawn {_stressCount}", () => swarm.Spawn(_stressCount), 2);
        Button(paper, font, "Clear", swarm.Clear, 3);
        Toggle(paper, font, "Churn: change a [Sync] value on every drone every frame", swarm.Churn, v => swarm.Churn = v, 4);
        Button(paper, font, "Spread them over every window", swarm.Spread, 5);
        Button(paper, font, "Give them all back to the host", swarm.Gather, 6);
    }

    private static void GuestbookControls(Paper paper, FontFile font, Guestbook book)
    {
        Header(paper, font, "Guestbook");
        for (int i = 0; i < book.Entries.Count; i++)
            Label(paper, font, book.Entries[i], 10 + i);
    }

    public override void DrawSceneControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Session");
        string role = Networking.IsHost ? "Host" : Networking.IsActive ? $"Client, peer #{Networking.Session!.LocalPeer?.Id}" : "Not connected";
        Label(paper, font, $"{role}    {_status}", 1);

        if (Networking.Session is { } session)
        {
            int id = 10;
            foreach (NetPeer peer in session.Peers)
                Label(paper, font, $"{peer}{(peer.IsLocal ? "" : $"    {peer.RoundTripTime.TotalMilliseconds:0} ms")}", id++);
        }

        if (Networking.IsHost)
            Button(paper, font, "Launch a client", LaunchClient, 2);
    }
}
