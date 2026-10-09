// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using static Prowl.Samples.Sample;

namespace VRShowcase;

/// <summary>The weapons, where they start, and the dummies and targets to try them on.</summary>
public sealed partial class VRShowcaseGame
{
    private static readonly Color Steel = new(0.55f, 0.57f, 0.6f, 1f);
    private static readonly Color Gunmetal = new(0.08f, 0.085f, 0.09f, 1f);
    private static readonly Color Polymer = new(0.04f, 0.04f, 0.045f, 1f);
    private static readonly Color Leather = new(0.12f, 0.05f, 0.02f, 1f);
    private static readonly Color Brass = new(0.5f, 0.32f, 0.06f, 1f);
    private static readonly Color Ash = new(0.3f, 0.2f, 0.1f, 1f);
    private static readonly Color Straw = new(0.45f, 0.36f, 0.12f, 1f);
    private static readonly Color Feather = new(0.8f, 0.15f, 0.1f, 1f);

    private void BuildArmory()
    {
        BuildWeaponRack(new Float3(2.6f, 0f, 1.4f));
        BuildKnifeTable(new Float3(2.6f, 0f, -0.6f));
        BuildDummy(new Float3(6f, 0f, -1.5f));
        BuildDummy(new Float3(7.6f, 0f, -0.2f));
        BuildDummy(new Float3(6.2f, 0f, 1.6f));
        BuildTargetBoard(new Float3(4.4f, 0f, -3.2f));
        BuildHayBale(new Float3(6.4f, 0f, -3.6f));
        BuildMelonStand(new Float3(3.2f, 0f, -2.8f));
        BuildRange(new Float3(-5f, 0f, -6f));
    }

    // ----------------------------------------------------------------
    //  Building weapons
    // ----------------------------------------------------------------

    /// <summary>One box piece of a weapon. Metal pieces weigh more than wood, which moves the centre of mass toward them.</summary>
    private readonly record struct Piece(Float3 Size, Float3 Center, Color Color, float Metallic = 0f, bool Collides = true)
    {
        public float Weight => Size.X * Size.Y * Size.Z * (Metallic > 0f ? 7f : 1f);
    }

    private Dictionary<string, Material>? _palette;

    /// <summary>The materials the models' parts are painted with, by the slot each part is named after.</summary>
    private Dictionary<string, Material> Palette => _palette ??= new()
    {
        ["Steel"] = Lit(Steel, 1f, 0.3f),
        ["Gunmetal"] = Lit(Gunmetal, 0.9f, 0.35f),
        ["Polymer"] = Lit(Polymer, 0f, 0.7f),
        ["Leather"] = Lit(Leather, 0f, 0.75f),
        ["Brass"] = Lit(Brass, 0.9f, 0.3f),
        ["Ash"] = Lit(Ash, 0f, 0.7f),
        ["Feather"] = Lit(Feather, 0f, 0.8f),
        ["Straw"] = Lit(Straw, 0f, 0.95f),
        ["Burlap"] = Lit(Straw, 0f, 0.9f),
        ["Rope"] = Lit(new Color(0.4f, 0.3f, 0.15f, 1f), 0f, 0.9f),
        ["White"] = Lit(new Color(0.9f, 0.9f, 0.88f, 1f), 0f, 0.6f),
        ["Paint"] = Lit(new Color(0.6f, 0.1f, 0.1f, 1f), 0.6f, 0.35f),
        ["Wood"] = _wood,
        ["Dark"] = _dark,
        ["Accent"] = _accent,
    };

    /// <summary>
    /// Puts the model from Assets/Models under <paramref name="parent"/> at <paramref name="local"/>, each of its parts
    /// in the palette's material for its slot, or in <paramref name="paint"/>'s where that names the slot.
    /// </summary>
    private GameObject Dress(GameObject parent, string model, Float3 local, Dictionary<string, Material>? paint = null, Float3? scale = null)
    {
        GameObject go = GameObject.InstantiateDetached(Load<PrefabAsset>("Models/" + model))!;
        go.SetParent(parent, false);
        go.Transform.LocalPosition = local;
        go.Transform.LocalRotation = Quaternion.Identity;
        if (scale.HasValue) go.Transform.LocalScale = scale.Value;
        foreach (MeshRenderer renderer in go.GetComponentsInChildren<MeshRenderer>())
        {
            string slot = renderer.GameObject.Name;
            if (paint != null && paint.TryGetValue(slot, out Material? chosen)) renderer.Material = chosen;
            else if (Palette.TryGetValue(slot, out Material? material)) renderer.Material = material;
        }
        return go;
    }

    /// <summary>A model standing in the world on its own, with nothing to collide.</summary>
    private GameObject Prop(string model, Float3 position, Quaternion rotation)
    {
        var go = new GameObject(model);
        go.Transform.Position = position;
        go.Transform.Rotation = rotation;
        Dress(go, model, Float3.Zero);
        Add(go);
        return go;
    }

    /// <summary>Keeps something's collider but stops drawing its box, for a model drawn over it instead.</summary>
    private static GameObject Hidden(GameObject go)
    {
        MeshRenderer? renderer = go.GetComponent<MeshRenderer>();
        if (renderer.IsValid()) renderer.Enabled = false;
        return go;
    }

    /// <summary>
    /// Builds a weapon as one rigidbody of box pieces, laid out in its own space with <paramref name="position"/> as that
    /// space's origin. A body spins about its own origin, so the pieces are moved to put the origin at their centre of
    /// mass, and <paramref name="shift"/> is how far, for anything else placed in the weapon's space afterwards.
    /// </summary>
    private Grabbable Weapon(string name, Float3 position, Quaternion rotation, float mass, SocketSize size, Float3 socketPoint, Piece[] pieces, GrabPoint[] grips, out Float3 shift)
    {
        Float3 weighted = Float3.Zero;
        float total = 0f;
        foreach (Piece piece in pieces)
        {
            weighted += piece.Center * piece.Weight;
            total += piece.Weight;
        }
        shift = weighted / total;

        var go = new GameObject(name);
        go.Transform.Position = position + rotation * shift;
        go.Transform.Rotation = rotation;
        foreach (Piece piece in pieces)
        {
            var part = new GameObject("Part");
            part.SetParent(go);
            part.Transform.LocalPosition = piece.Center - shift;
            part.Transform.LocalRotation = Quaternion.Identity;
            if (piece.Collides) part.AddComponent<BoxCollider>().Size = piece.Size;
        }

        // The model is laid out in the same space as the pieces, so it sits over them shifted the same way.
        Dress(go, name, -shift);

        var body = go.AddComponent<Rigidbody3D>();
        body.Mass = mass;
        body.EnableSpeculativeContacts = true;

        var grabbable = go.AddComponent<Grabbable>();
        foreach (GrabPoint grip in grips)
        {
            GrabPoint shifted = grip.Kind == GrabKind.Point
                ? GrabPoint.At(grip.Position - shift, grip.Rotation)
                : GrabPoint.Line(grip.LineStart - shift, grip.LineEnd - shift);
            shifted.Support = grip.Support;
            grabbable.Points.Add(shifted);
        }
        grabbable.Size = size;
        grabbable.SocketPosition = socketPoint - shift;
        go.AddComponent<MeleeWeapon>();
        Add(go);
        return grabbable;
    }

    private static Stabber Blade(Grabbable weapon, Float3 tip, float length, float sharpness)
    {
        var stabber = weapon.AddComponent<Stabber>();
        stabber.Tip = tip;
        stabber.Axis = Float3.UnitY;
        stabber.Length = length;
        stabber.Sharpness = sharpness;
        return stabber;
    }

    /// <summary>A visual part that moves on its own, such as a slide, parented to a weapon built with <paramref name="shift"/>.</summary>
    private GameObject MovingPart(Grabbable weapon, Float3 shift, Float3 center, string model)
    {
        var part = new GameObject("Moving Part");
        part.SetParent(weapon.GameObject);
        part.Transform.LocalPosition = center - shift;
        part.Transform.LocalRotation = Quaternion.Identity;
        Dress(part, model, Float3.Zero);
        return part;
    }

    // ----------------------------------------------------------------
    //  Melee weapons
    // ----------------------------------------------------------------

    private Grabbable Sword(Float3 position, Quaternion rotation)
    {
        Grabbable sword = Weapon("Sword", position, rotation, 1.3f, SocketSize.Large, new Float3(0f, 0.11f, 0f),
        [
            new(new Float3(0.034f, 0.2f, 0.034f), new Float3(0f, 0.1f, 0f), Leather),
            new(new Float3(0.05f, 0.04f, 0.05f), new Float3(0f, -0.01f, 0f), Brass, 0.9f),
            new(new Float3(0.2f, 0.025f, 0.04f), new Float3(0f, 0.215f, 0f), Brass, 0.9f),
            new(new Float3(0.05f, 0.72f, 0.008f), new Float3(0f, 0.59f, 0f), Steel, 1f),
            new(new Float3(0.025f, 0.06f, 0.008f), new Float3(0f, 0.97f, 0f), Steel, 1f),
        ],
        // The hilt is a line, so a second hand can take it too and either can slide.
        [GrabPoint.Line(new Float3(0f, 0.03f, 0f), new Float3(0f, 0.19f, 0f))], out Float3 shift);
        Blade(sword, new Float3(0f, 1f, 0f) - shift, 0.6f, 0.4f);
        return sword;
    }

    private Grabbable Knife(Float3 position, Quaternion rotation)
    {
        Grabbable knife = Weapon("Knife", position, rotation, 0.3f, SocketSize.Small, new Float3(0f, 0.05f, 0f),
        [
            new(new Float3(0.028f, 0.1f, 0.022f), new Float3(0f, 0.05f, 0f), Leather),
            new(new Float3(0.06f, 0.012f, 0.03f), new Float3(0f, 0.106f, 0f), Brass, 0.9f),
            new(new Float3(0.026f, 0.17f, 0.005f), new Float3(0f, 0.197f, 0f), Steel, 1f),
        ],
        [GrabPoint.At(new Float3(0f, 0.05f, 0f))], out Float3 shift);
        Blade(knife, new Float3(0f, 0.29f, 0f) - shift, 0.16f, 0.6f);
        return knife;
    }

    private Grabbable Spear(Float3 position, Quaternion rotation)
    {
        Grabbable spear = Weapon("Spear", position, rotation, 2.2f, SocketSize.Large, new Float3(0f, 1f, 0f),
        [
            new(new Float3(0.032f, 2f, 0.032f), new Float3(0f, 1f, 0f), Ash),
            new(new Float3(0.045f, 0.06f, 0.045f), new Float3(0f, 2f, 0f), Brass, 0.9f),
            new(new Float3(0.06f, 0.22f, 0.01f), new Float3(0f, 2.14f, 0f), Steel, 1f),
        ],
        [GrabPoint.Line(new Float3(0f, 0.08f, 0f), new Float3(0f, 1.9f, 0f))], out Float3 shift);
        Blade(spear, new Float3(0f, 2.26f, 0f) - shift, 0.2f, 0.5f);
        spear.GripPivot = 0.8f;
        return spear;
    }

    private Grabbable Axe(Float3 position, Quaternion rotation)
        => Weapon("Axe", position, rotation, 1.8f, SocketSize.Large, new Float3(0f, 0.3f, 0f),
        [
            new(new Float3(0.038f, 0.75f, 0.038f), new Float3(0f, 0.375f, 0f), Ash),
            new(new Float3(0.14f, 0.12f, 0.035f), new Float3(0.06f, 0.68f, 0f), Steel, 1f),
            new(new Float3(0.03f, 0.18f, 0.012f), new Float3(0.14f, 0.68f, 0f), Steel, 1f),
        ],
        [GrabPoint.Line(new Float3(0f, 0.05f, 0f), new Float3(0f, 0.6f, 0f))], out _);

    private Grabbable Bat(Float3 position, Quaternion rotation)
        => Weapon("Bat", position, rotation, 0.9f, SocketSize.Large, new Float3(0f, 0.15f, 0f),
        [
            new(new Float3(0.03f, 0.36f, 0.03f), new Float3(0f, 0.18f, 0f), Ash),
            new(new Float3(0.06f, 0.48f, 0.06f), new Float3(0f, 0.6f, 0f), Ash),
        ],
        [GrabPoint.Line(new Float3(0f, 0.03f, 0f), new Float3(0f, 0.33f, 0f))], out _);

    /// <summary>A heavy hammer, held hard enough to lever the whole body up by pressing it into the ground.</summary>
    private Grabbable Sledgehammer(Float3 position, Quaternion rotation)
    {
        Grabbable hammer = Weapon("Sledgehammer", position, rotation, 3.5f, SocketSize.Large, new Float3(0f, 0.35f, 0f),
        [
            new(new Float3(0.036f, 0.8f, 0.036f), new Float3(0f, 0.4f, 0f), Ash),
            new(new Float3(0.09f, 0.09f, 0.24f), new Float3(0f, 0.82f, 0f), Gunmetal, 1f),
        ],
        [GrabPoint.Line(new Float3(0f, 0.05f, 0f), new Float3(0f, 0.7f, 0f))], out _);
        hammer.CarryDrive = new HandDrive(6f, 1.8f, 900f, 24f, 7f, 115f);
        hammer.SharedDrive = new HandDrive(6f, 1.8f, 900f, 14f, 3f, 45f);
        hammer.GripPivot = 0.9f;
        return hammer;
    }

    private Grabbable Staff(Float3 position, Quaternion rotation)
    {
        Grabbable staff = Weapon("Staff", position, rotation, 1.2f, SocketSize.Large, new Float3(0f, 0.8f, 0f),
        [new(new Float3(0.034f, 1.6f, 0.034f), new Float3(0f, 0.8f, 0f), Ash)],
        [GrabPoint.Line(new Float3(0f, 0.05f, 0f), new Float3(0f, 1.55f, 0f))], out _);
        staff.GripPivot = 0.75f;
        return staff;
    }

    // ----------------------------------------------------------------
    //  Guns and the bow
    // ----------------------------------------------------------------

    /// <summary>A pistol along +Z, held by its grip, with a slide to rack and a fifteen round magazine.</summary>
    private Gun Pistol(Float3 position, Quaternion rotation)
    {
        Grabbable pistol = Weapon("Pistol", position, rotation, 0.9f, SocketSize.Medium, new Float3(0f, -0.02f, 0f),
        [
            new(new Float3(0.028f, 0.1f, 0.042f), new Float3(0f, -0.02f, 0f), Polymer),
            new(new Float3(0.03f, 0.026f, 0.17f), new Float3(0f, 0.04f, 0.055f), Polymer),
            new(new Float3(0.006f, 0.02f, 0.04f), new Float3(0f, 0.014f, 0.045f), Polymer),
            new(new Float3(0.032f, 0.028f, 0.17f), new Float3(0f, 0.066f, 0.055f), Gunmetal, 0.9f),
        ],
        // The grip, and a second hold below the front of it for a supporting hand to cup.
        [GrabPoint.At(new Float3(0f, -0.02f, 0f)), GrabPoint.At(new Float3(0f, -0.045f, 0.035f)).AsSupport()], out Float3 shift);

        GameObject slide = MovingPart(pistol, shift, new Float3(0f, 0.066f, 0.055f), "Pistol Slide");
        return Arm(pistol, shift, slide, magazineTop: new Float3(0f, 0.02f, 0f), depth: 0.085f, muzzle: new Float3(0f, 0.066f, 0.145f),
            ejectPort: new Float3(0.02f, 0.07f, 0.06f), handle: new Float3(0f, 0.066f, -0.02f), travel: 0.035f, reciprocates: true, automatic: false);
    }

    /// <summary>A compact automatic along +Z with a front grip for the second hand and a charging handle at the back.</summary>
    private Gun Smg(Float3 position, Quaternion rotation)
    {
        Grabbable smg = Weapon("SMG", position, rotation, 2.6f, SocketSize.Large, new Float3(0f, -0.03f, 0f),
        [
            new(new Float3(0.03f, 0.1f, 0.045f), new Float3(0f, -0.03f, 0f), Polymer),
            new(new Float3(0.042f, 0.06f, 0.32f), new Float3(0f, 0.045f, 0.08f), Gunmetal, 0.9f),
            new(new Float3(0.03f, 0.09f, 0.032f), new Float3(0f, -0.02f, 0.2f), Polymer),
            new(new Float3(0.03f, 0.06f, 0.16f), new Float3(0f, 0.035f, -0.16f), Polymer),
            new(new Float3(0.02f, 0.02f, 0.08f), new Float3(0f, 0.055f, 0.28f), Gunmetal, 0.9f),
        ],
        [GrabPoint.At(new Float3(0f, -0.03f, 0f)), GrabPoint.Line(new Float3(0f, 0.01f, 0.2f), new Float3(0f, -0.055f, 0.2f)).AsSupport()], out Float3 shift);

        GameObject handle = MovingPart(smg, shift, new Float3(0.03f, 0.065f, -0.02f), "SMG Handle");
        Gun gun = Arm(smg, shift, handle, magazineTop: new Float3(0f, 0.015f, 0.09f), depth: 0.07f, muzzle: new Float3(0f, 0.055f, 0.32f),
            ejectPort: new Float3(0.025f, 0.06f, 0.07f), handle: new Float3(0.03f, 0.065f, -0.02f), travel: 0.06f, reciprocates: false, automatic: true);
        gun.FireInterval = 0.08f;
        gun.RecoilBack = 0.6f;
        gun.RecoilUp = 0.22f;
        gun.HitImpulse = 3f;
        gun.KickBack = 0.012f;
        gun.KickPitch = 5f;
        return gun;
    }

    /// <summary>Turns a weapon into a gun: its magazine well, its slide or charging handle and where shots and casings come out.</summary>
    private static Gun Arm(Grabbable weapon, Float3 shift, GameObject slideVisual, Float3 magazineTop, float depth, Float3 muzzle, Float3 ejectPort, Float3 handle, float travel, bool reciprocates, bool automatic)
    {
        var well = new GameObject("Magazine Well");
        well.SetParent(weapon.GameObject);
        well.Transform.LocalPosition = magazineTop - shift;
        well.Transform.LocalRotation = Quaternion.Identity;
        var socket = well.AddComponent<MagazineWell>();
        socket.Depth = depth;

        var gun = weapon.AddComponent<Gun>();
        gun.Muzzle = muzzle - shift;
        gun.EjectPort = ejectPort - shift;
        gun.Automatic = automatic;
        gun.MagazineWell = socket;

        var slide = weapon.AddComponent<GunSlide>();
        slide.Gun = gun;
        slide.Visual = slideVisual.Transform;
        slide.Handle = handle - shift;
        slide.Travel = travel;
        slide.Reciprocates = reciprocates;
        gun.Slide = slide;
        return gun;
    }

    private Grabbable MagazineFor(string name, Float3 position, Quaternion rotation, Float3 size, int rounds)
    {
        Grabbable magazine = Weapon(name, position, rotation, 0.25f, SocketSize.Magazine, new Float3(0f, size.Y * 0.5f, 0f),
        [
            new(size, Float3.Zero, Gunmetal, 0.9f),
            new(new Float3(size.X * 0.8f, 0.006f, size.Z * 0.7f), new Float3(0f, size.Y * 0.5f + 0.003f, 0f), Brass, 0.9f, Collides: false),
        ],
        [GrabPoint.At(new Float3(0f, -size.Y * 0.2f, 0f))], out _);
        var mag = magazine.AddComponent<Magazine>();
        mag.Capacity = mag.Rounds = rounds;
        return magazine;
    }

    private Grabbable PistolMagazine(Float3 position, Quaternion rotation) => MagazineFor("Pistol Magazine", position, rotation, new Float3(0.022f, 0.09f, 0.034f), 15);
    private Grabbable SmgMagazine(Float3 position, Quaternion rotation) => MagazineFor("SMG Magazine", position, rotation, new Float3(0.024f, 0.15f, 0.04f), 30);

    /// <summary>Puts a magazine in a gun and a round in its chamber, ready to fire.</summary>
    private static void Load(Gun gun, Grabbable magazine)
    {
        gun.MagazineWell.Load(magazine);
        gun.ChamberFromMagazine();
    }

    /// <summary>A bow along +Y shooting along +Z, held at the middle of its riser.</summary>
    private Bow BuildBow(Float3 position, Quaternion rotation)
    {
        Grabbable bow = Weapon("Bow", position, rotation, 1.1f, SocketSize.Large, Float3.Zero,
        [
            new(new Float3(0.03f, 0.16f, 0.04f), Float3.Zero, Leather),
            new(new Float3(0.026f, 0.26f, 0.03f), new Float3(0f, 0.2f, -0.02f), Ash),
            new(new Float3(0.022f, 0.24f, 0.022f), new Float3(0f, 0.44f, -0.06f), Ash),
            new(new Float3(0.026f, 0.26f, 0.03f), new Float3(0f, -0.2f, -0.02f), Ash),
            new(new Float3(0.022f, 0.24f, 0.022f), new Float3(0f, -0.44f, -0.06f), Ash),
        ],
        [GrabPoint.At(Float3.Zero)], out Float3 shift);

        var b = bow.AddComponent<Bow>();
        b.TopTip = new Float3(0f, 0.56f, -0.08f) - shift;
        b.BottomTip = new Float3(0f, -0.56f, -0.08f) - shift;
        b.NockRest = new Float3(0f, 0.02f, -0.08f) - shift;
        b.ArrowRest = new Float3(0f, 0.05f, 0.02f) - shift;
        b.MakeArrow = () => BuildArrow(position, rotation);
        return b;
    }

    private Arrow BuildArrow(Float3 position, Quaternion rotation)
    {
        Grabbable arrow = Weapon("Arrow", position, rotation, 0.08f, SocketSize.Small, new Float3(0f, 0.1f, 0f),
        [
            new(new Float3(0.009f, 0.7f, 0.009f), new Float3(0f, 0.35f, 0f), Ash),
            new(new Float3(0.018f, 0.05f, 0.004f), new Float3(0f, 0.72f, 0f), Steel, 1f),
            new(new Float3(0.03f, 0.08f, 0.002f), new Float3(0f, 0.06f, 0f), Feather, 0f, Collides: false),
        ],
        [GrabPoint.Line(new Float3(0f, 0.05f, 0f), new Float3(0f, 0.6f, 0f))], out Float3 shift);
        arrow.Reach = 0.05f;

        var a = arrow.AddComponent<Arrow>();
        a.Notch = -shift;
        a.Tip = new Float3(0f, 0.745f, 0f) - shift;
        Blade(arrow, a.Tip, 0.12f, 0.8f);
        arrow.AddComponent<Expire>().Seconds = 90f;
        return a;
    }

    // ----------------------------------------------------------------
    //  Where the weapons start
    // ----------------------------------------------------------------

    /// <summary>A wall rack facing the spawn, each long weapon standing in a socket of its own.</summary>
    private void BuildWeaponRack(Float3 at)
    {
        Quaternion facing = Quaternion.FromEuler(0f, -90f, 0f);
        Add(Block("Rack Back", new Float3(0.1f, 2.4f, 3.2f), _wood, at + new Float3(0.15f, 1.2f, 0f)));
        Add(Block("Rack Foot", new Float3(0.5f, 0.08f, 3.2f), _wood, at + new Float3(0f, 0.04f, 0f)));

        Func<Float3, Quaternion, Grabbable>[] weapons = [Sword, Spear, Axe, Bat, Staff, Sledgehammer];
        for (int i = 0; i < weapons.Length; i++)
        {
            Float3 slot = at + new Float3(0f, 1.2f, -1.25f + i * 0.5f);
            Socket socket = RackSocket(slot, facing, SocketSize.Any, 0.2f);
            socket.Insert(weapons[i](slot, facing));
        }
    }

    private Socket RackSocket(Float3 position, Quaternion rotation, SocketSize accepts, float radius)
    {
        var go = new GameObject("Rack Socket");
        go.Transform.Position = position;
        go.Transform.Rotation = rotation;
        var socket = go.AddComponent<Socket>();
        socket.Accepts = accepts;
        socket.Radius = radius;
        Add(go);
        return socket;
    }

    /// <summary>A small table of knives, each lying in a socket, to pick up or throw.</summary>
    private void BuildKnifeTable(Float3 at)
    {
        Add(Block("Knife Table", new Float3(0.6f, 0.9f, 0.9f), _wood, at + new Float3(0f, 0.45f, 0f)));
        Quaternion lying = Quaternion.FromEuler(90f, 0f, 0f);
        for (int i = 0; i < 3; i++)
        {
            Float3 slot = at + new Float3(-0.15f + i * 0.15f, 0.93f, -0.05f);
            RackSocket(slot, lying, SocketSize.Small, 0.1f).Insert(Knife(slot, lying));
        }
    }

    /// <summary>
    /// A shooting range: a bench with the pistol, the automatic, spare magazines and the bow, facing hanging steel plates,
    /// cans on a shelf and bales to shoot arrows into.
    /// </summary>
    private void BuildRange(Float3 at)
    {
        // The bench runs along Z, the shooter stands on its +X side and faces down range toward -X.
        Add(Block("Range Bench", new Float3(0.7f, 0.9f, 2.6f), _wood, at + new Float3(0f, 0.45f, 0f)));
        Add(Block("Range Floor Line", new Float3(0.04f, 0.01f, 6f), _accent, at + new Float3(-0.45f, 0.005f, 0f)));
        Quaternion downRange = Quaternion.FromEuler(0f, -90f, 0f);
        Quaternion lying = downRange * Quaternion.FromEuler(0f, 0f, 90f);
        float top = 0.9f;

        Float3 pistolAt = at + new Float3(0.05f, top + 0.03f, -0.9f);
        Gun pistol = Pistol(pistolAt, lying);
        RackSocket(pistolAt, lying, SocketSize.Medium, 0.12f).Insert(pistol.GetComponent<Grabbable>()!);
        Load(pistol, PistolMagazine(pistolAt, lying));

        Float3 smgAt = at + new Float3(0.05f, top + 0.04f, -0.25f);
        Gun smg = Smg(smgAt, lying);
        RackSocket(smgAt, lying, SocketSize.Large, 0.15f).Insert(smg.GetComponent<Grabbable>()!);
        Load(smg, SmgMagazine(smgAt, lying));

        // Spare magazines standing upright in a row.
        for (int i = 0; i < 4; i++)
        {
            Float3 slot = at + new Float3(0.2f, top + 0.05f, 0.25f + i * 0.08f);
            RackSocket(slot, Quaternion.Identity, SocketSize.Magazine, 0.06f).Insert(PistolMagazine(slot, Quaternion.Identity));
        }
        for (int i = 0; i < 3; i++)
        {
            Float3 slot = at + new Float3(0.05f, top + 0.08f, 0.25f + i * 0.08f);
            RackSocket(slot, Quaternion.Identity, SocketSize.Magazine, 0.06f).Insert(SmgMagazine(slot, Quaternion.Identity));
        }

        Float3 bowAt = at + new Float3(0.1f, top + 0.05f, 0.95f);
        Quaternion bowLying = Quaternion.FromEuler(0f, 0f, 90f);
        Bow bow = BuildBow(bowAt, bowLying);
        RackSocket(bowAt, bowLying, SocketSize.Large, 0.2f).Insert(bow.Grabbable);
        for (int i = 0; i < 4; i++)
            BuildArrow(at + new Float3(-0.2f, top + 0.02f + i * 0.02f, 0.75f), Quaternion.FromEuler(0f, 0f, 90f));

        // Down range: swinging steel plates at a few distances, cans on a shelf, and bales for the arrows.
        for (int i = 0; i < 4; i++)
            BuildSteelPlate(at + new Float3(-6f - i * 3f, 0f, -1.5f + i * 1f), 0.25f + i * 0.05f);
        Add(Block("Can Shelf", new Float3(0.4f, 1.1f, 2f), _wood, at + new Float3(-4.5f, 0.55f, 2f)));
        for (int i = 0; i < 6; i++)
            BuildCan(at + new Float3(-4.5f, 1.1f + 0.06f, 1.25f + i * 0.3f));
        BuildHayBale(at + new Float3(-8f, 0f, 3.2f));
        BuildHayBale(at + new Float3(-12f, 0f, 2.4f));
        BuildTargetBoard(at + new Float3(-10f, 0f, 4.4f), Quaternion.FromEuler(0f, 90f, 0f));
    }

    private void BuildSteelPlate(Float3 at, float size)
    {
        Add(Hidden(Block("Plate Frame", new Float3(0.08f, 2f, 0.08f), _dark, at + new Float3(0f, 1f, -0.4f))));
        Add(Hidden(Block("Plate Frame", new Float3(0.08f, 2f, 0.08f), _dark, at + new Float3(0f, 1f, 0.4f))));
        Add(Hidden(Block("Plate Beam", new Float3(0.08f, 0.08f, 0.88f), _dark, at + new Float3(0f, 2f, 0f))));
        Prop("Plate Stand", at, Quaternion.Identity);

        var plate = new GameObject("Steel Plate");
        plate.Transform.Position = at + new Float3(0f, 1.4f, 0f);
        Float3 shape = new(0.02f, size * 2f, size * 2f);
        plate.AddComponent<BoxCollider>().Size = shape;
        Material material = Lit(new Color(0.75f, 0.75f, 0.72f, 1f), 0.8f, 0.4f);
        Dress(plate, "Steel Plate", Float3.Zero, new() { ["Steel"] = material }, new Float3(1f, size * 2f, size * 2f));
        var body = plate.AddComponent<Rigidbody3D>();
        body.Mass = 8f;
        body.EnableSpeculativeContacts = true;
        body.AngularDamping = 0.05f;
        var hinge = plate.AddComponent<BallSocketConstraint>();
        hinge.Anchor = new Float3(0f, 0.6f, 0f);
        var flash = plate.AddComponent<HitFlash>();
        flash.BaseColor = new Color(0.75f, 0.75f, 0.72f, 1f);
        flash.FlashColor = new Color(1f, 0.85f, 0.3f, 1f);
        flash.Materials.Add(material);
        plate.AddComponent<Stabbable>().Resistance = 3f;
        Add(plate);
        plate.AddComponent<DummyRope>().Anchor = new Float3(0f, size, 0f);
    }

    private void BuildCan(Float3 at)
    {
        Material paint = Lit(Hsv(_rng.NextSingle(), 0.7f, 0.5f), 0.8f, 0.3f);
        Rigidbody3D can = Body("Can", Mesh.CreateCube(new Float3(0.065f, 0.12f, 0.065f)), paint, at, Quaternion.Identity, 0.15f, new Float3(0.065f, 0.12f, 0.065f));
        Hidden(can.GameObject);
        Dress(can.GameObject, "Can", Float3.Zero, new() { ["Paint"] = paint });
        Grab(can, 0.07f);
        can.GameObject.AddComponent<Stabbable>().Resistance = 0.3f;
    }

    // ----------------------------------------------------------------
    //  Things to hit
    // ----------------------------------------------------------------

    /// <summary>A punching bag hanging by a rope from a post's arm, free to swing when hit or stabbed.</summary>
    private void BuildDummy(Float3 at)
    {
        Add(Hidden(Block("Dummy Post", new Float3(0.12f, 2.5f, 0.12f), _dark, at + new Float3(0f, 1.25f, 0f))));
        Add(Hidden(Block("Dummy Arm", new Float3(0.1f, 0.1f, 0.8f), _dark, at + new Float3(0f, 2.45f, 0.4f))));
        Add(Hidden(Block("Dummy Base", new Float3(0.6f, 0.06f, 0.6f), _dark, at + new Float3(0f, 0.03f, 0f))));
        Prop("Dummy Stand", at, Quaternion.Identity);

        var dummy = new GameObject("Dummy");
        dummy.Transform.Position = at + new Float3(0f, 1.15f, 0.75f);
        var body = dummy.AddComponent<Rigidbody3D>();
        body.Mass = 30f;
        body.EnableSpeculativeContacts = true;
        body.AngularDamping = 0.3f;
        body.LinearDamping = 0.05f;

        Material material = Lit(Straw, 0f, 0.9f);
        var bag = dummy.AddComponent<CapsuleCollider>();
        bag.Radius = 0.2f;
        bag.Height = 1f;
        var shape = dummy.AddComponent<MeshRenderer>();
        shape.Mesh = Mesh.CreateCapsule(0.2f, 1f);
        shape.Material = material;

        var rope = dummy.AddComponent<BallSocketConstraint>();
        rope.Anchor = new Float3(0f, 1.2f, 0f);
        dummy.AddComponent<DummyRope>().Anchor = new Float3(0f, 0.5f, 0f);

        dummy.AddComponent<Stabbable>().Resistance = 0.6f;
        var flash = dummy.AddComponent<HitFlash>();
        flash.BaseColor = Straw;
        flash.Materials.Add(material);
        Add(dummy);
    }

    /// <summary>A wooden board on a stand. Knives, spears and arrows that hit point first stick in it.</summary>
    private void BuildTargetBoard(Float3 at, Quaternion? facing = null)
    {
        Quaternion rotation = facing ?? Quaternion.Identity;
        Add(Hidden(Block("Board Stand", new Float3(0.1f, 1.2f, 0.1f), _dark, at + rotation * new Float3(0f, 0.6f, 0.1f))));
        Prop("Board Stand", at + rotation * new Float3(0f, 0f, 0.1f), rotation);
        var board = new GameObject("Target Board");
        board.Transform.Position = at + new Float3(0f, 1.5f, 0f);
        board.Transform.Rotation = rotation;
        board.AddComponent<BoxCollider>().Size = new Float3(1f, 1f, 0.1f);
        Dress(board, "Target Board", Float3.Zero);
        board.AddComponent<Rigidbody3D>().MotionType = Jitter2.Dynamics.MotionType.Static;
        var stabbable = board.AddComponent<Stabbable>();
        stabbable.Resistance = 2.5f;
        stabbable.RequiredSpeed = 2f;
        stabbable.MaxDepth = 0.08f;
        Add(board);
    }

    /// <summary>A heavy bale of straw, easy to sink a blade or an arrow into.</summary>
    private void BuildHayBale(Float3 at)
    {
        var bale = new GameObject("Hay Bale");
        bale.Transform.Position = at + new Float3(0f, 0.4f, 0f);
        Float3 size = new(1.2f, 0.8f, 0.6f);
        bale.AddComponent<BoxCollider>().Size = size;
        Material material = Lit(Straw, 0f, 0.95f);
        Dress(bale, "Hay Bale", Float3.Zero, new() { ["Straw"] = material });
        var baleBody = bale.AddComponent<Rigidbody3D>();
        baleBody.Mass = 60f;
        baleBody.EnableSpeculativeContacts = true;
        bale.AddComponent<Stabbable>().Resistance = 0.4f;
        var flash = bale.AddComponent<HitFlash>();
        flash.BaseColor = Straw;
        flash.Materials.Add(material);
        Add(bale);
    }

    /// <summary>Melons on a stand, to pick up, stab and carry around on a blade.</summary>
    private void BuildMelonStand(Float3 at)
    {
        Add(Block("Melon Stand", new Float3(0.8f, 0.8f, 0.5f), _wood, at + new Float3(0f, 0.4f, 0f)));
        for (int i = 0; i < 3; i++)
        {
            Rigidbody3D melon = Body("Melon", Mesh.CreateSphere(0.11f, 12, 18), Lit(new Color(0.06f, 0.2f, 0.04f, 1f), 0f, 0.4f),
                at + new Float3(-0.25f + i * 0.25f, 0.92f, 0f), Quaternion.Identity, 2f, null);
            melon.GameObject.AddComponent<SphereCollider>().Radius = 0.11f;
            melon.GameObject.AddComponent<Stabbable>().Resistance = 0.3f;
            Grab(melon, 0.11f);
        }
    }
}

/// <summary>Draws the rope something hangs by, from <see cref="Anchor"/> on it up to where it hangs from.</summary>
public sealed class DummyRope : MonoBehaviour
{
    public Float3 Anchor;

    private LineRenderer _line = null!;
    private Float3 _top;

    public override void OnEnable()
    {
        if (_line != null) return;
        var go = new GameObject("Rope");
        go.SetParent(GameObject);
        go.Transform.LocalPosition = Float3.Zero;
        go.Transform.LocalRotation = Quaternion.Identity;
        _line = go.AddComponent<LineRenderer>();
        _line.Material = new Material(Shader.LoadDefault(DefaultShader.Line));
        _line.StartWidth = _line.EndWidth = 0.015f;
        _line.StartColor = _line.EndColor = new Color(0.4f, 0.3f, 0.15f, 1f);
        BallSocketConstraint? hang = GetComponent<BallSocketConstraint>();
        _top = Transform.TransformPoint(hang.IsValid() ? hang.Anchor : Anchor);
    }

    public override void Update()
    {
        // The line's points are in this object's own space, which turns as it swings.
        _line.Points.Clear();
        _line.Points.Add(Anchor);
        _line.Points.Add(Transform.InverseTransformPoint(_top));
    }
}
