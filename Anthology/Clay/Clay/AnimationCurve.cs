using Prowl.Vector;

namespace Prowl.Clay;

/// <summary>
/// A keyframed curve. Data is stored in flat float arrays for cache-friendly access; one
/// curve handles scalar, vec3 or quat properties depending on <see cref="Dimension"/>.
/// </summary>
/// <remarks>
/// For <see cref="AnimationInterpolation.Linear"/> and <see cref="AnimationInterpolation.Step"/>:
/// <see cref="Values"/> contains <c>Dimension * Times.Length</c> floats laid out as
/// <c>[key0_components, key1_components, ...]</c>.
/// For <see cref="AnimationInterpolation.CubicSpline"/>: <see cref="Values"/> contains
/// <c>3 * Dimension * Times.Length</c> floats, laid out as
/// <c>[key0_inTan, key0_value, key0_outTan, key1_inTan, ...]</c>, matching glTF.
/// </remarks>
public sealed class AnimationCurve
{
    /// <summary>Interpolation kind used between keyframes.</summary>
    public required AnimationInterpolation Interpolation { get; init; }

    /// <summary>Component count per key: 1 (scalar), 3 (vec3), or 4 (quaternion XYZW).</summary>
    public required int Dimension { get; init; }

    /// <summary>Strictly increasing key times in seconds.</summary>
    public required float[] Times { get; init; }

    /// <summary>Packed values; see remarks on <see cref="AnimationCurve"/> for the layout.</summary>
    public required float[] Values { get; init; }

    /// <summary>Evaluates the curve at time <paramref name="time"/> as a scalar.</summary>
    public float EvaluateFloat(float time)
    {
        Locate(time, out int i0, out int i1, out float u);
        return Interpolation switch
        {
            AnimationInterpolation.Step => StepFloat(i0),
            AnimationInterpolation.CubicSpline => HermiteFloat(i0, i1, u),
            _ => LerpFloat(i0, i1, u),
        };
    }

    /// <summary>Evaluates the curve at time <paramref name="time"/> as a 3-component vector.</summary>
    public Float3 EvaluateVector3(float time)
    {
        Locate(time, out int i0, out int i1, out float u);
        return Interpolation switch
        {
            AnimationInterpolation.Step => StepVec3(i0),
            AnimationInterpolation.CubicSpline => HermiteVec3(i0, i1, u),
            _ => LerpVec3(i0, i1, u),
        };
    }

    /// <summary>
    /// Evaluates the curve at time <paramref name="time"/> as a quaternion. Linear interpolation
    /// uses spherical-linear (slerp) which matches glTF's normative behavior.
    /// </summary>
    public Quaternion EvaluateQuaternion(float time)
    {
        Locate(time, out int i0, out int i1, out float u);
        return Interpolation switch
        {
            AnimationInterpolation.Step => StepQuat(i0),
            AnimationInterpolation.CubicSpline => HermiteQuatNormalized(i0, i1, u),
            _ => SlerpQuat(i0, i1, u),
        };
    }

    private void Locate(float time, out int i0, out int i1, out float u)
    {
        if (Times.Length == 0) { i0 = i1 = 0; u = 0f; return; }
        if (Times.Length == 1) { i0 = i1 = 0; u = 0f; return; }
        if (time <= Times[0]) { i0 = i1 = 0; u = 0f; return; }
        if (time >= Times[^1]) { i0 = i1 = Times.Length - 1; u = 0f; return; }

        int lo = 0, hi = Times.Length - 1;
        while (lo + 1 < hi)
        {
            int mid = (lo + hi) >> 1;
            if (Times[mid] <= time) lo = mid;
            else hi = mid;
        }
        i0 = lo;
        i1 = lo + 1;
        float t0 = Times[i0], t1 = Times[i1];
        u = (t1 - t0) > 1e-12f ? (time - t0) / (t1 - t0) : 0f;
    }

    private float StepFloat(int i)
    {
        int stride = Interpolation == AnimationInterpolation.CubicSpline ? Dimension * 3 : Dimension;
        int valueOffset = Interpolation == AnimationInterpolation.CubicSpline ? Dimension : 0;
        return Values[i * stride + valueOffset];
    }

    private float LerpFloat(int i0, int i1, float u)
    {
        int stride = Dimension;
        float a = Values[i0 * stride];
        float b = Values[i1 * stride];
        return a + (b - a) * u;
    }

    private float HermiteFloat(int i0, int i1, float u)
    {
        int stride = Dimension * 3;
        // Cubic Hermite per glTF 2.0: dt = t1 - t0; v(t) = h00 v0 + h10 dt outTan_0 + h01 v1 + h11 dt inTan_1.
        float t0 = Times[i0], t1 = Times[i1], dt = t1 - t0;
        float v0    = Values[i0 * stride + Dimension];
        float out0  = Values[i0 * stride + Dimension * 2];
        float in1   = Values[i1 * stride + 0];
        float v1    = Values[i1 * stride + Dimension];
        return Hermite1D(v0, out0, in1, v1, u, dt);
    }

    private Float3 StepVec3(int i)
    {
        int stride = Interpolation == AnimationInterpolation.CubicSpline ? Dimension * 3 : Dimension;
        int valueOffset = Interpolation == AnimationInterpolation.CubicSpline ? Dimension : 0;
        int o = i * stride + valueOffset;
        return new Float3(Values[o], Values[o + 1], Values[o + 2]);
    }

    private Float3 LerpVec3(int i0, int i1, float u)
    {
        int stride = Dimension;
        int a = i0 * stride;
        int b = i1 * stride;
        return new Float3(
            Values[a] + (Values[b] - Values[a]) * u,
            Values[a + 1] + (Values[b + 1] - Values[a + 1]) * u,
            Values[a + 2] + (Values[b + 2] - Values[a + 2]) * u);
    }

    private Float3 HermiteVec3(int i0, int i1, float u)
    {
        int stride = Dimension * 3;
        float t0 = Times[i0], t1 = Times[i1], dt = t1 - t0;
        int v0Off  = i0 * stride + Dimension;
        int oOff   = i0 * stride + Dimension * 2;
        int inOff  = i1 * stride + 0;
        int v1Off  = i1 * stride + Dimension;

        return new Float3(
            Hermite1D(Values[v0Off],     Values[oOff],     Values[inOff],     Values[v1Off],     u, dt),
            Hermite1D(Values[v0Off + 1], Values[oOff + 1], Values[inOff + 1], Values[v1Off + 1], u, dt),
            Hermite1D(Values[v0Off + 2], Values[oOff + 2], Values[inOff + 2], Values[v1Off + 2], u, dt));
    }

    private Quaternion StepQuat(int i)
    {
        int stride = Interpolation == AnimationInterpolation.CubicSpline ? Dimension * 3 : Dimension;
        int valueOffset = Interpolation == AnimationInterpolation.CubicSpline ? Dimension : 0;
        int o = i * stride + valueOffset;
        return new Quaternion(Values[o], Values[o + 1], Values[o + 2], Values[o + 3]);
    }

    private Quaternion SlerpQuat(int i0, int i1, float u)
    {
        int stride = Dimension;
        int a = i0 * stride;
        int b = i1 * stride;
        var q0 = new Quaternion(Values[a],     Values[a + 1], Values[a + 2], Values[a + 3]);
        var q1 = new Quaternion(Values[b],     Values[b + 1], Values[b + 2], Values[b + 3]);
        return Slerp(q0, q1, u);
    }

    private Quaternion HermiteQuatNormalized(int i0, int i1, float u)
    {
        int stride = Dimension * 3;
        float t0 = Times[i0], t1 = Times[i1], dt = t1 - t0;
        int v0Off = i0 * stride + Dimension;
        int oOff  = i0 * stride + Dimension * 2;
        int inOff = i1 * stride + 0;
        int v1Off = i1 * stride + Dimension;

        float x = Hermite1D(Values[v0Off],     Values[oOff],     Values[inOff],     Values[v1Off],     u, dt);
        float y = Hermite1D(Values[v0Off + 1], Values[oOff + 1], Values[inOff + 1], Values[v1Off + 1], u, dt);
        float z = Hermite1D(Values[v0Off + 2], Values[oOff + 2], Values[inOff + 2], Values[v1Off + 2], u, dt);
        float w = Hermite1D(Values[v0Off + 3], Values[oOff + 3], Values[inOff + 3], Values[v1Off + 3], u, dt);
        float len = MathF.Sqrt(x * x + y * y + z * z + w * w);
        return len < 1e-12f
            ? Quaternion.Identity
            : new Quaternion(x / len, y / len, z / len, w / len);
    }

    private static float Hermite1D(float v0, float outTan, float inTan, float v1, float u, float dt)
    {
        float u2 = u * u;
        float u3 = u2 * u;
        float h00 = 2f * u3 - 3f * u2 + 1f;
        float h10 = u3 - 2f * u2 + u;
        float h01 = -2f * u3 + 3f * u2;
        float h11 = u3 - u2;
        return h00 * v0 + h10 * dt * outTan + h01 * v1 + h11 * dt * inTan;
    }

    private static Quaternion Slerp(Quaternion a, Quaternion b, float t)
    {
        float dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
        if (dot < 0f)
        {
            b = new Quaternion(-b.X, -b.Y, -b.Z, -b.W);
            dot = -dot;
        }

        if (dot > 0.9995f)
        {
            // Nearly parallel - use linear and renormalize to avoid divide-by-zero.
            float x = a.X + (b.X - a.X) * t;
            float y = a.Y + (b.Y - a.Y) * t;
            float z = a.Z + (b.Z - a.Z) * t;
            float w = a.W + (b.W - a.W) * t;
            float len = MathF.Sqrt(x * x + y * y + z * z + w * w);
            return new Quaternion(x / len, y / len, z / len, w / len);
        }

        float theta = MathF.Acos(dot);
        float sinTheta = MathF.Sin(theta);
        float s0 = MathF.Sin((1f - t) * theta) / sinTheta;
        float s1 = MathF.Sin(t * theta) / sinTheta;
        return new Quaternion(
            s0 * a.X + s1 * b.X,
            s0 * a.Y + s1 * b.Y,
            s0 * a.Z + s1 * b.Z,
            s0 * a.W + s1 * b.W);
    }
}
