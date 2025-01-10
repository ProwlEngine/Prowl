// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Globalization;

namespace Prowl.Vector;

/// <summary>
/// A structure encapsulating a four-dimensional vector (x,y,z,w),
/// which is used to efficiently rotate an object about the (x,y,z) vector by the angle theta, where w = cos(theta/2).
/// </summary>
public struct Quaternion : IEquatable<Quaternion>
{
    /// <summary>
    /// Specifies the X-value of the vector component of the Quaternion.
    /// </summary>
    public double x;
    /// <summary>
    /// Specifies the Y-value of the vector component of the Quaternion.
    /// </summary>
    public double y;
    /// <summary>
    /// Specifies the Z-value of the vector component of the Quaternion.
    /// </summary>
    public double z;
    /// <summary>
    /// Specifies the rotation component of the Quaternion.
    /// </summary>
    public double w;

    public double this[int index]
    {
        get
        {
            switch (index)
            {
                case 0: return x;
                case 1: return y;
                case 2: return z;
                case 3: return w;
                default:
                    throw new IndexOutOfRangeException("Invalid Quaternion index.");
            }
        }

        set
        {
            switch (index)
            {
                case 0: x = value; break;
                case 1: y = value; break;
                case 2: z = value; break;
                case 3: w = value; break;
                default:
                    throw new IndexOutOfRangeException("Invalid Quaternion index.");
            }
        }
    }

    public Vector3 eulerAngles { get => this.GetRotation().ToDeg().NormalizeEulerAngleDegrees(); set => this = value.NormalizeEulerAngleDegrees().ToRad().GetQuaternion(); }

    /// <summary>
    /// Returns a Quaternion representing no rotation.
    /// </summary>
    public static Quaternion identity => new(0, 0, 0, 1);

    /// <summary>
    /// Constructs a Quaternion from the given components.
    /// </summary>
    /// <param name="x">The X component of the Quaternion.</param>
    /// <param name="y">The Y component of the Quaternion.</param>
    /// <param name="z">The Z component of the Quaternion.</param>
    /// <param name="w">The W component of the Quaternion.</param>
    public Quaternion(double x, double y, double z, double w)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
    }

    /// <summary>
    /// Constructs a Quaternion from the given vector and rotation parts.
    /// </summary>
    /// <param name="vectorPart">The vector part of the Quaternion.</param>
    /// <param name="scalarPart">The rotation part of the Quaternion.</param>
    public Quaternion(Vector3 vectorPart, double scalarPart)
    {
        x = vectorPart.x;
        y = vectorPart.y;
        z = vectorPart.z;
        w = scalarPart;
    }

    public System.Numerics.Quaternion Todouble() => new((float)x, (float)y, (float)z, (float)w);

    /// <summary>
    /// Calculates the length of the Quaternion.
    /// </summary>
    /// <returns>The computed length of the Quaternion.</returns>
    public double Magnitude()
    {
        double ls = x * x + y * y + z * z + w * w;
        return Math.Sqrt(ls);
    }

    /// <summary>
    /// Calculates the length squared of the Quaternion. This operation is cheaper than Length().
    /// </summary>
    /// <returns>The length squared of the Quaternion.</returns>
    public double SqrMagnitude() => x * x + y * y + z * z + w * w;

    public static Quaternion NormalizeSafe(Quaternion q)
    {
        double mag = q.Magnitude();
        if (mag < double.Epsilon)
            return identity;
        else
            return q / mag;
    }

    /// <summary>
    /// Divides each component of the Quaternion by the length of the Quaternion.
    /// </summary>
    /// <param name="value">The source Quaternion.</param>
    /// <returns>The normalized Quaternion.</returns>
    public static Quaternion Normalize(Quaternion value)
    {
        Quaternion ans;

        double ls = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;

        double invNorm = 1.0 / Math.Sqrt(ls);

        ans.x = value.x * invNorm;
        ans.y = value.y * invNorm;
        ans.z = value.z * invNorm;
        ans.w = value.w * invNorm;

        return ans;
    }

    /// <summary>
    /// Creates the conjugate of a specified Quaternion.
    /// </summary>
    /// <param name="value">The Quaternion of which to return the conjugate.</param>
    /// <returns>A new Quaternion that is the conjugate of the specified one.</returns>
    public static Quaternion Conjugate(Quaternion value)
    {
        Quaternion ans;

        ans.x = -value.x;
        ans.y = -value.y;
        ans.z = -value.z;
        ans.w = value.w;

        return ans;
    }

    /// <summary>
    /// Returns the inverse of a Quaternion.
    /// </summary>
    /// <param name="value">The source Quaternion.</param>
    /// <returns>The inverted Quaternion.</returns>
    public static Quaternion Inverse(Quaternion value)
    {
        //  -1   (       a              -v       )
        // q   = ( -------------   ------------- )
        //       (  a^2 + |v|^2  ,  a^2 + |v|^2  )

        Quaternion ans;

        double ls = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
        double invNorm = 1.0 / ls;

        ans.x = -value.x * invNorm;
        ans.y = -value.y * invNorm;
        ans.z = -value.z * invNorm;
        ans.w = value.w * invNorm;

        return ans;
    }

    public static Quaternion Euler(double x, double y, double z) => Euler(new Vector3(x, y, z));
    public static Quaternion Euler(Vector3 euler) => euler.NormalizeEulerAngleDegrees().ToRad().GetQuaternion();
    public Vector3 ToEuler() => this.GetRotation().ToDeg().NormalizeEulerAngleDegrees();

    /// <summary>
    /// Creates a Quaternion from a normalized vector axis and an angle to rotate about the vector.
    /// </summary>
    /// <param name="axis">The unit vector to rotate around.
    /// This vector must be normalized before calling this function or the resulting Quaternion will be incorrect.</param>
    /// <param name="angle">The angle, in radians, to rotate around the vector.</param>
    /// <returns>The created Quaternion.</returns>
    public static Quaternion AngleAxis(double angle, Vector3 axis)
    {
        Quaternion ans;

        double halfAngle = angle * 0.5;
        double s = Math.Sin(halfAngle);
        double c = Math.Cos(halfAngle);

        ans.x = axis.x * s;
        ans.y = axis.y * s;
        ans.z = axis.z * s;
        ans.w = c;

        return ans;
    }

    /// <summary>
    /// Creates a new Quaternion from the given yaw, pitch, and roll, in radians.
    /// </summary>
    /// <param name="yaw">The yaw angle, in radians, around the Y-axis.</param>
    /// <param name="pitch">The pitch angle, in radians, around the X-axis.</param>
    /// <param name="roll">The roll angle, in radians, around the Z-axis.</param>
    /// <returns></returns>
    public static Quaternion CreateFromYawPitchRoll(double yaw, double pitch, double roll)
    {
        //  Roll first, about axis the object is facing, then
        //  pitch upward, then yaw to face into the new heading
        double halfRoll = roll * 0.5;
        double sr = Math.Sin(halfRoll);
        double cr = Math.Cos(halfRoll);

        double halfPitch = pitch * 0.5;
        double sp = Math.Sin(halfPitch);
        double cp = Math.Cos(halfPitch);

        double halfYaw = yaw * 0.5;
        double sy = Math.Sin(halfYaw);
        double cy = Math.Cos(halfYaw);

        Quaternion result;

        result.x = cy * sp * cr + sy * cp * sr;
        result.y = sy * cp * cr - cy * sp * sr;
        result.z = cy * cp * sr - sy * sp * cr;
        result.w = cy * cp * cr + sy * sp * sr;

        return result;
    }

    /// <summary>
    /// Creates a Quaternion from the given rotation matrix.
    /// </summary>
    /// <param name="matrix">The rotation matrix.</param>
    /// <returns>The created Quaternion.</returns>
    public static Quaternion MatrixToQuaternion(Matrix4x4 matrix)
    {
        double trace = matrix.M11 + matrix.M22 + matrix.M33;

        Quaternion q = new Quaternion();

        if (trace > 0.0)
        {
            double s = Math.Sqrt(trace + 1.0);
            q.w = s * 0.5;
            s = 0.5 / s;
            q.x = (matrix.M23 - matrix.M32) * s;
            q.y = (matrix.M31 - matrix.M13) * s;
            q.z = (matrix.M12 - matrix.M21) * s;
        }
        else
        {
            if (matrix.M11 >= matrix.M22 && matrix.M11 >= matrix.M33)
            {
                double s = Math.Sqrt(1.0 + matrix.M11 - matrix.M22 - matrix.M33);
                double invS = 0.5 / s;
                q.x = 0.5 * s;
                q.y = (matrix.M12 + matrix.M21) * invS;
                q.z = (matrix.M13 + matrix.M31) * invS;
                q.w = (matrix.M23 - matrix.M32) * invS;
            }
            else if (matrix.M22 > matrix.M33)
            {
                double s = Math.Sqrt(1.0 + matrix.M22 - matrix.M11 - matrix.M33);
                double invS = 0.5 / s;
                q.x = (matrix.M21 + matrix.M12) * invS;
                q.y = 0.5 * s;
                q.z = (matrix.M32 + matrix.M23) * invS;
                q.w = (matrix.M31 - matrix.M13) * invS;
            }
            else
            {
                double s = Math.Sqrt(1.0 + matrix.M33 - matrix.M11 - matrix.M22);
                double invS = 0.5 / s;
                q.x = (matrix.M31 + matrix.M13) * invS;
                q.y = (matrix.M32 + matrix.M23) * invS;
                q.z = 0.5 * s;
                q.w = (matrix.M12 - matrix.M21) * invS;
            }
        }

        return q;
    }

    /// <summary>
    /// Calculates the dot product of two Quaternions.
    /// </summary>
    /// <param name="quaternion1">The first source Quaternion.</param>
    /// <param name="quaternion2">The second source Quaternion.</param>
    /// <returns>The dot product of the Quaternions.</returns>
    public static double Dot(Quaternion quaternion1, Quaternion quaternion2)
    {
        return quaternion1.x * quaternion2.x +
               quaternion1.y * quaternion2.y +
               quaternion1.z * quaternion2.z +
               quaternion1.w * quaternion2.w;
    }

    /// <summary>
    /// Interpolates between two quaternions, using spherical linear interpolation.
    /// </summary>
    /// <param name="quaternion1">The first source Quaternion.</param>
    /// <param name="quaternion2">The second source Quaternion.</param>
    /// <param name="amount">The relative weight of the second source Quaternion in the interpolation.</param>
    /// <returns>The interpolated Quaternion.</returns>
    public static Quaternion Slerp(Quaternion quaternion1, Quaternion quaternion2, double amount)
    {
        double t = amount;

        double cosOmega = quaternion1.x * quaternion2.x + quaternion1.y * quaternion2.y +
                          quaternion1.z * quaternion2.z + quaternion1.w * quaternion2.w;

        bool flip = false;

        if (cosOmega < 0.0)
        {
            flip = true;
            cosOmega = -cosOmega;
        }

        double s1, s2;

        if (cosOmega > (1.0 - double.Epsilon))
        {
            // Too close, do straight linear interpolation.
            s1 = 1.0 - t;
            s2 = (flip) ? -t : t;
        }
        else
        {
            double omega = Math.Acos(cosOmega);
            double invSinOmega = 1 / Math.Sin(omega);

            s1 = Math.Sin((1.0 - t) * omega) * invSinOmega;
            s2 = (flip)
                ? -Math.Sin(t * omega) * invSinOmega
                : Math.Sin(t * omega) * invSinOmega;
        }

        Quaternion ans;

        ans.x = s1 * quaternion1.x + s2 * quaternion2.x;
        ans.y = s1 * quaternion1.y + s2 * quaternion2.y;
        ans.z = s1 * quaternion1.z + s2 * quaternion2.z;
        ans.w = s1 * quaternion1.w + s2 * quaternion2.w;

        return ans;
    }

    /// <summary>
    ///  Linearly interpolates between two quaternions.
    /// </summary>
    /// <param name="quaternion1">The first source Quaternion.</param>
    /// <param name="quaternion2">The second source Quaternion.</param>
    /// <param name="amount">The relative weight of the second source Quaternion in the interpolation.</param>
    /// <returns>The interpolated Quaternion.</returns>
    public static Quaternion Lerp(Quaternion quaternion1, Quaternion quaternion2, double amount)
    {
        double t = amount;
        double t1 = 1.0 - t;

        Quaternion r = new Quaternion();

        double dot = quaternion1.x * quaternion2.x + quaternion1.y * quaternion2.y +
                     quaternion1.z * quaternion2.z + quaternion1.w * quaternion2.w;

        if (dot >= 0.0)
        {
            r.x = t1 * quaternion1.x + t * quaternion2.x;
            r.y = t1 * quaternion1.y + t * quaternion2.y;
            r.z = t1 * quaternion1.z + t * quaternion2.z;
            r.w = t1 * quaternion1.w + t * quaternion2.w;
        }
        else
        {
            r.x = t1 * quaternion1.x - t * quaternion2.x;
            r.y = t1 * quaternion1.y - t * quaternion2.y;
            r.z = t1 * quaternion1.z - t * quaternion2.z;
            r.w = t1 * quaternion1.w - t * quaternion2.w;
        }

        // Normalize it.
        double ls = r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w;
        double invNorm = 1.0 / Math.Sqrt(ls);

        r.x *= invNorm;
        r.y *= invNorm;
        r.z *= invNorm;
        r.w *= invNorm;

        return r;
    }

    /// <summary>
    /// Returns the angle in degrees between two rotations.
    /// </summary>
    public static double Angle(Quaternion a, Quaternion b) => MathD.Acos(MathD.Min(MathD.Abs(Dot(a, b)), 1.0)) * 2.0 * MathD.Rad2Deg;

    public static Quaternion RotateTowards(Quaternion from, Quaternion to, double maxDegreesDelta)
    {
        double angle = Angle(from, to);
        return angle == 0.0 ? to : Slerp(from, to, MathD.Min(1.0, maxDegreesDelta / angle));
    }

    /// <summary>
    /// Concatenates two Quaternions; the result represents the value1 rotation followed by the value2 rotation.
    /// </summary>
    /// <param name="value1">The first Quaternion rotation in the series.</param>
    /// <param name="value2">The second Quaternion rotation in the series.</param>
    /// <returns>A new Quaternion representing the concatenation of the value1 rotation followed by the value2 rotation.</returns>
    public static Quaternion Concatenate(Quaternion value1, Quaternion value2)
    {
        Quaternion ans;

        // Concatenate rotation is actually q2 * q1 instead of q1 * q2.
        // So that's why value2 goes q1 and value1 goes q2.
        double q1x = value2.x;
        double q1y = value2.y;
        double q1z = value2.z;
        double q1w = value2.w;

        double q2x = value1.x;
        double q2y = value1.y;
        double q2z = value1.z;
        double q2w = value1.w;

        // cross(av, bv)
        double cx = q1y * q2z - q1z * q2y;
        double cy = q1z * q2x - q1x * q2z;
        double cz = q1x * q2y - q1y * q2x;

        double dot = q1x * q2x + q1y * q2y + q1z * q2z;

        ans.x = q1x * q2w + q2x * q1w + cx;
        ans.y = q1y * q2w + q2y * q1w + cy;
        ans.z = q1z * q2w + q2z * q1w + cz;
        ans.w = q1w * q2w - dot;

        return ans;
    }

    /// <summary>
    /// Flips the sign of each component of the quaternion.
    /// </summary>
    /// <param name="value">The source Quaternion.</param>
    /// <returns>The negated Quaternion.</returns>
    public static Quaternion Negate(Quaternion value)
    {
        Quaternion ans;

        ans.x = -value.x;
        ans.y = -value.y;
        ans.z = -value.z;
        ans.w = -value.w;

        return ans;
    }

    /// <summary>
    /// Adds two Quaternions element-by-element.
    /// </summary>
    /// <param name="value1">The first source Quaternion.</param>
    /// <param name="value2">The second source Quaternion.</param>
    /// <returns>The result of adding the Quaternions.</returns>
    public static Quaternion Add(Quaternion value1, Quaternion value2)
    {
        Quaternion ans;

        ans.x = value1.x + value2.x;
        ans.y = value1.y + value2.y;
        ans.z = value1.z + value2.z;
        ans.w = value1.w + value2.w;

        return ans;
    }

    /// <summary>
    /// Subtracts one Quaternion from another.
    /// </summary>
    /// <param name="value1">The first source Quaternion.</param>
    /// <param name="value2">The second Quaternion, to be subtracted from the first.</param>
    /// <returns>The result of the subtraction.</returns>
    public static Quaternion Subtract(Quaternion value1, Quaternion value2)
    {
        Quaternion ans;

        ans.x = value1.x - value2.x;
        ans.y = value1.y - value2.y;
        ans.z = value1.z - value2.z;
        ans.w = value1.w - value2.w;

        return ans;
    }

    /// <summary>
    /// Multiplies two Quaternions together.
    /// </summary>
    /// <param name="value1">The Quaternion on the left side of the multiplication.</param>
    /// <param name="value2">The Quaternion on the right side of the multiplication.</param>
    /// <returns>The result of the multiplication.</returns>
    public static Quaternion Multiply(Quaternion value1, Quaternion value2)
    {
        Quaternion ans;

        double q1x = value1.x;
        double q1y = value1.y;
        double q1z = value1.z;
        double q1w = value1.w;

        double q2x = value2.x;
        double q2y = value2.y;
        double q2z = value2.z;
        double q2w = value2.w;

        // cross(av, bv)
        double cx = q1y * q2z - q1z * q2y;
        double cy = q1z * q2x - q1x * q2z;
        double cz = q1x * q2y - q1y * q2x;

        double dot = q1x * q2x + q1y * q2y + q1z * q2z;

        ans.x = q1x * q2w + q2x * q1w + cx;
        ans.y = q1y * q2w + q2y * q1w + cy;
        ans.z = q1z * q2w + q2z * q1w + cz;
        ans.w = q1w * q2w - dot;

        return ans;
    }

    /// <summary>
    /// Multiplies a Quaternion by a scalar value.
    /// </summary>
    /// <param name="value1">The source Quaternion.</param>
    /// <param name="value2">The scalar value.</param>
    /// <returns>The result of the multiplication.</returns>
    public static Quaternion Multiply(Quaternion value1, double value2)
    {
        Quaternion ans;

        ans.x = value1.x * value2;
        ans.y = value1.y * value2;
        ans.z = value1.z * value2;
        ans.w = value1.w * value2;

        return ans;
    }

    /// <summary>
    /// Divides a Quaternion by another Quaternion.
    /// </summary>
    /// <param name="value1">The source Quaternion.</param>
    /// <param name="value2">The divisor.</param>
    /// <returns>The result of the division.</returns>
    public static Quaternion Divide(Quaternion value1, Quaternion value2)
    {
        Quaternion ans;

        double q1x = value1.x;
        double q1y = value1.y;
        double q1z = value1.z;
        double q1w = value1.w;

        //-------------------------------------
        // Inverse part.
        double ls = value2.x * value2.x + value2.y * value2.y +
                    value2.z * value2.z + value2.w * value2.w;
        double invNorm = 1.0 / ls;

        double q2x = -value2.x * invNorm;
        double q2y = -value2.y * invNorm;
        double q2z = -value2.z * invNorm;
        double q2w = value2.w * invNorm;

        //-------------------------------------
        // Multiply part.

        // cross(av, bv)
        double cx = q1y * q2z - q1z * q2y;
        double cy = q1z * q2x - q1x * q2z;
        double cz = q1x * q2y - q1y * q2x;

        double dot = q1x * q2x + q1y * q2y + q1z * q2z;

        ans.x = q1x * q2w + q2x * q1w + cx;
        ans.y = q1y * q2w + q2y * q1w + cy;
        ans.z = q1z * q2w + q2z * q1w + cz;
        ans.w = q1w * q2w - dot;

        return ans;
    }

    /// <summary>
    /// Flips the sign of each component of the quaternion.
    /// </summary>
    /// <param name="value">The source Quaternion.</param>
    /// <returns>The negated Quaternion.</returns>
    public static Quaternion operator -(Quaternion value)
    {
        Quaternion ans;

        ans.x = -value.x;
        ans.y = -value.y;
        ans.z = -value.z;
        ans.w = -value.w;

        return ans;
    }

    /// <summary>
    /// Adds two Quaternions element-by-element.
    /// </summary>
    /// <param name="value1">The first source Quaternion.</param>
    /// <param name="value2">The second source Quaternion.</param>
    /// <returns>The result of adding the Quaternions.</returns>
    public static Quaternion operator +(Quaternion value1, Quaternion value2)
    {
        Quaternion ans;

        ans.x = value1.x + value2.x;
        ans.y = value1.y + value2.y;
        ans.z = value1.z + value2.z;
        ans.w = value1.w + value2.w;

        return ans;
    }

    /// <summary>
    /// Subtracts one Quaternion from another.
    /// </summary>
    /// <param name="value1">The first source Quaternion.</param>
    /// <param name="value2">The second Quaternion, to be subtracted from the first.</param>
    /// <returns>The result of the subtraction.</returns>
    public static Quaternion operator -(Quaternion value1, Quaternion value2)
    {
        Quaternion ans;

        ans.x = value1.x - value2.x;
        ans.y = value1.y - value2.y;
        ans.z = value1.z - value2.z;
        ans.w = value1.w - value2.w;

        return ans;
    }

    /// <summary>
    /// Multiplies two Quaternions together.
    /// </summary>
    /// <param name="value1">The Quaternion on the left side of the multiplication.</param>
    /// <param name="value2">The Quaternion on the right side of the multiplication.</param>
    /// <returns>The result of the multiplication.</returns>
    public static Quaternion operator *(Quaternion value1, Quaternion value2)
    {
        Quaternion ans;

        double q1x = value1.x;
        double q1y = value1.y;
        double q1z = value1.z;
        double q1w = value1.w;

        double q2x = value2.x;
        double q2y = value2.y;
        double q2z = value2.z;
        double q2w = value2.w;

        // cross(av, bv)
        double cx = q1y * q2z - q1z * q2y;
        double cy = q1z * q2x - q1x * q2z;
        double cz = q1x * q2y - q1y * q2x;

        double dot = q1x * q2x + q1y * q2y + q1z * q2z;

        ans.x = q1x * q2w + q2x * q1w + cx;
        ans.y = q1y * q2w + q2y * q1w + cy;
        ans.z = q1z * q2w + q2z * q1w + cz;
        ans.w = q1w * q2w - dot;

        return ans;
    }

    /// <summary>
    /// Multiplies a Quaternion by a scalar value.
    /// </summary>
    /// <param name="value1">The source Quaternion.</param>
    /// <param name="value2">The scalar value.</param>
    /// <returns>The result of the multiplication.</returns>
    public static Quaternion operator *(Quaternion value1, double value2)
    {
        Quaternion ans;

        ans.x = value1.x * value2;
        ans.y = value1.y * value2;
        ans.z = value1.z * value2;
        ans.w = value1.w * value2;

        return ans;
    }

    public static Vector3 operator *(Quaternion rotation, Vector3 point)
    {
        double x = rotation.x * 2.0;
        double y = rotation.y * 2.0;
        double z = rotation.z * 2.0;
        double xx = rotation.x * x;
        double yy = rotation.y * y;
        double zz = rotation.z * z;
        double xy = rotation.x * y;
        double xz = rotation.x * z;
        double yz = rotation.y * z;
        double wx = rotation.w * x;
        double wy = rotation.w * y;
        double wz = rotation.w * z;

        Vector3 res;
        res.x = (1.0 - (yy + zz)) * point.x + (xy - wz) * point.y + (xz + wy) * point.z;
        res.y = (xy + wz) * point.x + (1.0 - (xx + zz)) * point.y + (yz - wx) * point.z;
        res.z = (xz - wy) * point.x + (yz + wx) * point.y + (1.0 - (xx + yy)) * point.z;
        return res;
    }

    /// <summary>
    /// Divides a Quaternion by another Quaternion.
    /// </summary>
    /// <param name="value1">The source Quaternion.</param>
    /// <param name="value2">The divisor.</param>
    /// <returns>The result of the division.</returns>
    public static Quaternion operator /(Quaternion value1, Quaternion value2)
    {
        Quaternion ans;

        double q1x = value1.x;
        double q1y = value1.y;
        double q1z = value1.z;
        double q1w = value1.w;

        //-------------------------------------
        // Inverse part.
        double ls = value2.x * value2.x + value2.y * value2.y +
                    value2.z * value2.z + value2.w * value2.w;
        double invNorm = 1.0 / ls;

        double q2x = -value2.x * invNorm;
        double q2y = -value2.y * invNorm;
        double q2z = -value2.z * invNorm;
        double q2w = value2.w * invNorm;

        //-------------------------------------
        // Multiply part.

        // cross(av, bv)
        double cx = q1y * q2z - q1z * q2y;
        double cy = q1z * q2x - q1x * q2z;
        double cz = q1x * q2y - q1y * q2x;

        double dot = q1x * q2x + q1y * q2y + q1z * q2z;

        ans.x = q1x * q2w + q2x * q1w + cx;
        ans.y = q1y * q2w + q2y * q1w + cy;
        ans.z = q1z * q2w + q2z * q1w + cz;
        ans.w = q1w * q2w - dot;

        return ans;
    }

    public static Quaternion operator /(Quaternion q, double v) => new(q.x / v, q.y / v, q.z / v, q.w / v);

    /// <summary>
    /// Returns a boolean indicating whether the two given Quaternions are equal.
    /// </summary>
    /// <param name="value1">The first Quaternion to compare.</param>
    /// <param name="value2">The second Quaternion to compare.</param>
    /// <returns>True if the Quaternions are equal; False otherwise.</returns>
    public static bool operator ==(Quaternion value1, Quaternion value2) => value1.Equals(value2);

    /// <summary>
    /// Returns a boolean indicating whether the two given Quaternions are not equal.
    /// </summary>
    /// <param name="value1">The first Quaternion to compare.</param>
    /// <param name="value2">The second Quaternion to compare.</param>
    /// <returns>True if the Quaternions are not equal; False if they are equal.</returns>
    public static bool operator !=(Quaternion value1, Quaternion value2) => !value1.Equals(value2);

    public static implicit operator System.Numerics.Quaternion(Quaternion value) => new((float)value.x, (float)value.y, (float)value.z, (float)value.w);

    public static implicit operator Quaternion(System.Numerics.Quaternion value) => new(value.X, value.Y, value.Z, value.W);

    /// <summary>
    /// Returns a boolean indicating whether the given Quaternion is equal to this Quaternion instance.
    /// </summary>
    /// <param name="other">The Quaternion to compare this instance to.</param>
    /// <returns>True if the other Quaternion is equal to this instance; False otherwise.</returns>
    public bool Equals(Quaternion other) =>
        (MathD.ApproximatelyEquals(x, other.x) &&
         MathD.ApproximatelyEquals(y, other.y) &&
         MathD.ApproximatelyEquals(z, other.z) &&
         MathD.ApproximatelyEquals(w, other.w));

    /// <summary>
    /// Returns a boolean indicating whether the given Object is equal to this Quaternion instance.
    /// </summary>
    /// <param name="obj">The Object to compare against.</param>
    /// <returns>True if the Object is equal to this Quaternion; False otherwise.</returns>
    public override bool Equals(object? obj) => obj is Quaternion quaternion && Equals(quaternion);

    /// <summary>
    /// Returns a String representing this Quaternion instance.
    /// </summary>
    /// <returns>The string representation.</returns>
    public override string ToString()
    {
        CultureInfo ci = CultureInfo.CurrentCulture;

        return string.Format(ci, "{{X:{0} Y:{1} Z:{2} W:{3}}}", x.ToString(ci), y.ToString(ci), z.ToString(ci), w.ToString(ci));
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => x.GetHashCode() + y.GetHashCode() + z.GetHashCode() + w.GetHashCode();
}
