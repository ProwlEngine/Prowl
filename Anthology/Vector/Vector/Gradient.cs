// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Prowl.Vector;

/// <summary>How a <see cref="Gradient"/> moves between its keys.</summary>
public enum GradientMode : byte
{
    /// <summary>Interpolate linearly from each key to the next.</summary>
    Blend,
    /// <summary>Hold each key until the next one is reached, giving hard bands.</summary>
    Fixed
}

/// <summary>An RGB key on a <see cref="Gradient"/>. The alpha channel is ignored.</summary>
[Serializable]
public struct GradientColorKey : IEquatable<GradientColorKey>, IComparable<GradientColorKey>
{
    /// <summary>Position of the key along the gradient.</summary>
    public float Time;

    /// <summary>Colour at this key.</summary>
    public Color Color;

    /// <summary>Creates a colour key.</summary>
    public GradientColorKey(float time, Color color)
    {
        Time = time;
        Color = color;
    }

    /// <summary>Orders keys by <see cref="Time"/>.</summary>
    public readonly int CompareTo(GradientColorKey other) => Time.CompareTo(other.Time);

    /// <inheritdoc/>
    public readonly bool Equals(GradientColorKey other) => Time.Equals(other.Time) && Color.Equals(other.Color);

    /// <inheritdoc/>
    public readonly override bool Equals(object? obj) => obj is GradientColorKey other && Equals(other);

    /// <inheritdoc/>
    public readonly override int GetHashCode() => (Time.GetHashCode() * 397) ^ Color.GetHashCode();

    /// <summary>Compares two keys for equality.</summary>
    public static bool operator ==(GradientColorKey left, GradientColorKey right) => left.Equals(right);

    /// <summary>Compares two keys for inequality.</summary>
    public static bool operator !=(GradientColorKey left, GradientColorKey right) => !left.Equals(right);

    /// <inheritdoc/>
    public readonly override string ToString() => Time.ToString() + ": " + Color.ToString();
}

/// <summary>An opacity key on a <see cref="Gradient"/>.</summary>
[Serializable]
public struct GradientAlphaKey : IEquatable<GradientAlphaKey>, IComparable<GradientAlphaKey>
{
    /// <summary>Position of the key along the gradient.</summary>
    public float Time;

    /// <summary>Opacity at this key.</summary>
    public float Alpha;

    /// <summary>Creates an alpha key.</summary>
    public GradientAlphaKey(float time, float alpha)
    {
        Time = time;
        Alpha = alpha;
    }

    /// <summary>Orders keys by <see cref="Time"/>.</summary>
    public readonly int CompareTo(GradientAlphaKey other) => Time.CompareTo(other.Time);

    /// <inheritdoc/>
    public readonly bool Equals(GradientAlphaKey other) => Time.Equals(other.Time) && Alpha.Equals(other.Alpha);

    /// <inheritdoc/>
    public readonly override bool Equals(object? obj) => obj is GradientAlphaKey other && Equals(other);

    /// <inheritdoc/>
    public readonly override int GetHashCode() => (Time.GetHashCode() * 397) ^ Alpha.GetHashCode();

    /// <summary>Compares two keys for equality.</summary>
    public static bool operator ==(GradientAlphaKey left, GradientAlphaKey right) => left.Equals(right);

    /// <summary>Compares two keys for inequality.</summary>
    public static bool operator !=(GradientAlphaKey left, GradientAlphaKey right) => !left.Equals(right);

    /// <inheritdoc/>
    public readonly override string ToString() => Time.ToString() + ": " + Alpha.ToString();
}

/// <summary>
/// A colour ramp built from independent colour and opacity keys.
/// </summary>
/// <remarks>
/// <para>
/// Colour and alpha are keyed separately so a fade can be placed without splitting a colour band,
/// which is the same split Unity and most DCC gradient editors use. Both lists are kept sorted by
/// time, and evaluation binary searches them, so a gradient stays correct however its keys were added.
/// </para>
/// <para>
/// Times are not restricted to 0 to 1. Evaluating outside the keyed range clamps to the nearest key,
/// and the two lists may cover different ranges.
/// </para>
/// </remarks>
[Serializable]
public sealed class Gradient
{
    // [DataMember] marks these for reflection based serializers such as Prowl.Echo. It is the BCL
    // attribute, so Vector takes no dependency on any serializer to be round-trippable.
    [DataMember] private List<GradientColorKey> _colorKeys;
    [DataMember] private List<GradientAlphaKey> _alphaKeys;

    /// <summary>How the gradient moves between keys.</summary>
    public GradientMode Mode = GradientMode.Blend;

    /// <summary>Colour keys in ascending time order.</summary>
    public IReadOnlyList<GradientColorKey> ColorKeys => _colorKeys;

    /// <summary>Alpha keys in ascending time order.</summary>
    public IReadOnlyList<GradientAlphaKey> AlphaKeys => _alphaKeys;

    /// <summary>Creates a solid white gradient spanning 0 to 1.</summary>
    public Gradient()
    {
        _colorKeys = new List<GradientColorKey> { new GradientColorKey(0f, Color.White), new GradientColorKey(1f, Color.White) };
        _alphaKeys = new List<GradientAlphaKey> { new GradientAlphaKey(0f, 1f), new GradientAlphaKey(1f, 1f) };
    }

    /// <summary>Creates a gradient from the given keys, in any order.</summary>
    public Gradient(IEnumerable<GradientColorKey> colorKeys, IEnumerable<GradientAlphaKey> alphaKeys)
    {
        _colorKeys = new List<GradientColorKey>();
        _alphaKeys = new List<GradientAlphaKey>();
        SetKeys(colorKeys, alphaKeys);
    }

    /// <summary>Creates a gradient that is one colour everywhere.</summary>
    public static Gradient Solid(Color color) =>
        new Gradient(
            new[] { new GradientColorKey(0f, color) },
            new[] { new GradientAlphaKey(0f, color.A) });

    /// <summary>Creates a gradient ramping from one colour to another over 0 to 1.</summary>
    public static Gradient Between(Color from, Color to) => Between(from, to, 0f, 1f);

    /// <summary>Creates a gradient ramping from one colour to another over the given times.</summary>
    public static Gradient Between(Color from, Color to, float startTime, float endTime) =>
        new Gradient(
            new[] { new GradientColorKey(startTime, from), new GradientColorKey(endTime, to) },
            new[] { new GradientAlphaKey(startTime, from.A), new GradientAlphaKey(endTime, to.A) });

    /// <summary>Time of the first key of either list, or zero when the gradient has no keys.</summary>
    public float StartTime
    {
        get
        {
            if (_colorKeys.Count == 0) return _alphaKeys.Count == 0 ? 0f : _alphaKeys[0].Time;
            if (_alphaKeys.Count == 0) return _colorKeys[0].Time;
            return Maths.Min(_colorKeys[0].Time, _alphaKeys[0].Time);
        }
    }

    /// <summary>Time of the last key of either list, or zero when the gradient has no keys.</summary>
    public float EndTime
    {
        get
        {
            if (_colorKeys.Count == 0) return _alphaKeys.Count == 0 ? 0f : _alphaKeys[_alphaKeys.Count - 1].Time;
            if (_alphaKeys.Count == 0) return _colorKeys[_colorKeys.Count - 1].Time;
            return Maths.Max(_colorKeys[_colorKeys.Count - 1].Time, _alphaKeys[_alphaKeys.Count - 1].Time);
        }
    }

    #region Editing

    /// <summary>Adds a colour key, keeping the list sorted, and returns its index.</summary>
    public int AddColorKey(float time, Color color) => AddColorKey(new GradientColorKey(time, color));

    /// <summary>Adds a colour key, keeping the list sorted, and returns its index.</summary>
    public int AddColorKey(GradientColorKey key)
    {
        int index = ColorUpperBound(key.Time);
        _colorKeys.Insert(index, key);
        return index;
    }

    /// <summary>Adds an alpha key, keeping the list sorted, and returns its index.</summary>
    public int AddAlphaKey(float time, float alpha) => AddAlphaKey(new GradientAlphaKey(time, alpha));

    /// <summary>Adds an alpha key, keeping the list sorted, and returns its index.</summary>
    public int AddAlphaKey(GradientAlphaKey key)
    {
        int index = AlphaUpperBound(key.Time);
        _alphaKeys.Insert(index, key);
        return index;
    }

    /// <summary>Replaces a colour key and returns its index after any reordering.</summary>
    public int SetColorKey(int index, GradientColorKey key)
    {
        ThrowIfOutOfRange(index, _colorKeys.Count);

        if (_colorKeys[index].Time == key.Time)
        {
            _colorKeys[index] = key;
            return index;
        }

        _colorKeys.RemoveAt(index);
        return AddColorKey(key);
    }

    /// <summary>Replaces an alpha key and returns its index after any reordering.</summary>
    public int SetAlphaKey(int index, GradientAlphaKey key)
    {
        ThrowIfOutOfRange(index, _alphaKeys.Count);

        if (_alphaKeys[index].Time == key.Time)
        {
            _alphaKeys[index] = key;
            return index;
        }

        _alphaKeys.RemoveAt(index);
        return AddAlphaKey(key);
    }

    /// <summary>Removes the colour key at <paramref name="index"/>.</summary>
    public void RemoveColorKey(int index)
    {
        ThrowIfOutOfRange(index, _colorKeys.Count);
        _colorKeys.RemoveAt(index);
    }

    /// <summary>Removes the alpha key at <paramref name="index"/>.</summary>
    public void RemoveAlphaKey(int index)
    {
        ThrowIfOutOfRange(index, _alphaKeys.Count);
        _alphaKeys.RemoveAt(index);
    }

    /// <summary>Replaces every key. The supplied keys do not need to be sorted.</summary>
    public void SetKeys(IEnumerable<GradientColorKey> colorKeys, IEnumerable<GradientAlphaKey> alphaKeys)
    {
        if (colorKeys is null) throw new ArgumentNullException(nameof(colorKeys));
        if (alphaKeys is null) throw new ArgumentNullException(nameof(alphaKeys));

        var color = new List<GradientColorKey>(colorKeys);
        var alpha = new List<GradientAlphaKey>(alphaKeys);
        color.Sort();
        alpha.Sort();

        _colorKeys.Clear();
        _colorKeys.AddRange(color);
        _alphaKeys.Clear();
        _alphaKeys.AddRange(alpha);
    }

    /// <summary>Removes every key.</summary>
    public void Clear()
    {
        _colorKeys.Clear();
        _alphaKeys.Clear();
    }

    /// <summary>Index of the colour key nearest <paramref name="time"/> within <paramref name="tolerance"/>, or -1.</summary>
    public int FindColorKey(float time, float tolerance)
    {
        int best = -1;
        float bestDistance = tolerance;
        for (int i = 0; i < _colorKeys.Count; i++)
        {
            float distance = Maths.Abs(_colorKeys[i].Time - time);
            if (distance > bestDistance) continue;
            bestDistance = distance;
            best = i;
        }
        return best;
    }

    /// <summary>Index of the alpha key nearest <paramref name="time"/> within <paramref name="tolerance"/>, or -1.</summary>
    public int FindAlphaKey(float time, float tolerance)
    {
        int best = -1;
        float bestDistance = tolerance;
        for (int i = 0; i < _alphaKeys.Count; i++)
        {
            float distance = Maths.Abs(_alphaKeys[i].Time - time);
            if (distance > bestDistance) continue;
            bestDistance = distance;
            best = i;
        }
        return best;
    }

    /// <summary>Creates an independent copy of this gradient.</summary>
    public Gradient Clone() => new Gradient(_colorKeys, _alphaKeys) { Mode = Mode };

    #endregion

    #region Evaluation

    /// <summary>
    /// Evaluates the gradient. Times outside the keyed range clamp to the nearest key; a gradient
    /// with no colour keys evaluates as white and one with no alpha keys as fully opaque.
    /// </summary>
    public Color Evaluate(float time)
    {
        Color color = EvaluateRgb(time);
        return new Color(color.R, color.G, color.B, EvaluateAlpha(time));
    }

    /// <summary>Evaluates only the colour keys. The returned alpha is always 1.</summary>
    public Color EvaluateRgb(float time)
    {
        int count = _colorKeys.Count;
        if (count == 0) return Color.White;

        GradientColorKey first = _colorKeys[0];
        if (count == 1 || time <= first.Time) return Opaque(first.Color);

        GradientColorKey last = _colorKeys[count - 1];
        if (time >= last.Time) return Opaque(last.Color);

        int i = ColorSegment(time, count);
        GradientColorKey a = _colorKeys[i];
        if (Mode == GradientMode.Fixed) return Opaque(a.Color);

        GradientColorKey b = _colorKeys[i + 1];
        float span = b.Time - a.Time;
        float t = span > 0f ? (time - a.Time) / span : 0f;
        return Opaque(Color.Lerp(a.Color, b.Color, t));
    }

    /// <summary>Evaluates only the alpha keys.</summary>
    public float EvaluateAlpha(float time)
    {
        int count = _alphaKeys.Count;
        if (count == 0) return 1f;

        GradientAlphaKey first = _alphaKeys[0];
        if (count == 1 || time <= first.Time) return first.Alpha;

        GradientAlphaKey last = _alphaKeys[count - 1];
        if (time >= last.Time) return last.Alpha;

        int i = AlphaSegment(time, count);
        GradientAlphaKey a = _alphaKeys[i];
        if (Mode == GradientMode.Fixed) return a.Alpha;

        GradientAlphaKey b = _alphaKeys[i + 1];
        float span = b.Time - a.Time;
        float t = span > 0f ? (time - a.Time) / span : 0f;
        return a.Alpha + (b.Alpha - a.Alpha) * t;
    }

    private static Color Opaque(Color color) => new Color(color.R, color.G, color.B, 1f);

    /// <summary>Largest index whose time is at or before <paramref name="time"/>, capped so a next key exists.</summary>
    private int ColorSegment(float time, int count)
    {
        int lo = 0, hi = count - 1;
        while (lo + 1 < hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            if (_colorKeys[mid].Time <= time) lo = mid;
            else hi = mid;
        }
        return lo;
    }

    /// <summary>Largest index whose time is at or before <paramref name="time"/>, capped so a next key exists.</summary>
    private int AlphaSegment(float time, int count)
    {
        int lo = 0, hi = count - 1;
        while (lo + 1 < hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            if (_alphaKeys[mid].Time <= time) lo = mid;
            else hi = mid;
        }
        return lo;
    }

    private int ColorUpperBound(float time)
    {
        int lo = 0, hi = _colorKeys.Count;
        while (lo < hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            if (_colorKeys[mid].Time <= time) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private int AlphaUpperBound(float time)
    {
        int lo = 0, hi = _alphaKeys.Count;
        while (lo < hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            if (_alphaKeys[mid].Time <= time) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private static void ThrowIfOutOfRange(int index, int count)
    {
        if ((uint)index >= (uint)count)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Gradient has {count} key(s) of that kind.");
    }

    #endregion
}
