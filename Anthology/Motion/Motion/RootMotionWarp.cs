using System.Collections.Generic;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>Options for <see cref="RootMotionWarp.Override"/>.</summary>
public struct RootMotionOverrideOptions
{
    public RootMotionOverrideOptions() { }

    /// <summary>Multiplier on the linear (translation) part of the root delta.</summary>
    public float LinearSpeedScale = 1f;
    /// <summary>Maximum linear speed in units/second; values &lt;= 0 disable the clamp.</summary>
    public float MaxLinearSpeed;
    /// <summary>Maximum angular speed in degrees/second; values &lt;= 0 disable the clamp.</summary>
    public float MaxAngularSpeedDegrees;
    /// <summary>If true, rotate the horizontal translation to follow <see cref="DesiredHeadingForward"/>.</summary>
    public bool OverrideHeading;
    /// <summary>Desired world-space horizontal heading (used when <see cref="OverrideHeading"/> is set).</summary>
    public Float3 DesiredHeadingForward;

    public static RootMotionOverrideOptions Default => new();
}

/// <summary>
/// Root motion overriding and warping. Trajectories are character space transforms relative to the
/// first frame.
/// </summary>
public static class RootMotionWarp
{
    private const float Epsilon = 1e-5f;

    /// <summary>Scales, clamps, and optionally re-headings a single per-frame root-motion delta.</summary>
    public static Transform3D Override(Transform3D delta, float deltaTime, RootMotionOverrideOptions options)
    {
        Float3 t = delta.position * MathF.Max(0f, options.LinearSpeedScale);

        if (options.OverrideHeading)
        {
            Float3 currentHoriz = new(t.X, 0f, t.Z);
            Float3 desiredHoriz = new(options.DesiredHeadingForward.X, 0f, options.DesiredHeadingForward.Z);
            if (Float3.Length(currentHoriz) > Epsilon && Float3.Length(desiredHoriz) > Epsilon)
                t = YawBetween(currentHoriz, desiredHoriz) * t;
        }

        if (options.MaxLinearSpeed > 0f && deltaTime > 0f)
        {
            float length = Float3.Length(t);
            float maxLength = options.MaxLinearSpeed * deltaTime;
            if (length > maxLength && length > 1e-6f)
                t *= maxLength / length;
        }

        Quaternion r = delta.rotation;
        if (options.MaxAngularSpeedDegrees > 0f && deltaTime > 0f)
        {
            float maxRad = options.MaxAngularSpeedDegrees * Maths.Deg2Rad * deltaTime;
            float angle = 2f * MathF.Acos(Math.Clamp(MathF.Abs(r.W), -1f, 1f));
            if (angle > maxRad && angle > 1e-6f)
                r = Quaternion.Slerp(Quaternion.Identity, r, maxRad / angle);
        }

        return new Transform3D(t, r, delta.scale);
    }

    /// <summary>
    /// Rewrites the trajectory so the travel after the warp window heads along
    /// <paramref name="targetDirection"/>, spreading the turn over the window. Null when there is
    /// nothing to warp.
    /// </summary>
    public static Transform3D[]? WarpOrientation(RootMotion rootMotion, Float3 targetDirection, float windowStart, float windowEnd, float startTime = 0f)
    {
        ArgumentNullException.ThrowIfNull(rootMotion);
        Float3 target = Horizontal(targetDirection);
        Transform3D[] frames = RelativeFrames(rootMotion);
        if (Float3.Length(target) < Epsilon || !TryGetOrientationWindow(frames.Length - 1, windowStart, windowEnd, startTime, out int s, out int e))
            return null;
        return WarpOrientationFrames(frames, YawAngle(PostWarpDirection(frames, e), target), s, e);
    }

    /// <summary>
    /// Orientation warp by an angle: turns the travel after the warp window by
    /// <paramref name="yawDegrees"/> about up. See <see cref="WarpOrientation"/>.
    /// </summary>
    public static Transform3D[]? WarpOrientationByAngle(RootMotion rootMotion, float yawDegrees, float windowStart, float windowEnd, float startTime = 0f)
    {
        ArgumentNullException.ThrowIfNull(rootMotion);
        Transform3D[] frames = RelativeFrames(rootMotion);
        if (!TryGetOrientationWindow(frames.Length - 1, windowStart, windowEnd, startTime, out int s, out int e))
            return null;
        return WarpOrientationFrames(frames, yawDegrees * Maths.Deg2Rad, s, e);
    }

    /// <summary>
    /// Target warp over the whole clip: rewrites the trajectory so the total displacement becomes
    /// <paramref name="desiredTotalDelta"/>. See <see cref="WarpTrajectory(RootMotion, Float3, float, float, float)"/>.
    /// </summary>
    public static Transform3D[] WarpTrajectory(RootMotion rootMotion, Float3 desiredTotalDelta)
        => WarpTrajectory(rootMotion, desiredTotalDelta, 0f, 1f, 0f);

    /// <summary>
    /// Rewrites the trajectory so the total displacement becomes <paramref name="desiredTotalDelta"/>,
    /// warping only the frames inside the window. The horizontal path is turned and scaled, the vertical
    /// offset linearly.
    /// </summary>
    public static Transform3D[] WarpTrajectory(RootMotion rootMotion, Float3 desiredTotalDelta, float windowStart, float windowEnd, float startTime)
    {
        ArgumentNullException.ThrowIfNull(rootMotion);
        Transform3D[] frames = RelativeFrames(rootMotion);
        int last = frames.Length - 1;
        if (last < 1)
            return frames;

        int s = Math.Clamp((int)MathF.Round(MathF.Max(windowStart, startTime) * last), 0, last);
        int e = Math.Clamp((int)MathF.Ceiling(Math.Clamp(windowEnd, 0f, 1f) * last), 0, last);
        if (e <= s)
            return frames;

        Float3 start = frames[s].position;
        Float3 window = frames[e].position - start;
        Float3 tail = frames[last].position - frames[e].position;
        Float3 goal = desiredTotalDelta - start;

        Float3 windowH = Horizontal(window);
        Float3 tailH = Horizontal(tail);
        Float3 goalH = Horizontal(goal);

        float scale = 1f;
        Quaternion turn = Quaternion.Identity;
        Float3 spread = default;
        if (Float3.Length(windowH) > Epsilon)
        {
            scale = SolveWindowScale(windowH, tailH, Float3.Length(goalH));
            Float3 reached = windowH * scale + tailH;
            if (Float3.Length(reached) > Epsilon && Float3.Length(goalH) > Epsilon)
                turn = YawBetween(reached, goalH);
        }
        else
        {
            spread = goalH - tailH;
        }
        float lift = goal.Y - window.Y - tail.Y;

        var output = new Transform3D[frames.Length];
        for (int i = 0; i <= s; i++)
            output[i] = frames[i];
        for (int i = s + 1; i <= e; i++)
        {
            float pct = (i - s) / (float)(e - s);
            Float3 offset = frames[i].position - start;
            Float3 offsetH = Horizontal(offset);
            Float3 warped = turn * (offsetH * scale + spread * pct);
            output[i] = new Transform3D(start + new Float3(warped.X, offset.Y + lift * pct, warped.Z), turn * frames[i].rotation, frames[i].scale);
        }
        for (int i = e + 1; i <= last; i++)
            output[i] = TransformOps.Combine(output[i - 1], TransformOps.Delta(frames[i - 1], frames[i]));
        return output;
    }

    /// <summary>The rotation about up that turns the horizontal part of <paramref name="from"/> onto that of <paramref name="to"/>.</summary>
    internal static Quaternion YawBetween(Float3 from, Float3 to) => Quaternion.AxisAngle(Float3.UnitY, YawAngle(from, to));

    /// <summary>The signed angle in radians about up from the horizontal part of <paramref name="from"/> to that of <paramref name="to"/>.</summary>
    internal static float YawAngle(Float3 from, Float3 to)
    {
        Float3 f = Horizontal(from);
        Float3 t = Horizontal(to);
        if (Float3.Length(f) < Epsilon || Float3.Length(t) < Epsilon)
            return 0f;
        return MathF.Atan2(f.Z * t.X - f.X * t.Z, f.X * t.X + f.Z * t.Z);
    }

    // Window frames [s, e]. A zero length window gives s == e. A window already passed fails.
    private static bool TryGetOrientationWindow(int last, float windowStart, float windowEnd, float startTime, out int s, out int e)
    {
        s = Math.Clamp((int)MathF.Round(Math.Clamp(MathF.Max(windowStart, startTime), 0f, 1f) * last), 0, last);
        if (windowEnd <= windowStart)
        {
            e = s;
            return last > 0;
        }
        e = Math.Clamp((int)MathF.Ceiling(Math.Clamp(windowEnd, 0f, 1f) * last), 0, last);
        return e > s;
    }

    private static Transform3D[] WarpOrientationFrames(Transform3D[] frames, float yawRadians, int s, int e)
    {
        int last = frames.Length - 1;
        var output = new Transform3D[frames.Length];
        for (int i = 0; i <= s; i++)
            output[i] = frames[i];

        if (e == s)
        {
            Quaternion turn = Quaternion.AxisAngle(Float3.UnitY, yawRadians);
            Float3 pivot = frames[s].position;
            for (int i = s + 1; i <= last; i++)
                output[i] = new Transform3D(pivot + turn * (frames[i].position - pivot), turn * frames[i].rotation, frames[i].scale);
            return output;
        }

        for (int i = s + 1; i <= e; i++)
        {
            Quaternion ramp = Quaternion.AxisAngle(Float3.UnitY, yawRadians * (i - s) / (e - s));
            output[i] = new Transform3D(frames[i].position, ramp * frames[i].rotation, frames[i].scale);
        }
        for (int i = e + 1; i <= last; i++)
            output[i] = TransformOps.Combine(output[i - 1], TransformOps.Delta(frames[i - 1], frames[i]));
        return output;
    }

    // The horizontal travel after frame e, or the last frame's facing when there is none.
    private static Float3 PostWarpDirection(Transform3D[] frames, int e)
    {
        Float3 travel = Horizontal(frames[^1].position - frames[e].position);
        return Float3.Length(travel) > Epsilon ? travel : Horizontal(frames[^1].rotation * new Float3(0f, 0f, 1f));
    }

    // The non negative scale s with |window * s + tail| == goalLength, or the closest achievable.
    private static float SolveWindowScale(Float3 window, Float3 tail, float goalLength)
    {
        float a = Float3.Dot(window, window);
        float b = 2f * Float3.Dot(window, tail);
        float c = Float3.Dot(tail, tail) - goalLength * goalLength;
        float disc = b * b - 4f * a * c;
        if (disc < 0f)
            return MathF.Max(0f, -b / (2f * a));
        float root = (-b + MathF.Sqrt(disc)) / (2f * a);
        return MathF.Max(0f, root);
    }

    private static Transform3D[] RelativeFrames(RootMotion rootMotion)
    {
        int count = rootMotion.FrameCount;
        var frames = new Transform3D[count];
        for (int i = 0; i < count; i++)
            frames[i] = rootMotion.SampleDelta(0f, count > 1 ? i / (float)(count - 1) : 0f);
        return frames;
    }

    private static Float3 Horizontal(Float3 v) => new(v.X, 0f, v.Z);
}
