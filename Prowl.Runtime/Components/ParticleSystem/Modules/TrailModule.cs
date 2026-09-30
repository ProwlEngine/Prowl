// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Rendering;
using Prowl.Vector;

namespace Prowl.Runtime.ParticleSystem.Modules;

public enum TrailTextureMode
{
    /// <summary>The texture spans the whole trail once.</summary>
    Stretch,
    /// <summary>The texture repeats every <see cref="TrailModule.TileLength"/> world units.</summary>
    Tile
}

/// <summary>
/// Leaves a ribbon behind each particle. Drawn with <see cref="RendererModule.TrailMaterial"/>, or the
/// particle material when that is empty.
/// </summary>
[Serializable]
public class TrailModule : ParticleSystemModule
{
    [Range(0f, 1f), Tooltip("Share of particles that get a trail.")]
    public float Ratio = 1f;

    [Tooltip("How long a point stays, as a share of the particle's lifetime.")]
    public MinMaxCurve Lifetime = new(1f);

    [Tooltip("Distance the particle moves before a new point is added.")]
    public float MinVertexDistance = 0.2f;

    [Tooltip("Most points one trail keeps. The oldest point goes first.")]
    public int MaxPoints = 32;

    [Tooltip("Record points in world space, so the trail stays behind when a local space system moves.")]
    public bool WorldSpace = false;

    [Tooltip("Remove the trail the moment its particle dies, instead of letting it fade out.")]
    public bool DieWithParticles = true;

    public TrailTextureMode TextureMode = TrailTextureMode.Stretch;
    [ShowIf(nameof(IsTile))]
    public float TileLength = 1f;

    [Tooltip("Width follows the particle's size.")]
    public bool SizeAffectsWidth = true;

    [Tooltip("Width along the trail, 0 at the particle and 1 at the tail.")]
    public AnimationCurve WidthOverTrail = new(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
    public float WidthMultiplier = 1f;

    [Tooltip("Tint the trail with the particle's color.")]
    public bool InheritParticleColor = true;
    [Tooltip("Tint over the particle's life.")]
    public MinMaxGradient ColorOverLifetime = new(Color.White);
    [Tooltip("Tint along the trail, 0 at the particle and 1 at the tail.")]
    public MinMaxGradient ColorOverTrail = new(Color.White);

    private bool IsTile => TextureMode == TrailTextureMode.Tile;

    private struct TrailPoint
    {
        public Float3 Position;
        public float Time;
    }

    private struct TrailState
    {
        public int Start;
        public int Count;
        public float Life;
        public bool Orphan;
        public bool InWorld;
        public Color Color;
        public float Width;
        public float ColorRandom;
    }

    private TrailPoint[] _points = Array.Empty<TrailPoint>();
    private TrailState[] _states = Array.Empty<TrailState>();
    private readonly Stack<int> _free = new();
    private readonly List<int> _orphans = new();
    private int _capacity;
    private int _slots;

    // Scratch for one trail's points, oldest first, while its segments are built.
    private Float3[] _scratchPos = Array.Empty<Float3>();
    private float[] _scratchTime = Array.Empty<float>();

    internal bool HasOrphans => _orphans.Count > 0;

    /// <summary>
    /// Drops every trail when <see cref="MaxPoints"/> changed, since live slots are laid out for the old
    /// size. Returns true when it did, so the system can forget the slots its particles hold.
    /// </summary>
    internal bool ResetIfResized()
    {
        int capacity = Math.Clamp(MaxPoints, 2, 512);
        if (_capacity == 0 || capacity == _capacity) return false;

        _capacity = capacity;
        _points = Array.Empty<TrailPoint>();
        _states = Array.Empty<TrailState>();
        Clear();
        return true;
    }

    internal void Clear()
    {
        _free.Clear();
        _orphans.Clear();
        _slots = 0;
    }

    /// <summary>Gives the particle a trail if it rolls one. Returns a one based slot, 0 for none.</summary>
    internal int Allocate(in Particle p)
    {
        if (Ratio < 1f && p.Random(0x151) >= Ratio) return 0;
        if (_capacity == 0) _capacity = Math.Clamp(MaxPoints, 2, 512);

        int slot;
        if (_free.Count > 0)
            slot = _free.Pop();
        else
        {
            slot = _slots++;
            if (_states.Length < _slots)
            {
                int size = Math.Max(_slots, _states.Length * 2);
                Array.Resize(ref _states, size);
                Array.Resize(ref _points, size * _capacity);
            }
        }

        _states[slot] = new TrailState
        {
            Life = MathF.Max(0.001f, Lifetime.Evaluate(p.NormalizedAge, p.Random(0x152)) * p.StartLifetime),
            InWorld = WorldSpace,
            ColorRandom = p.Random(0x153),
        };
        return slot + 1;
    }

    /// <summary>Adds a point where the particle is once it has moved far enough, and drops expired points.</summary>
    internal void Record(ParticleSystemComponent system, in Particle p, float time)
    {
        int slot = p.TrailSlot - 1;
        if (slot < 0 || slot >= _slots) return;

        ref TrailState s = ref _states[slot];
        Float3 position = s.InWorld ? system.SimPointToWorld(p.Position) : p.Position;
        Expire(ref s, slot, time);

        if (s.Count > 0)
        {
            Float3 newest = _points[slot * _capacity + (s.Start + s.Count - 1) % _capacity].Position;
            if (Float3.LengthSquared(position - newest) < MinVertexDistance * MinVertexDistance)
                return;
        }
        Push(ref s, slot, position, time);
    }

    /// <summary>The particle died. Its trail either goes with it or fades out on its own.</summary>
    internal void Release(ParticleSystemComponent system, in Particle p, float time)
    {
        int slot = p.TrailSlot - 1;
        if (slot < 0 || slot >= _slots) return;

        ref TrailState s = ref _states[slot];
        if (DieWithParticles)
        {
            s.Count = 0;
            _free.Push(slot);
            return;
        }

        Push(ref s, slot, s.InWorld ? system.SimPointToWorld(p.Position) : p.Position, time);
        s.Orphan = true;
        s.Color = TrailColor(in p);
        s.Width = HeadWidth(system, in p);
        _orphans.Add(slot);
    }

    /// <summary>Ages the trails whose particles are gone and frees the ones that faded out.</summary>
    internal void UpdateOrphans(float time)
    {
        for (int i = _orphans.Count - 1; i >= 0; i--)
        {
            int slot = _orphans[i];
            ref TrailState s = ref _states[slot];
            Expire(ref s, slot, time);
            if (s.Count > 0) continue;

            s.Orphan = false;
            _free.Push(slot);
            _orphans.RemoveAt(i);
        }
    }

    private void Push(ref TrailState s, int slot, Float3 position, float time)
    {
        if (s.Count == _capacity)
        {
            s.Start = (s.Start + 1) % _capacity;
            s.Count--;
        }
        _points[slot * _capacity + (s.Start + s.Count) % _capacity] = new TrailPoint { Position = position, Time = time };
        s.Count++;
    }

    private void Expire(ref TrailState s, int slot, float time)
    {
        while (s.Count > 0 && time - _points[slot * _capacity + s.Start].Time > s.Life)
        {
            s.Start = (s.Start + 1) % _capacity;
            s.Count--;
        }
    }

    private Color TrailColor(in Particle p)
    {
        Color c = InheritParticleColor ? p.Color : Color.White;
        return c * ColorOverLifetime.Evaluate(p.NormalizedAge, p.Random(0x154));
    }

    private float HeadWidth(ParticleSystemComponent system, in Particle p)
    {
        float width = WidthMultiplier;
        if (SizeAffectsWidth)
            width *= (MathF.Abs(p.Size.X) + MathF.Abs(p.Size.Y)) * 0.5f * system.SizeScale;
        return width;
    }

    /// <summary>
    /// Writes one instance per trail segment. Each instance carries both ends of its segment (position,
    /// width, tangent, texture coordinate and color) and the particle shader turns it into a camera facing
    /// quad, so neighbouring segments share their edges exactly. Returns how many were written.
    /// </summary>
    internal int BuildSegments(ParticleSystemComponent system, float time, ref InstanceData[] buffer, ref AABB bounds, ref bool hasBounds)
    {
        int written = 0;
        ReadOnlySpan<Particle> particles = system.Particles;
        for (int i = 0; i < particles.Length; i++)
        {
            ref readonly Particle p = ref particles[i];
            int slot = p.TrailSlot - 1;
            if (slot < 0 || slot >= _slots) continue;

            ref TrailState s = ref _states[slot];
            Float3 head = s.InWorld ? system.SimPointToWorld(p.Position) : p.Position;
            written = BuildTrail(system, ref s, slot, head, true, time, TrailColor(in p), HeadWidth(system, in p), ref buffer, written, ref bounds, ref hasBounds);
        }

        foreach (int slot in _orphans)
        {
            ref TrailState s = ref _states[slot];
            written = BuildTrail(system, ref s, slot, default, false, time, s.Color, s.Width, ref buffer, written, ref bounds, ref hasBounds);
        }
        return written;
    }

    private int BuildTrail(ParticleSystemComponent system, ref TrailState s, int slot, Float3 head, bool hasHead, float time,
        Color color, float width, ref InstanceData[] buffer, int written, ref AABB bounds, ref bool hasBounds)
    {
        int count = s.Count + (hasHead ? 1 : 0);
        if (count < 2) return written;

        if (_scratchPos.Length < count)
        {
            _scratchPos = new Float3[count * 2];
            _scratchTime = new float[count * 2];
        }

        for (int k = 0; k < s.Count; k++)
        {
            TrailPoint point = _points[slot * _capacity + (s.Start + k) % _capacity];
            _scratchPos[k] = s.InWorld ? point.Position : system.SimPointToWorld(point.Position);
            _scratchTime[k] = point.Time;
        }
        if (hasHead)
        {
            _scratchPos[count - 1] = s.InWorld ? head : system.SimPointToWorld(head);
            _scratchTime[count - 1] = time;
        }

        int needed = written + count - 1;
        if (buffer.Length < needed)
            Array.Resize(ref buffer, Math.Max(needed, buffer.Length * 2));

        float totalLength = 0f;
        for (int k = 1; k < count; k++)
            totalLength += Float3.Distance(_scratchPos[k], _scratchPos[k - 1]);

        // Walk from the head back to the tail so texture coordinates start at the particle.
        float distanceFromHead = 0f;
        PointData next = MakePoint(count - 1, count, time, s.Life, color, width, 0f, totalLength, s.ColorRandom);
        for (int k = count - 2; k >= 0; k--)
        {
            distanceFromHead += Float3.Distance(_scratchPos[k + 1], _scratchPos[k]);
            PointData current = MakePoint(k, count, time, s.Life, color, width, distanceFromHead, totalLength, s.ColorRandom);

            buffer[written++] = new InstanceData
            {
                ModelRow0 = new Float4(current.Position, current.Width),
                ModelRow1 = new Float4(next.Position, next.Width),
                ModelRow2 = new Float4(current.Tangent, current.U),
                ModelRow3 = new Float4(next.Tangent, next.U),
                Color = current.Color,
                CustomData = next.Color,
            };

            float pad = MathF.Max(current.Width, next.Width) * 0.5f;
            Encapsulate(ref bounds, ref hasBounds, current.Position, pad);
            Encapsulate(ref bounds, ref hasBounds, next.Position, pad);
            next = current;
        }
        return written;
    }

    private struct PointData
    {
        public Float3 Position;
        public Float3 Tangent;
        public float Width;
        public float U;
        public Color Color;
    }

    private PointData MakePoint(int k, int count, float time, float life, Color color, float width, float distanceFromHead, float totalLength, float colorRandom)
    {
        Float3 before = _scratchPos[Math.Max(0, k - 1)];
        Float3 after = _scratchPos[Math.Min(count - 1, k + 1)];
        float t = Maths.Saturate((time - _scratchTime[k]) / life);

        return new PointData
        {
            Position = _scratchPos[k],
            Tangent = Float3.NormalizeSafe(after - before, Float3.UnitY),
            Width = width * WidthOverTrail.Evaluate(t),
            U = TextureMode == TrailTextureMode.Tile
                ? distanceFromHead / MathF.Max(TileLength, 1e-4f)
                : totalLength > 0f ? distanceFromHead / totalLength : 0f,
            Color = color * ColorOverTrail.Evaluate(t, colorRandom),
        };
    }

    private static void Encapsulate(ref AABB bounds, ref bool hasBounds, Float3 point, float pad)
    {
        AABB box = new(point - new Float3(pad), point + new Float3(pad));
        if (hasBounds) bounds.Encapsulate(box);
        else { bounds = box; hasBounds = true; }
    }
}
