// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.ParticleSystem;
using Prowl.Runtime.ParticleSystem.Modules;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Gradient = Prowl.Vector.Gradient;

namespace VehicleShowcase;

/// <summary>
/// Leaves marks on the ground under wheels that slide sideways or lock under braking. The marks share one mesh, a ring of
/// the most recent segments, laid at the bottom of each wheel across the way it travels. Hand it the wheels of
/// every vehicle with <see cref="Track"/>; it needs a MeshRenderer beside it with a transparent material.
/// </summary>
public sealed class Skidmarks : MonoBehaviour
{
    public int MaxSegments = 3000;

    /// <summary>Sliding speed in m/s where a mark starts, and the lower speed it carries on down to.</summary>
    public float StartSlip = 3f;
    public float KeepSlip = 2f;

    private const float SegmentLength = 0.25f;
    private const float Raise = 0.02f;

    private sealed class Trail
    {
        public Rigidbody3D Body = null!;
        public bool Active;
        public Float3 Center, Left, Right;
        public float Along;
    }

    private readonly Dictionary<WheelCollider, Trail> _trails = new();
    private Mesh _mesh = null!;
    private Float3[] _vertices = null!;
    private Float3[] _normals = null!;
    private Float4[] _tangents = null!;
    private Float2[] _uv = null!;
    private uint[] _indices = null!;
    private int _next;
    private bool _dirty;

    public override void OnEnable()
    {
        int vertexCount = MaxSegments * 4;
        _vertices = new Float3[vertexCount];
        _normals = new Float3[vertexCount];
        _tangents = new Float4[vertexCount];
        _uv = new Float2[vertexCount];
        _indices = new uint[MaxSegments * 6];
        for (int i = 0; i < MaxSegments; i++)
        {
            uint v = (uint)i * 4;
            _indices[i * 6 + 0] = v;
            _indices[i * 6 + 1] = v + 2;
            _indices[i * 6 + 2] = v + 1;
            _indices[i * 6 + 3] = v;
            _indices[i * 6 + 4] = v + 3;
            _indices[i * 6 + 5] = v + 2;
        }

        _mesh = new Mesh { Name = "Skidmarks" };
        Upload();
        GetComponent<MeshRenderer>()!.Mesh = _mesh;
    }

    /// <summary>Starts marking under <paramref name="wheels"/>, which all belong to <paramref name="body"/>.</summary>
    public void Track(Rigidbody3D body, IEnumerable<WheelCollider> wheels)
    {
        foreach (WheelCollider wheel in wheels) _trails[wheel] = new Trail { Body = body };
    }

    /// <summary>Ends every trail, so a car put somewhere else does not draw a mark across to it.</summary>
    public void Lift()
    {
        foreach (Trail trail in _trails.Values) trail.Active = false;
    }

    public override void Update()
    {
        foreach ((WheelCollider wheel, Trail trail) in _trails) Mark(wheel, trail);
    }

    public override void LateUpdate()
    {
        if (!_dirty) return;
        _dirty = false;
        Upload();
    }

    private void Mark(WheelCollider wheel, Trail trail)
    {
        if (!wheel.IsGrounded)
        {
            trail.Active = false;
            return;
        }

        // The bottom of the wheel, which is on the ground whenever the wheel really touches it.
        Float3 normal = wheel.ContactNormal;
        Float3 bottom = wheel.GetWheelCenter() - normal * wheel.Radius;
        bool touching = MathF.Abs(Float3.Dot(wheel.ContactPoint - bottom, normal)) < 0.1f;

        // Ground running faster than the tyre is a locked wheel. Wheelspin is left out, it leaves no mark worth showing.
        float slip = MathF.Max(MathF.Abs(wheel.SidewaysSlip), wheel.ForwardSlip);
        bool sliding = slip > (trail.Active ? KeepSlip : StartSlip);
        if (!touching || !sliding)
        {
            trail.Active = false;
            return;
        }

        Float3 travel = trail.Body.GetPointVelocity(bottom);
        travel -= normal * Float3.Dot(travel, normal);
        if (Float3.LengthSquared(travel) < 0.01f)
        {
            trail.Active = false;
            return;
        }

        Float3 side = Float3.Normalize(Float3.Cross(normal, travel)) * (wheel.Width * 0.5f);
        Float3 center = bottom + normal * Raise;
        Float3 left = center - side, right = center + side;

        if (trail.Active)
        {
            float travelled = Float3.Distance(center, trail.Center);
            if (travelled < SegmentLength) return;
            if (travelled < SegmentLength * 8f) AddSegment(trail, left, right, normal, Float3.Normalize(travel), travelled / (wheel.Width * 2f));
        }
        else trail.Along = 0f;

        trail.Active = true;
        trail.Center = center;
        trail.Left = left;
        trail.Right = right;
    }

    private void AddSegment(Trail trail, Float3 left, Float3 right, Float3 normal, Float3 heading, float advance)
    {
        float from = trail.Along;
        trail.Along += advance;
        int v = _next % MaxSegments * 4;
        _next++;

        _vertices[v + 0] = trail.Left;
        _vertices[v + 1] = trail.Right;
        _vertices[v + 2] = right;
        _vertices[v + 3] = left;
        _uv[v + 0] = new Float2(0f, from);
        _uv[v + 1] = new Float2(1f, from);
        _uv[v + 2] = new Float2(1f, trail.Along);
        _uv[v + 3] = new Float2(0f, trail.Along);
        for (int i = 0; i < 4; i++)
        {
            _normals[v + i] = normal;
            _tangents[v + i] = new Float4(heading, 1f);
        }
        _dirty = true;
    }

    private void Upload()
    {
        _mesh.Vertices = _vertices;
        _mesh.Normals = _normals;
        _mesh.Tangents = _tangents;
        _mesh.UV = _uv;
        _mesh.Indices = _indices;
        _mesh.RecalculateBounds();
    }
}

/// <summary>What a surface throws up under a slipping tyre.</summary>
public enum TyreSprayKind { Smoke, Dust, Chips, None }

/// <summary>Marks a surface for <see cref="TyreSpray"/>. Surfaces without one smoke.</summary>
public sealed class TyreSurface : MonoBehaviour
{
    public TyreSprayKind Spray = TyreSprayKind.Dust;
}

/// <summary>
/// Throws something up from wheels that slide, lock or spin in place, more the faster the tyre slips over the ground:
/// smoke off hard ground, dust off dirt, which also trails behind wheels rolling fast over it, and chips off ice.
/// The ground picks which with a <see cref="TyreSurface"/>. Hand it the wheels of every vehicle with
/// <see cref="Track"/>; it sets up its own world space particle systems.
/// </summary>
public sealed class TyreSpray : MonoBehaviour
{
    public Texture2D? SmokeTexture;
    public Texture2D? ChipTexture;

    /// <summary>Slip speed in m/s where each kind starts.</summary>
    public float SmokeSlip = 4f;
    public float DustSlip = 1.5f;
    public float ChipSlip = 2f;

    /// <summary>The most puffs, clouds or chips one wheel throws each second.</summary>
    public float MaxRate = 90f;

    private readonly Dictionary<WheelCollider, float> _wheels = new();
    private readonly Random _random = new();
    private ParticleSystemComponent _smoke = null!, _dust = null!, _chips = null!;

    public void Track(IEnumerable<WheelCollider> wheels)
    {
        foreach (WheelCollider wheel in wheels) _wheels[wheel] = 0f;
    }

    public override void OnEnable()
    {
        _smoke = Cloud(new Color(0.8f, 0.8f, 0.82f, 1f), 0.4f, 0.5f, 4f, 0.5f);
        _smoke.Initial.StartLifetime = new MinMaxCurve(1.8f, 3f);

        _dust = Cloud(new Color(0.55f, 0.43f, 0.3f, 1f), 0.5f, 0.8f, 5f, 0.2f);
        _dust.Initial.StartLifetime = new MinMaxCurve(2.5f, 4f);

        _chips = System(ChipTexture, 1000);
        _chips.Initial.StartLifetime = new MinMaxCurve(0.7f, 1.3f);
        _chips.Initial.StartSize = new MinMaxCurve(0.1f, 0.22f);
        _chips.Initial.StartColor = new MinMaxGradient(new Color(0.45f, 0.7f, 0.95f, 1f));
        _chips.Initial.GravityModifier = 1f;
        _chips.ColorOverLifetime.Enabled = true;
        _chips.ColorOverLifetime.Color = new MinMaxGradient(Fade(0.05f, 1f, 0.7f));
        _chips.Collision.Enabled = true;
        _chips.Collision.Type = ParticleCollisionType.World;
        _chips.Collision.Bounce = 0.3f;
        _chips.Collision.Dampen = 0.4f;
        _chips.Collision.Quality = ParticleCollisionQuality.Medium;
    }

    // A soft, lit cloud that grows and fades as it drifts up and slows.
    private ParticleSystemComponent Cloud(Color color, float opacity, float size, float growth, float rise)
    {
        ParticleSystemComponent cloud = System(SmokeTexture, 1500);
        cloud.Initial.StartSize = new MinMaxCurve(size, size * 1.8f);
        cloud.Initial.StartRotation = new MinMaxCurve(0f, 360f);
        cloud.Initial.StartColor = new MinMaxGradient(color);
        cloud.SizeOverLifetime.Enabled = true;
        cloud.SizeOverLifetime.Size = new MinMaxCurve(new AnimationCurve([new Keyframe(0f, 1f), new Keyframe(1f, growth)]));
        cloud.RotationOverLifetime.Enabled = true;
        cloud.RotationOverLifetime.Z = new MinMaxCurve(-30f, 30f);
        cloud.ColorOverLifetime.Enabled = true;
        cloud.ColorOverLifetime.Color = new MinMaxGradient(Fade(0.1f, opacity, 0f));
        cloud.VelocityOverLifetime.Enabled = true;
        cloud.VelocityOverLifetime.Space = ParticleSpace.World;
        cloud.VelocityOverLifetime.Y = new MinMaxCurve(rise);
        cloud.Wind.Enabled = true;
        cloud.Wind.Drag = 1.5f;
        cloud.Wind.Turbulence = 0.4f;
        cloud.TextureSheet.Enabled = true;
        cloud.TextureSheet.TilesX = 4;
        cloud.TextureSheet.TilesY = 4;
        cloud.TextureSheet.FrameBlending = true;
        cloud.Renderer.SoftParticleDistance = 0.5f;
        return cloud;
    }

    private ParticleSystemComponent System(Texture2D? texture, int maxParticles)
    {
        var system = GameObject.AddComponent<ParticleSystemComponent>();
        system.SimulationSpace = SimulationSpace.World;
        system.MaxParticles = maxParticles;
        system.Emission.RateOverTime = new MinMaxCurve(0f);
        system.Renderer.Lit = true;
        if (texture.IsValid())
        {
            var material = new Material(Shader.LoadDefault(DefaultShader.Particle));
            material.SetTexture("_MainTex", texture!);
            material.SetColor("_MainColor", Color.White);
            system.Renderer.Material = material;
        }
        return system;
    }

    // White, rising to full opacity by the given time, then fading to the end opacity.
    private static Gradient Fade(float peakTime, float peak, float end) => new(
        [new GradientColorKey(0f, Color.White), new GradientColorKey(1f, Color.White)],
        [new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peakTime, peak), new GradientAlphaKey(1f, end)]);

    public override void Update()
    {
        float dt = Time.DeltaTime;
        foreach (WheelCollider wheel in _wheels.Keys)
        {
            if (!wheel.EnabledInHierarchy || !wheel.GetGroundHit(out WheelHit hit))
            {
                _wheels[wheel] = 0f;
                continue;
            }

            TyreSurface? surface = hit.GameObject.IsValid() ? hit.GameObject!.GetComponent<TyreSurface>() : null;
            TyreSprayKind kind = surface.IsValid() ? surface!.Spray : TyreSprayKind.Smoke;
            float slip = MathF.Max(MathF.Abs(wheel.SidewaysSlip), MathF.Abs(wheel.ForwardSlip));
            float rolling = MathF.Abs(wheel.Rpm) * MathF.PI / 30f * wheel.Radius;
            float rate = kind switch
            {
                TyreSprayKind.Smoke => (slip - SmokeSlip) * 6f,
                TyreSprayKind.Dust => (slip - DustSlip) * 6f + rolling * 0.3f,
                TyreSprayKind.Chips => (slip - ChipSlip) * 30f,
                _ => 0f,
            };
            if (rate <= 0f)
            {
                _wheels[wheel] = 0f;
                continue;
            }

            float due = _wheels[wheel] + MathF.Min(MaxRate, rate) * dt;
            int count = (int)due;
            _wheels[wheel] = due - count;
            for (int i = 0; i < count; i++) Throw(kind, wheel, hit);
        }
    }

    private void Throw(TyreSprayKind kind, WheelCollider wheel, in WheelHit hit)
    {
        Float3 at = hit.Point + hit.SidewaysDir * Range(-0.5f, 0.5f) * wheel.Width;
        if (kind == TyreSprayKind.Chips)
        {
            // Thrown the way the tyre scrubs over the ice.
            Float3 scrub = Float3.NormalizeSafe(hit.ForwardDir * wheel.ForwardSlip + hit.SidewaysDir * wheel.SidewaysSlip, Float3.Zero);
            Float3 throwOut = scrub * Range(2f, 5f) + hit.Normal * Range(1.5f, 3.5f) + new Float3(Range(-0.8f, 0.8f), 0f, Range(-0.8f, 0.8f));
            _chips.Emit(new EmitParams { Position = at + hit.Normal * 0.05f, Velocity = hit.GroundVelocity + throwOut }, 1);

            // Now and then a little frost mist with them.
            if (_random.NextSingle() < 0.15f)
                _smoke.Emit(new EmitParams { Position = at + hit.Normal * 0.15f, Velocity = hit.GroundVelocity + scrub * 1.5f + hit.Normal * 0.5f, StartColor = new Color(0.85f, 0.93f, 1f, 1f) }, 1);
            return;
        }

        Float3 spread = new(Range(-0.6f, 0.6f), Range(0.2f, 0.8f), Range(-0.6f, 0.6f));
        ParticleSystemComponent cloud = kind == TyreSprayKind.Dust ? _dust : _smoke;
        cloud.Emit(new EmitParams { Position = at + hit.Normal * 0.15f, Velocity = hit.GroundVelocity + spread }, 1);
    }

    private float Range(float min, float max) => min + _random.NextSingle() * (max - min);
}
