namespace Prowl.Motion;

/// <summary>
/// A time position within an animation expressed as an integer frame index plus a fractional part
/// in [0,1) through that frame.
/// </summary>
public readonly struct FrameTime : IEquatable<FrameTime>
{
    private readonly int _frameIndex;
    private readonly float _percentage;

    /// <summary>Creates a frame time from a separate frame index and fractional part.</summary>
    public FrameTime(int frameIndex, float percentageThrough)
    {
        _frameIndex = frameIndex;
        _percentage = percentageThrough;
    }

    /// <summary>Creates a frame time from a fractional frame value (e.g. 3.25 -> frame 3, 0.25).</summary>
    public FrameTime(float frameTime)
    {
        _frameIndex = (int)MathF.Floor(frameTime);
        _percentage = frameTime - _frameIndex;
    }

    /// <summary>The integer frame index.</summary>
    public int FrameIndex => _frameIndex;

    /// <summary>The fraction through the current frame, in [0,1).</summary>
    public float Percentage => _percentage;

    /// <summary>The combined fractional frame value (FrameIndex + Percentage).</summary>
    public float ToFloat() => _frameIndex + _percentage;

    public bool Equals(FrameTime other) => _frameIndex == other._frameIndex && _percentage.Equals(other._percentage);
    public override bool Equals(object? obj) => obj is FrameTime other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_frameIndex, _percentage);
    public override string ToString() => $"FrameTime(frame {_frameIndex}, {_percentage:0.###})";
}
