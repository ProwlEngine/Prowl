using System.Collections.Generic;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// Per-frame root motion: the transform of the character root across the clip. Supports sampling
/// the delta (one root transform expressed in the frame of another) between two times.
/// </summary>
public sealed class RootMotion
{
    private readonly Transform3D[] _frames;
    private readonly float _duration;

    /// <summary>Builds root motion from per-frame root transforms and the clip duration in seconds.</summary>
    public RootMotion(IReadOnlyList<Transform3D> frameTransforms, float durationSeconds)
    {
        ArgumentNullException.ThrowIfNull(frameTransforms);
        if (frameTransforms.Count == 0)
            throw new ArgumentException("Root motion needs at least one frame.", nameof(frameTransforms));

        _frames = new Transform3D[frameTransforms.Count];
        for (int i = 0; i < _frames.Length; i++)
            _frames[i] = frameTransforms[i];
        _duration = durationSeconds;
    }

    public bool IsValid => _frames.Length > 0;

    public int FrameCount => _frames.Length;

    /// <summary>Length in seconds of the clip this root motion belongs to.</summary>
    public float Duration => _duration;

    /// <summary>The per frame root transforms.</summary>
    public IReadOnlyList<Transform3D> Frames => _frames;

    /// <summary>The total root delta from the start to the end of the clip.</summary>
    public Transform3D TotalDelta => TransformOps.Delta(_frames[0], _frames[^1]);

    /// <summary>The root delta between two frame times.</summary>
    public Transform3D GetDelta(FrameTime from, FrameTime to)
        => TransformOps.Delta(Sample(from), Sample(to));

    /// <summary>The root delta between two normalized times in [0,1].</summary>
    public Transform3D SampleDelta(float fromPercentage, float toPercentage)
        => TransformOps.Delta(SampleAt(fromPercentage), SampleAt(toPercentage));

    private Transform3D Sample(FrameTime frameTime)
    {
        int i0 = Math.Clamp(frameTime.FrameIndex, 0, _frames.Length - 1);
        if (i0 >= _frames.Length - 1 || frameTime.Percentage <= 0f)
            return _frames[i0];
        return Transform3D.Lerp(_frames[i0], _frames[i0 + 1], frameTime.Percentage);
    }

    private Transform3D SampleAt(float percentage)
    {
        float clamped = float.IsFinite(percentage) ? Maths.Clamp(percentage, 0f, 1f) : 0f;
        float frameValue = clamped * (_frames.Length - 1);
        return Sample(new FrameTime(frameValue));
    }
}
