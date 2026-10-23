using System;

namespace Prowl.Graphite;

/// <summary>How long the graph keeps a resource it created: one frame, a ring of earlier frames, or until released.</summary>
public readonly struct Lifetime : IEquatable<Lifetime>
{
    private readonly int _frames;

    private Lifetime(int frames) => _frames = frames;

    /// <summary>Lasts one frame. The default.</summary>
    public static Lifetime Transient => default;

    /// <summary>One copy kept across frames until it goes unused or is released.</summary>
    public static Lifetime Persistent => new(-1);

    /// <summary>The current copy plus the given number of earlier ones.</summary>
    public static Lifetime History(int frames)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frames);
        return new Lifetime(frames);
    }

    public bool IsTransient => _frames == 0;

    public bool IsPersistent => _frames < 0;

    /// <summary>Earlier frames kept, zero unless this is a history lifetime.</summary>
    public int HistoryFrames => Math.Max(0, _frames);

    internal bool IsRetained => _frames != 0;

    internal int Slots => _frames < 0 ? 1 : _frames + 1;

    public bool Equals(Lifetime other) => _frames == other._frames;

    public override bool Equals(object? obj) => obj is Lifetime other && Equals(other);

    public override int GetHashCode() => _frames;

    public static bool operator ==(Lifetime a, Lifetime b) => a._frames == b._frames;

    public static bool operator !=(Lifetime a, Lifetime b) => a._frames != b._frames;
}
