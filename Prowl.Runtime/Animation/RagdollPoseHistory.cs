// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Runtime;

/// <summary>
/// The animation's recent frames by time: where the character was, and each part's pose within it. A
/// physics step samples it at any moment it has seen, so the ragdoll follows the animation exactly
/// rather than guessing where it goes next.
/// </summary>
internal sealed class RagdollPoseHistory
{
    /// <summary>Two recorded frames around a moment, and how far between them it is.</summary>
    public readonly record struct Moment(int From, int To, float Along);

    private readonly double[] _times;
    private readonly Transform3D[] _characters;
    private readonly Transform3D[,] _parts;
    private int _oldest, _count;

    public RagdollPoseHistory(int parts, int capacity)
    {
        _times = new double[capacity];
        _characters = new Transform3D[capacity];
        _parts = new Transform3D[capacity, parts];
    }

    public int Count => _count;

    public void Clear() => _oldest = _count = 0;

    /// <summary>Starts the frame at <paramref name="time"/>, or reuses it when already recorded, and returns its slot.</summary>
    public int Record(double time, in Transform3D character)
    {
        int slot;
        if (_count > 0 && _times[Slot(_count - 1)] == time)
        {
            slot = Slot(_count - 1);
        }
        else
        {
            if (_count == _times.Length) _oldest = (_oldest + 1) % _times.Length;
            else _count++;
            slot = Slot(_count - 1);
            _times[slot] = time;
        }

        _characters[slot] = character;
        return slot;
    }

    public void SetPart(int slot, int part, in Transform3D pose) => _parts[slot, part] = pose;

    /// <summary>The frames around <paramref name="time"/>, carrying on past the newest when it is later still.</summary>
    public Moment At(double time)
    {
        if (_count < 2) return new Moment(Slot(_count - 1), Slot(_count - 1), 0f);

        int next = 1;
        while (next < _count - 1 && _times[Slot(next)] < time) next++;
        int from = Slot(next - 1), to = Slot(next);
        return new Moment(from, to, MathF.Max(0f, (float)((time - _times[from]) / (_times[to] - _times[from]))));
    }

    public Transform3D Character(in Moment moment) => Between(_characters[moment.From], _characters[moment.To], moment.Along);

    public Transform3D Part(in Moment moment, int part) => Between(_parts[moment.From, part], _parts[moment.To, part], moment.Along);

    private int Slot(int index) => (_oldest + Math.Max(index, 0)) % _times.Length;

    private static Transform3D Between(in Transform3D from, in Transform3D to, float t)
    {
        Quaternion rotation = t <= 1f
            ? Quaternion.Slerp(from.rotation, to.rotation, t)
            : Quaternion.Normalize(Quaternion.FromRotationVector(Quaternion.ToRotationVector(to.rotation * Quaternion.Inverse(from.rotation)) * (t - 1f)) * to.rotation);
        return new Transform3D(from.position + (to.position - from.position) * t, rotation, Float3.One);
    }
}
