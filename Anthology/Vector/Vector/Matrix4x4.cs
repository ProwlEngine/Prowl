// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Prowl.Vector;

/// <summary>
/// A structure encapsulating a 4x4 matrix.
/// </summary>
public struct Matrix4x4 : IEquatable<Matrix4x4>
{
    #region Public Fields
    public double M11;
    public double M12;
    public double M13;
    public double M14;

    public double M21;
    public double M22;
    public double M23;
    public double M24;

    public double M31;
    public double M32;
    public double M33;
    public double M34;

    public double M41;
    public double M42;
    public double M43;
    public double M44;
    #endregion Public Fields

    public Vector4 M1
    {
        get => new Vector4(M11, M12, M13, M14); set { M11 = value.x; M12 = value.y; M13 = value.z; M14 = value.w; }
    }

    public Vector4 M2
    {
        get => new Vector4(M21, M22, M23, M24); set { M21 = value.x; M22 = value.y; M23 = value.z; M24 = value.w; }
    }

    public Vector4 M3
    {
        get => new Vector4(M31, M32, M33, M34); set { M31 = value.x; M32 = value.y; M33 = value.z; M34 = value.w; }
    }

    public Vector4 M4
    {
        get => new Vector4(M41, M42, M43, M44); set { M41 = value.x; M42 = value.y; M43 = value.z; M44 = value.w; }
    }

    private static readonly Matrix4x4 _identity = new Matrix4x4
    (
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1
    );

    /// <summary>
    /// Returns the multiplicative identity matrix.
    /// </summary>
    public static Matrix4x4 Identity => _identity;

    /// <summary>
    /// Returns whether the matrix is the identity matrix.
    /// </summary>
    public bool IsIdentity => MathD.ApproximatelyEquals(M11, 1) && MathD.ApproximatelyEquals(M22, 1) && MathD.ApproximatelyEquals(M33, 1) && MathD.ApproximatelyEquals(M44, 1) && // Check diagonal element first for early out.
                              M12 == 0 && M13 == 0 && M14 == 0 &&
                              M21 == 0 && M23 == 0 && M24 == 0 &&
                              M31 == 0 && M32 == 0 && M34 == 0 &&
                              M41 == 0 && M42 == 0 && M43 == 0;

    // Access element at [row, column].
    public double this[int row, int column]
    {
        get => this[row + column * 4];

        set => this[row + column * 4] = value;
    }

    // Access element at sequential index (0..15 inclusive).
    public double this[int index]
    {
        get => index switch
        {
            0 => M11,
            1 => M21,
            2 => M31,
            3 => M41,
            4 => M12,
            5 => M22,
            6 => M32,
            7 => M42,
            8 => M13,
            9 => M23,
            10 => M33,
            11 => M43,
            12 => M14,
            13 => M24,
            14 => M34,
            15 => M44,
            _ => throw new IndexOutOfRangeException("Invalid matrix index."),
        };

        set
        {
            switch (index)
            {
                case 0: M11 = value; break;
                case 1: M21 = value; break;
                case 2: M31 = value; break;
                case 3: M41 = value; break;
                case 4: M12 = value; break;
                case 5: M22 = value; break;
                case 6: M32 = value; break;
                case 7: M42 = value; break;
                case 8: M13 = value; break;
                case 9: M23 = value; break;
                case 10: M33 = value; break;
                case 11: M43 = value; break;
                case 12: M14 = value; break;
                case 13: M24 = value; break;
                case 14: M34 = value; break;
                case 15: M44 = value; break;

                default:
                    throw new IndexOutOfRangeException("Invalid matrix index.");
            }
        }
    }

    /// <summary>
    /// Gets or sets the translation component of this matrix.
    /// </summary>
    public Vector3 Translation
    {
        get => new Vector3(M41, M42, M43);
        set
        {
            M41 = value.x;
            M42 = value.y;
            M43 = value.z;
        }
    }

    /// <summary>
    /// Constructs a Matrix4x4 from the given components.
    /// </summary>
    public Matrix4x4(double m11, double m12, double m13, double m14,
        double m21, double m22, double m23, double m24,
        double m31, double m32, double m33, double m34,
        double m41, double m42, double m43, double m44)
    {
        M11 = m11;
        M12 = m12;
        M13 = m13;
        M14 = m14;
        M21 = m21;
        M22 = m22;
        M23 = m23;
        M24 = m24;
        M31 = m31;
        M32 = m32;
        M33 = m33;
        M34 = m34;
        M41 = m41;
        M42 = m42;
        M43 = m43;
        M44 = m44;
    }

    public Matrix4x4(Vector4 row1, Vector4 row2, Vector4 row3, Vector4 row4)
    {
        M11 = row1.x;
        M12 = row1.y;
        M13 = row1.z;
        M14 = row1.w;
        M21 = row2.x;
        M22 = row2.y;
        M23 = row2.z;
        M24 = row2.w;
        M31 = row3.x;
        M32 = row3.y;
        M33 = row3.z;
        M34 = row3.w;
        M41 = row4.x;
        M42 = row4.y;
        M43 = row4.z;
        M44 = row4.w;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public System.Numerics.Matrix4x4 ToFloat()
    {
        System.Numerics.Matrix4x4 result;
        result.M11 = (float)M11;
        result.M12 = (float)M12;
        result.M13 = (float)M13;
        result.M14 = (float)M14;

        result.M21 = (float)M21;
        result.M22 = (float)M22;
        result.M23 = (float)M23;
        result.M24 = (float)M24;

        result.M31 = (float)M31;
        result.M32 = (float)M32;
        result.M33 = (float)M33;
        result.M34 = (float)M34;

        result.M41 = (float)M41;
        result.M42 = (float)M42;
        result.M43 = (float)M43;
        result.M44 = (float)M44;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix4x4 FromFloat(System.Numerics.Matrix4x4 floatMatrix)
    {
        Matrix4x4 result;
        result.M11 = floatMatrix.M11;
        result.M12 = floatMatrix.M12;
        result.M13 = floatMatrix.M13;
        result.M14 = floatMatrix.M14;

        result.M21 = floatMatrix.M21;
        result.M22 = floatMatrix.M22;
        result.M23 = floatMatrix.M23;
        result.M24 = floatMatrix.M24;

        result.M31 = floatMatrix.M31;
        result.M32 = floatMatrix.M32;
        result.M33 = floatMatrix.M33;
        result.M34 = floatMatrix.M34;

        result.M41 = floatMatrix.M41;
        result.M42 = floatMatrix.M42;
        result.M43 = floatMatrix.M43;
        result.M44 = floatMatrix.M44;
        return result;
    }

    public static Matrix4x4 TRS(Vector3 m_LocalPosition, Quaternion m_LocalRotation, Vector3 m_LocalScale)
        => CreateScale(m_LocalScale) * CreateFromQuaternion(m_LocalRotation) * CreateTranslation(m_LocalPosition);

    public Vector3 MultiplyPoint(Vector3 v) => Vector3.Transform(v, this);


    /// <summary>
    /// Creates a spherical billboard that rotates around a specified object position.
    /// </summary>
    /// <param name="objectPosition">Position of the object the billboard will rotate around.</param>
    /// <param name="cameraPosition">Position of the camera.</param>
    /// <param name="cameraUpVector">The up vector of the camera.</param>
    /// <param name="cameraForwardVector">The forward vector of the camera.</param>
    /// <returns>The created billboard matrix</returns>
    public static Matrix4x4 CreateBillboard(Vector3 objectPosition, Vector3 cameraPosition, Vector3 cameraUpVector, Vector3 cameraForwardVector)
    {
        Vector3 zaxis = new Vector3(
            objectPosition.x - cameraPosition.x,
            objectPosition.y - cameraPosition.y,
            objectPosition.z - cameraPosition.z);

        double norm = zaxis.sqrMagnitude;

        if (norm < double.Epsilon)
        {
            zaxis = -cameraForwardVector;
        }
        else
        {
            zaxis = zaxis * (1.0 / Math.Sqrt(norm));
        }

        Vector3 xaxis = Vector3.Normalize(Vector3.Cross(cameraUpVector, zaxis));
        Vector3 yaxis = Vector3.Cross(zaxis, xaxis);

        Matrix4x4 result = new();
        result.M1 = new(xaxis.x, xaxis.y, xaxis.z, 0.0);
        result.M2 = new(yaxis.x, yaxis.y, yaxis.z, 0.0);
        result.M3 = new(zaxis.x, zaxis.y, zaxis.z, 0.0);
        result.M4 = new(objectPosition.x, objectPosition.y, objectPosition.z, 1.0);
        return result;
    }

    /// <summary>
    /// Creates a cylindrical billboard that rotates around a specified axis.
    /// </summary>
    /// <param name="objectPosition">Position of the object the billboard will rotate around.</param>
    /// <param name="cameraPosition">Position of the camera.</param>
    /// <param name="rotateAxis">Axis to rotate the billboard around.</param>
    /// <param name="cameraForwardVector">Forward vector of the camera.</param>
    /// <param name="objectForwardVector">Forward vector of the object.</param>
    /// <returns>The created billboard matrix.</returns>
    public static Matrix4x4 CreateConstrainedBillboard(Vector3 objectPosition, Vector3 cameraPosition, Vector3 rotateAxis, Vector3 cameraForwardVector, Vector3 objectForwardVector)
    {
        const double minAngle = 1.0 - (0.1 * (Math.PI / 180.0)); // 0.1 degrees

        // Treat the case when object and camera positions are too close.
        Vector3 faceDir = new Vector3(
            objectPosition.x - cameraPosition.x,
            objectPosition.y - cameraPosition.y,
            objectPosition.z - cameraPosition.z);

        double norm = faceDir.sqrMagnitude;

        if (norm < double.Epsilon)
        {
            faceDir = -cameraForwardVector;
        }
        else
        {
            faceDir = faceDir * ((1.0 / Math.Sqrt(norm)));
        }

        Vector3 yaxis = rotateAxis;
        Vector3 xaxis;
        Vector3 zaxis;

        // Treat the case when angle between faceDir and rotateAxis is too close to 0.
        double dot = Vector3.Dot(rotateAxis, faceDir);

        if (Math.Abs(dot) > minAngle)
        {
            zaxis = objectForwardVector;

            // Make sure passed values are useful for compute.
            dot = Vector3.Dot(rotateAxis, zaxis);

            if (Math.Abs(dot) > minAngle)
            {
                zaxis = (Math.Abs(rotateAxis.z) > minAngle) ? new Vector3(1, 0, 0) : new Vector3(0, 0, -1);
            }

            xaxis = Vector3.Normalize(Vector3.Cross(rotateAxis, zaxis));
            zaxis = Vector3.Normalize(Vector3.Cross(xaxis, rotateAxis));
        }
        else
        {
            xaxis = Vector3.Normalize(Vector3.Cross(rotateAxis, faceDir));
            zaxis = Vector3.Normalize(Vector3.Cross(xaxis, yaxis));
        }

        Matrix4x4 result = new();
        result.M1 = new(xaxis.x, xaxis.y, xaxis.z, 0.0);
        result.M2 = new(yaxis.x, yaxis.y, yaxis.z, 0.0);
        result.M3 = new(zaxis.x, zaxis.y, zaxis.z, 0.0);
        result.M4 = new(objectPosition.x, objectPosition.y, objectPosition.z, 1.0);

        return result;
    }

    /// <summary>
    /// Creates a translation matrix.
    /// </summary>
    /// <param name="position">The amount to translate in each axis.</param>
    /// <returns>The translation matrix.</returns>
    public static Matrix4x4 CreateTranslation(Vector3 position)
    {
        Matrix4x4 result = new();
        result.M1 = new(1f, 0, 0, 0);
        result.M2 = new(0, 1f, 0, 0);
        result.M3 = new(0, 0, 1f, 0);
        result.M4 = new(position.x, position.y, position.z, 1f);
        return result;
    }

    /// <summary>
    /// Creates a translation matrix.
    /// </summary>
    /// <param name="xPosition">The amount to translate on the X-axis.</param>
    /// <param name="yPosition">The amount to translate on the Y-axis.</param>
    /// <param name="zPosition">The amount to translate on the Z-axis.</param>
    /// <returns>The translation matrix.</returns>
    public static Matrix4x4 CreateTranslation(double xPosition, double yPosition, double zPosition)
    {
        Matrix4x4 result = new();

        result.M1 = new(1f, 0, 0, 0);
        result.M2 = new(0, 1f, 0, 0);
        result.M3 = new(0, 0, 1f, 0);
        result.M4 = new(xPosition, yPosition, zPosition, 1f);

        return result;
    }

    /// <summary>
    /// Creates a scaling matrix.
    /// </summary>
    /// <param name="xScale">Value to scale by on the X-axis.</param>
    /// <param name="yScale">Value to scale by on the Y-axis.</param>
    /// <param name="zScale">Value to scale by on the Z-axis.</param>
    /// <returns>The scaling matrix.</returns>
    public static Matrix4x4 CreateScale(double xScale, double yScale, double zScale)
    {
        Matrix4x4 result = new();
        result.M1 = new(xScale, 0, 0, 0);
        result.M2 = new(0, yScale, 0, 0);
        result.M3 = new(0, 0, zScale, 0);
        result.M4 = new(0, 0, 0, 1);
        return result;
    }

    /// <summary>
    /// Creates a scaling matrix with a center point.
    /// </summary>
    /// <param name="xScale">Value to scale by on the X-axis.</param>
    /// <param name="yScale">Value to scale by on the Y-axis.</param>
    /// <param name="zScale">Value to scale by on the Z-axis.</param>
    /// <param name="centerPoint">The center point.</param>
    /// <returns>The scaling matrix.</returns>
    public static Matrix4x4 CreateScale(double xScale, double yScale, double zScale, Vector3 centerPoint)
    {
        Matrix4x4 result = new();

        double tx = centerPoint.x * (1 - xScale);
        double ty = centerPoint.y * (1 - yScale);
        double tz = centerPoint.z * (1 - zScale);

        result.M1 = new(xScale, 0, 0, 0);
        result.M2 = new(0, yScale, 0, 0);
        result.M3 = new(0, 0, zScale, 0);
        result.M4 = new(tx, ty, tz, 1);

        return result;
    }

    /// <summary>f
    /// Creates a scaling matrix.
    /// </summary>
    /// <param name="scales">The vector containing the amount to scale by on each axis.</param>
    /// <returns>The scaling matrix.</returns>
    public static Matrix4x4 CreateScale(Vector3 scales)
    {
        Matrix4x4 result = new();
        result.M1 = new(scales.x, 0, 0, 0);
        result.M2 = new(0, scales.y, 0, 0);
        result.M3 = new(0, 0, scales.z, 0);
        result.M4 = new(0, 0, 0, 1);
        return result;
    }

    /// <summary>
    /// Creates a scaling matrix with a center point.
    /// </summary>
    /// <param name="scales">The vector containing the amount to scale by on each axis.</param>
    /// <param name="centerPoint">The center point.</param>
    /// <returns>The scaling matrix.</returns>
    public static Matrix4x4 CreateScale(Vector3 scales, Vector3 centerPoint)
    {
        Matrix4x4 result = new();

        double tx = centerPoint.x * (1 - scales.x);
        double ty = centerPoint.y * (1 - scales.y);
        double tz = centerPoint.z * (1 - scales.z);

        result.M1 = new(scales.x, 0, 0, 0);
        result.M2 = new(0, scales.y, 0, 0);
        result.M3 = new(0, 0, scales.z, 0);
        result.M4 = new(tx, ty, tz, 1);

        return result;
    }

    /// <summary>
    /// Creates a uniform scaling matrix that scales equally on each axis.
    /// </summary>
    /// <param name="scale">The uniform scaling factor.</param>
    /// <returns>The scaling matrix.</returns>
    public static Matrix4x4 CreateScale(double scale)
    {
        Matrix4x4 result = new();
        result.M1 = new(scale, 0, 0, 0);
        result.M2 = new(0, scale, 0, 0);
        result.M3 = new(0, 0, scale, 0);
        result.M4 = new(0, 0, 0, 1);

        return result;
    }

    /// <summary>
    /// Creates a uniform scaling matrix that scales equally on each axis with a center point.
    /// </summary>
    /// <param name="scale">The uniform scaling factor.</param>
    /// <param name="centerPoint">The center point.</param>
    /// <returns>The scaling matrix.</returns>
    public static Matrix4x4 CreateScale(double scale, Vector3 centerPoint)
    {
        Matrix4x4 result = new();
        double tx = centerPoint.x * (1 - scale);
        double ty = centerPoint.y * (1 - scale);
        double tz = centerPoint.z * (1 - scale);
        result.M1 = new(scale, 0, 0, 0);
        result.M2 = new(0, scale, 0, 0);
        result.M3 = new(0, 0, scale, 0);
        result.M4 = new(tx, ty, tz, 1);
        return result;
    }

    /// <summary>
    /// Creates a matrix for rotating points around the X-axis.
    /// </summary>
    /// <param name="radians">The amount, in radians, by which to rotate around the X-axis.</param>
    /// <returns>The rotation matrix.</returns>
    public static Matrix4x4 CreateRotationX(double radians)
    {
        Matrix4x4 result = new();

        double c = Math.Cos(radians);
        double s = Math.Sin(radians);

        // [  1  0  0  0 ]
        // [  0  c  s  0 ]
        // [  0 -s  c  0 ]
        // [  0  0  0  1 ]
        result.M1 = new Vector4(1.0, 0.0, 0.0, 0.0);
        result.M2 = new Vector4(0.0, c, s, 0.0);
        result.M3 = new Vector4(0.0, -s, c, 0.0);
        result.M4 = new Vector4(0.0, 0.0, 0.0, 1.0);

        return result;
    }

    /// <summary>
    /// Creates a matrix for rotating points around the X-axis, from a center point.
    /// </summary>
    /// <param name="radians">The amount, in radians, by which to rotate around the X-axis.</param>
    /// <param name="centerPoint">The center point.</param>
    /// <returns>The rotation matrix.</returns>
    public static Matrix4x4 CreateRotationX(double radians, Vector3 centerPoint)
    {
        Matrix4x4 result = new();

        double c = Math.Cos(radians);
        double s = Math.Sin(radians);

        double y = centerPoint.y * (1 - c) + centerPoint.z * s;
        double z = centerPoint.z * (1 - c) - centerPoint.y * s;

        // [  1  0  0  0 ]
        // [  0  c  s  0 ]
        // [  0 -s  c  0 ]
        // [  0  y  z  1 ]
        result.M1 = new Vector4(1.0, 0.0, 0.0, 0.0);
        result.M2 = new Vector4(0.0, c, s, 0.0);
        result.M3 = new Vector4(0.0, -s, c, 0.0);
        result.M4 = new Vector4(0.0, y, z, 1.0);

        return result;
    }

    /// <summary>
    /// Creates a matrix for rotating points around the Y-axis.
    /// </summary>
    /// <param name="radians">The amount, in radians, by which to rotate around the Y-axis.</param>
    /// <returns>The rotation matrix.</returns>
    public static Matrix4x4 CreateRotationY(double radians)
    {
        Matrix4x4 result = new();

        double c = Math.Cos(radians);
        double s = Math.Sin(radians);

        // [  c  0 -s  0 ]
        // [  0  1  0  0 ]
        // [  s  0  c  0 ]
        // [  0  0  0  1 ]
        result.M1 = new Vector4(c, 0.0, -s, 0.0);
        result.M2 = new Vector4(0.0, 1.0, 0.0, 0.0);
        result.M3 = new Vector4(s, 0.0, c, 0.0);
        result.M4 = new Vector4(0.0, 0.0, 0.0, 1.0);

        return result;
    }

    /// <summary>
    /// Creates a matrix for rotating points around the Y-axis, from a center point.
    /// </summary>
    /// <param name="radians">The amount, in radians, by which to rotate around the Y-axis.</param>
    /// <param name="centerPoint">The center point.</param>
    /// <returns>The rotation matrix.</returns>
    public static Matrix4x4 CreateRotationY(double radians, Vector3 centerPoint)
    {
        Matrix4x4 result = new();

        double c = Math.Cos(radians);
        double s = Math.Sin(radians);

        double x = centerPoint.x * (1 - c) - centerPoint.z * s;
        double z = centerPoint.z * (1 - c) + centerPoint.x * s;

        // [  c  0 -s  0 ]
        // [  0  1  0  0 ]
        // [  s  0  c  0 ]
        // [  x  0  z  1 ]
        result.M1 = new Vector4(c, 0.0, -s, 0.0);
        result.M2 = new Vector4(0.0, 1.0, 0.0, 0.0);
        result.M3 = new Vector4(s, 0.0, c, 0.0);
        result.M4 = new Vector4(x, 0.0, z, 1.0);

        return result;
    }

    /// <summary>
    /// Creates a matrix for rotating points around the Z-axis.
    /// </summary>
    /// <param name="radians">The amount, in radians, by which to rotate around the Z-axis.</param>
    /// <returns>The rotation matrix.</returns>
    public static Matrix4x4 CreateRotationZ(double radians)
    {
        Matrix4x4 result = new();

        double c = Math.Cos(radians);
        double s = Math.Sin(radians);

        // [  c  s  0  0 ]
        // [ -s  c  0  0 ]
        // [  0  0  1  0 ]
        // [  0  0  0  1 ]
        result.M1 = new Vector4(c, s, 0.0, 0.0);
        result.M2 = new Vector4(-s, c, 0.0, 0.0);
        result.M3 = new Vector4(0.0, 0.0, 1.0, 0.0);
        result.M4 = new Vector4(0.0, 0.0, 0.0, 1.0);

        return result;
    }

    /// <summary>
    /// Creates a matrix for rotating points around the Z-axis, from a center point.
    /// </summary>
    /// <param name="radians">The amount, in radians, by which to rotate around the Z-axis.</param>
    /// <param name="centerPoint">The center point.</param>
    /// <returns>The rotation matrix.</returns>
    public static Matrix4x4 CreateRotationZ(double radians, Vector3 centerPoint)
    {
        Matrix4x4 result = new();

        double c = Math.Cos(radians);
        double s = Math.Sin(radians);

        double x = centerPoint.x * (1 - c) + centerPoint.y * s;
        double y = centerPoint.y * (1 - c) - centerPoint.x * s;

        // [  c  s  0  0 ]
        // [ -s  c  0  0 ]
        // [  0  0  1  0 ]
        // [  x  y  0  1 ]
        result.M1 = new Vector4(c, s, 0.0, 0.0);
        result.M2 = new Vector4(-s, c, 0.0, 0.0);
        result.M3 = new Vector4(0.0, 0.0, 1.0, 0.0);
        result.M4 = new Vector4(x, y, 0.0, 1.0);

        return result;
    }

    /// <summary>
    /// Creates a matrix that rotates around an arbitrary vector.
    /// </summary>
    /// <param name="axis">The axis to rotate around.</param>
    /// <param name="angle">The angle to rotate around the given axis, in radians.</param>
    /// <returns>The rotation matrix.</returns>
    public static Matrix4x4 CreateFromAxisAngle(Vector3 axis, double angle)
    {
        // a: angle
        // x, y, z: unit vector for axis.
        //
        // Rotation matrix M can compute by using below equation.
        //
        //        T               T
        //  M = uu + (cos a)( I-uu ) + (sin a)S
        //
        // Where:
        //
        //  u = ( x, y, z )
        //
        //      [  0 -z  y ]
        //  S = [  z  0 -x ]
        //      [ -y  x  0 ]
        //
        //      [ 1 0 0 ]
        //  I = [ 0 1 0 ]
        //      [ 0 0 1 ]
        //
        //
        //     [  xx+cosa*(1-xx)   yx-cosa*yx-sina*z zx-cosa*xz+sina*y ]
        // M = [ xy-cosa*yx+sina*z    yy+cosa(1-yy)  yz-cosa*yz-sina*x ]
        //     [ zx-cosa*zx-sina*y zy-cosa*zy+sina*x   zz+cosa*(1-zz)  ]
        //
        double x = axis.x, y = axis.y, z = axis.z;
        double sa = Math.Sin(angle), ca = Math.Cos(angle);
        double xx = x * x, yy = y * y, zz = z * z;
        double xy = x * y, xz = x * z, yz = y * z;

        Matrix4x4 result = new();

        result.M11 = xx + ca * (1.0 - xx);
        result.M12 = xy - ca * xy + sa * z;
        result.M13 = xz - ca * xz - sa * y;
        result.M14 = 0.0;
        result.M21 = xy - ca * xy - sa * z;
        result.M22 = yy + ca * (1.0 - yy);
        result.M23 = yz - ca * yz + sa * x;
        result.M24 = 0.0;
        result.M31 = xz - ca * xz + sa * y;
        result.M32 = yz - ca * yz - sa * x;
        result.M33 = zz + ca * (1.0 - zz);
        result.M34 = 0.0;

        result.M4 = new Vector4(0.0, 0.0, 0.0, 1.0);

        return result;
    }

    /// <summary>
    /// Creates a right-handed perspective projection matrix based on a field of view, aspect ratio, and near and far view plane distances.
    /// </summary>
    /// <param name="fieldOfView">Field of view in the y direction, in radians.</param>
    /// <param name="aspectRatio">Aspect ratio, defined as view space width divided by height.</param>
    /// <param name="nearPlaneDistance">Distance to the near view plane.</param>
    /// <param name="farPlaneDistance">Distance to the far view plane.</param>
    /// <returns>The perspective projection matrix.</returns>
    public static Matrix4x4 CreatePerspectiveFieldOfViewRightHanded(double fieldOfView, double aspectRatio, double nearPlaneDistance, double farPlaneDistance)
    {
        if (fieldOfView <= 0.0 || fieldOfView >= Math.PI)
            throw new ArgumentOutOfRangeException(nameof(fieldOfView));

        if (nearPlaneDistance <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(nearPlaneDistance));

        if (farPlaneDistance <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(farPlaneDistance));

        if (nearPlaneDistance >= farPlaneDistance)
            throw new ArgumentOutOfRangeException(nameof(nearPlaneDistance));

        double yScale = 1.0 / Math.Tan(fieldOfView * 0.5);
        double xScale = yScale / aspectRatio;

        Matrix4x4 result;

        result.M11 = xScale;
        result.M12 = result.M13 = result.M14 = 0.0;

        result.M22 = yScale;
        result.M21 = result.M23 = result.M24 = 0.0;

        result.M31 = result.M32 = 0.0;
        result.M33 = farPlaneDistance / (nearPlaneDistance - farPlaneDistance);
        result.M34 = -1.0;

        result.M41 = result.M42 = result.M44 = 0.0;
        result.M43 = nearPlaneDistance * farPlaneDistance / (nearPlaneDistance - farPlaneDistance);

        return result;
    }

    /// <summary>Creates a left-handed perspective projection matrix based on a field of view, aspect ratio, and near and far view plane distances.</summary>
    /// <param name="fieldOfView">The field of view in the y direction, in radians.</param>
    /// <param name="aspectRatio">The aspect ratio, defined as view space width divided by height.</param>
    /// <param name="nearPlaneDistance">The distance to the near view plane.</param>
    /// <param name="farPlaneDistance">The distance to the far view plane.</param>
    /// <returns>The left-handed perspective projection matrix.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fieldOfView" /> is less than or equal to zero.
    /// -or-
    /// <paramref name="fieldOfView" /> is greater than or equal to <see cref="double.Pi" />.
    /// <paramref name="nearPlaneDistance" /> is less than or equal to zero.
    /// -or-
    /// <paramref name="farPlaneDistance" /> is less than or equal to zero.
    /// -or-
    /// <paramref name="nearPlaneDistance" /> is greater than or equal to <paramref name="farPlaneDistance" />.</exception>
    public static Matrix4x4 CreatePerspectiveFieldOfView(double fieldOfView, double aspectRatio, double nearPlaneDistance, double farPlaneDistance)
    {
        // This implementation is based on the DirectX Math Library XMMatrixPerspectiveFovLH method
        // https://github.com/microsoft/DirectXMath/blob/master/Inc/DirectXMathMatrix.inl

        if (fieldOfView <= 0.0 || fieldOfView >= Math.PI)
            throw new ArgumentOutOfRangeException(nameof(fieldOfView));

        if (nearPlaneDistance <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(nearPlaneDistance));

        if (farPlaneDistance <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(farPlaneDistance));

        if (nearPlaneDistance >= farPlaneDistance)
            throw new ArgumentOutOfRangeException(nameof(nearPlaneDistance));

        double height = 1.0 / Math.Tan(fieldOfView * 0.5);
        double width = height / aspectRatio;
        double range = double.IsPositiveInfinity(farPlaneDistance) ? 1.0 : farPlaneDistance / (farPlaneDistance - nearPlaneDistance);

        Matrix4x4 result = default;

        result.M1 = new Vector4(width, 0, 0, 0);
        result.M2 = new Vector4(0, height, 0, 0);
        result.M3 = new Vector4(0, 0, range, 1.0);
        result.M4 = new Vector4(0, 0, -range * nearPlaneDistance, 0);

        return result;
    }

    /// <summary>
    /// Creates a right-handed perspective projection matrix from the given view volume dimensions.
    /// </summary>
    /// <param name="width">Width of the view volume at the near view plane.</param>
    /// <param name="height">Height of the view volume at the near view plane.</param>
    /// <param name="nearPlaneDistance">Distance to the near view plane.</param>
    /// <param name="farPlaneDistance">Distance to the far view plane.</param>
    /// <returns>The perspective projection matrix.</returns>
    public static Matrix4x4 CreatePerspectiveRightHanded(double width, double height, double nearPlaneDistance, double farPlaneDistance)
    {
        if (nearPlaneDistance <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(nearPlaneDistance));

        if (farPlaneDistance <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(farPlaneDistance));

        if (nearPlaneDistance >= farPlaneDistance)
            throw new ArgumentOutOfRangeException(nameof(nearPlaneDistance));

        Matrix4x4 result;

        result.M11 = 2.0 * nearPlaneDistance / width;
        result.M12 = result.M13 = result.M14 = 0.0;

        result.M22 = 2.0 * nearPlaneDistance / height;
        result.M21 = result.M23 = result.M24 = 0.0;

        result.M33 = farPlaneDistance / (nearPlaneDistance - farPlaneDistance);
        result.M31 = result.M32 = 0.0;
        result.M34 = -1.0;

        result.M41 = result.M42 = result.M44 = 0.0;
        result.M43 = nearPlaneDistance * farPlaneDistance / (nearPlaneDistance - farPlaneDistance);

        return result;
    }

    /// <summary>Creates a left-handed perspective projection matrix from the given view volume dimensions.</summary>
    /// <param name="width">The width of the view volume at the near view plane.</param>
    /// <param name="height">The height of the view volume at the near view plane.</param>
    /// <param name="nearPlaneDistance">The distance to the near view plane.</param>
    /// <param name="farPlaneDistance">The distance to the far view plane.</param>
    /// <returns>The left-handed perspective projection matrix.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="nearPlaneDistance" /> is less than or equal to zero.
    /// -or-
    /// <paramref name="farPlaneDistance" /> is less than or equal to zero.
    /// -or-
    /// <paramref name="nearPlaneDistance" /> is greater than or equal to <paramref name="farPlaneDistance" />.</exception>
    public static Matrix4x4 CreatePerspective(double width, double height, double nearPlaneDistance, double farPlaneDistance)
    {
        // This implementation is based on the DirectX Math Library XMMatrixPerspectiveLH method
        // https://github.com/microsoft/DirectXMath/blob/master/Inc/DirectXMathMatrix.inl

        if (nearPlaneDistance <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(nearPlaneDistance));
        if (nearPlaneDistance >= farPlaneDistance)
            throw new ArgumentOutOfRangeException(nameof(nearPlaneDistance));
        if (farPlaneDistance <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(farPlaneDistance));

        double dblNearPlaneDistance = nearPlaneDistance + nearPlaneDistance;
        double range = double.IsPositiveInfinity(farPlaneDistance) ? 1.0f : farPlaneDistance / (farPlaneDistance - nearPlaneDistance);

        Matrix4x4 result = default;

        result.M1 = new Vector4(dblNearPlaneDistance / width, 0, 0, 0);
        result.M2 = new Vector4(0, dblNearPlaneDistance / height, 0, 0);
        result.M3 = new Vector4(0, 0, range, 1.0f);
        result.M4 = new Vector4(0, 0, -range * nearPlaneDistance, 0);

        return result;
    }

    /// <summary>
    /// Creates a right-handed customized, perspective projection matrix.
    /// </summary>
    /// <param name="left">Minimum x-value of the view volume at the near view plane.</param>
    /// <param name="right">Maximum x-value of the view volume at the near view plane.</param>
    /// <param name="bottom">Minimum y-value of the view volume at the near view plane.</param>
    /// <param name="top">Maximum y-value of the view volume at the near view plane.</param>
    /// <param name="nearPlaneDistance">Distance to the near view plane.</param>
    /// <param name="farPlaneDistance">Distance to of the far view plane.</param>
    /// <returns>The perspective projection matrix.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix4x4 CreatePerspectiveOffCenterRightHanded(double left, double right, double bottom, double top, double nearPlaneDistance, double farPlaneDistance)
    {
        // This implementation is based on the DirectX Math Library XMMatrixPerspectiveOffCenterRH method
        // https://github.com/microsoft/DirectXMath/blob/master/Inc/DirectXMathMatrix.inl

        if (nearPlaneDistance <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(nearPlaneDistance));
        if (farPlaneDistance <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(farPlaneDistance));
        if (nearPlaneDistance >= farPlaneDistance)
            throw new ArgumentOutOfRangeException(nameof(nearPlaneDistance));

        double dblNearPlaneDistance = nearPlaneDistance + nearPlaneDistance;
        double reciprocalWidth = 1.0 / (right - left);
        double reciprocalHeight = 1.0 / (top - bottom);
        double range = double.IsPositiveInfinity(farPlaneDistance) ? -1.0 : farPlaneDistance / (nearPlaneDistance - farPlaneDistance);

        Matrix4x4 result = default;

        result.M1 = new Vector4(dblNearPlaneDistance * reciprocalWidth, 0, 0, 0);
        result.M2 = new Vector4(0, dblNearPlaneDistance * reciprocalHeight, 0, 0);
        result.M3 = new Vector4(
            (left + right) * reciprocalWidth,
            (top + bottom) * reciprocalHeight,
            range,
            -1.0f
        );
        result.M4 = new Vector4(0, 0, range * nearPlaneDistance, 0);

        return result;
    }

    /// <summary>
    /// Creates a left-handed customized, perspective projection matrix.
    /// </summary>
    /// <param name="left">Minimum x-value of the view volume at the near view plane.</param>
    /// <param name="right">Maximum x-value of the view volume at the near view plane.</param>
    /// <param name="bottom">Minimum y-value of the view volume at the near view plane.</param>
    /// <param name="top">Maximum y-value of the view volume at the near view plane.</param>
    /// <param name="nearPlaneDistance">Distance to the near view plane.</param>
    /// <param name="farPlaneDistance">Distance to of the far view plane.</param>
    /// <returns>The perspective projection matrix.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix4x4 CreatePerspectiveOffCenter(double left, double right, double bottom, double top, double nearPlaneDistance, double farPlaneDistance)
    {
        // This implementation is based on the DirectX Math Library XMMatrixPerspectiveOffCenterLH method
        // https://github.com/microsoft/DirectXMath/blob/master/Inc/DirectXMathMatrix.inl

        if (nearPlaneDistance <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(nearPlaneDistance));
        if (farPlaneDistance <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(farPlaneDistance));
        if (nearPlaneDistance >= farPlaneDistance)
            throw new ArgumentOutOfRangeException(nameof(nearPlaneDistance));

        double dblNearPlaneDistance = nearPlaneDistance + nearPlaneDistance;
        double reciprocalWidth = 1.0 / (right - left);
        double reciprocalHeight = 1.0 / (top - bottom);
        double range = double.IsPositiveInfinity(farPlaneDistance) ? 1.0 : farPlaneDistance / (farPlaneDistance - nearPlaneDistance);

        Matrix4x4 result = default;

        result.M1 = new Vector4(dblNearPlaneDistance * reciprocalWidth, 0, 0, 0);
        result.M2 = new Vector4(0, dblNearPlaneDistance * reciprocalHeight, 0, 0);
        result.M3 = new Vector4(
            -(left + right) * reciprocalWidth,
            -(top + bottom) * reciprocalHeight,
            range,
            1.0f
        );
        result.M4 = new Vector4(0, 0, -range * nearPlaneDistance, 0);

        return result;
    }

    /// <summary>
    /// Creates a right-handed orthographic perspective matrix from the given view volume dimensions.
    /// </summary>
    /// <param name="width">Width of the view volume.</param>
    /// <param name="height">Height of the view volume.</param>
    /// <param name="zNearPlane">Minimum Z-value of the view volume.</param>
    /// <param name="zFarPlane">Maximum Z-value of the view volume.</param>
    /// <returns>The orthographic projection matrix.</returns>
    public static Matrix4x4 CreateOrthographicRightHanded(double width, double height, double zNearPlane, double zFarPlane)
    {
        Matrix4x4 result;

        result.M11 = 2.0 / width;
        result.M12 = result.M13 = result.M14 = 0.0;

        result.M22 = 2.0 / height;
        result.M21 = result.M23 = result.M24 = 0.0;

        result.M33 = 1.0 / (zNearPlane - zFarPlane);
        result.M31 = result.M32 = result.M34 = 0.0;

        result.M41 = result.M42 = 0.0;
        result.M43 = zNearPlane / (zNearPlane - zFarPlane);
        result.M44 = 1.0;

        return result;
    }

    /// <summary>Creates a left-handed orthographic perspective matrix from the given view volume dimensions.</summary>
    /// <param name="width">The width of the view volume.</param>
    /// <param name="height">The height of the view volume.</param>
    /// <param name="zNearPlane">The minimum Z-value of the view volume.</param>
    /// <param name="zFarPlane">The maximum Z-value of the view volume.</param>
    /// <returns>The left-handed orthographic projection matrix.</returns>
    public static Matrix4x4 CreateOrthographic(double width, double height, double zNearPlane, double zFarPlane)
    {
        // This implementation is based on the DirectX Math Library XMMatrixOrthographicLH method
        // https://github.com/microsoft/DirectXMath/blob/master/Inc/DirectXMathMatrix.inl

        double range = 1.0 / (zFarPlane - zNearPlane);

        Matrix4x4 result;

        result.M11 = 2.0 / width;
        result.M12 = result.M13 = result.M14 = 0.0;

        result.M22 = 2.0 / height;
        result.M21 = result.M23 = result.M24 = 0.0;

        result.M33 = range;
        result.M31 = result.M32 = result.M34 = 0.0;

        result.M41 = result.M42 = 0.0;
        result.M43 = -range * zNearPlane;
        result.M44 = 1;

        return result;
    }

    /// <summary>
    /// Builds a right-handed customized, orthographic projection matrix.
    /// </summary>
    /// <param name="left">Minimum X-value of the view volume.</param>
    /// <param name="right">Maximum X-value of the view volume.</param>
    /// <param name="bottom">Minimum Y-value of the view volume.</param>
    /// <param name="top">Maximum Y-value of the view volume.</param>
    /// <param name="zNearPlane">Minimum Z-value of the view volume.</param>
    /// <param name="zFarPlane">Maximum Z-value of the view volume.</param>
    /// <returns>The orthographic projection matrix.</returns>
    public static Matrix4x4 CreateOrthographicOffCenterRightHanded(double left, double right, double bottom, double top, double zNearPlane, double zFarPlane)
    {
        Matrix4x4 result;

        result.M11 = 2.0 / (right - left);
        result.M12 = result.M13 = result.M14 = 0.0;

        result.M22 = 2.0 / (top - bottom);
        result.M21 = result.M23 = result.M24 = 0.0;

        result.M33 = 1.0 / (zNearPlane - zFarPlane);
        result.M31 = result.M32 = result.M34 = 0.0;

        result.M41 = (left + right) / (left - right);
        result.M42 = (top + bottom) / (bottom - top);
        result.M43 = zNearPlane / (zNearPlane - zFarPlane);
        result.M44 = 1.0;

        return result;
    }

    /// <summary>Creates a left-handed customized orthographic projection matrix.</summary>
    /// <param name="left">The minimum X-value of the view volume.</param>
    /// <param name="right">The maximum X-value of the view volume.</param>
    /// <param name="bottom">The minimum Y-value of the view volume.</param>
    /// <param name="top">The maximum Y-value of the view volume.</param>
    /// <param name="zNearPlane">The minimum Z-value of the view volume.</param>
    /// <param name="zFarPlane">The maximum Z-value of the view volume.</param>
    /// <returns>The left-handed orthographic projection matrix.</returns>
    public static Matrix4x4 CreateOrthographicOffCenter(double left, double right, double bottom, double top, double zNearPlane, double zFarPlane)
    {
        // This implementation is based on the DirectX Math Library XMMatrixOrthographicOffCenterLH method
        // https://github.com/microsoft/DirectXMath/blob/master/Inc/DirectXMathMatrix.inl

        double reciprocalWidth = 1.0 / (right - left);
        double reciprocalHeight = 1.0 / (top - bottom);
        double range = 1.0 / (zFarPlane - zNearPlane);

        Matrix4x4 result = default;

        result.M1 = new Vector4(reciprocalWidth + reciprocalWidth, 0, 0, 0);
        result.M2 = new Vector4(0, reciprocalHeight + reciprocalHeight, 0, 0);
        result.M3 = new Vector4(0, 0, range, 0);
        result.M4 = new Vector4(
            -(left + right) * reciprocalWidth,
            -(top + bottom) * reciprocalHeight,
            -range * zNearPlane,
            1
        );

        return result;
    }

    /// <summary>Creates a right-handed view matrix.</summary>
    /// <param name="cameraPosition">The position of the camera.</param>
    /// <param name="cameraDirection">The direction in which the camera is pointing.</param>
    /// <param name="cameraUpVector">The direction that is "up" from the camera's point of view.</param>
    /// <returns>The right-handed view matrix.</returns>
    public static Matrix4x4 CreateLookToRightHanded(in Vector3 cameraPosition, in Vector3 cameraDirection, in Vector3 cameraUpVector)
    {
        // This implementation is based on the DirectX Math Library XMMatrixLookToRH method
        // https://github.com/microsoft/DirectXMath/blob/master/Inc/DirectXMathMatrix.inl

        return CreateLookTo(cameraPosition, -cameraDirection, cameraUpVector);
    }

    /// <summary>Creates a left-handed view matrix.</summary>
    /// <param name="cameraPosition">The position of the camera.</param>
    /// <param name="cameraDirection">The direction in which the camera is pointing.</param>
    /// <param name="cameraUpVector">The direction that is "up" from the camera's point of view.</param>
    /// <returns>The left-handed view matrix.</returns>
    public static Matrix4x4 CreateLookTo(in Vector3 cameraPosition, in Vector3 cameraDirection, in Vector3 cameraUpVector)
    {
        Vector3 axisZ = Vector3.Normalize(cameraDirection);
        Vector3 axisX = Vector3.Normalize(Vector3.Cross(cameraUpVector, axisZ));
        Vector3 axisY = Vector3.Cross(axisZ, axisX);
        Vector3 negativeCameraPosition = -cameraPosition;

        Matrix4x4 result = new();

        result.M1 = new Vector4(axisX.x, axisY.x, axisZ.x, 0.0);
        result.M2 = new Vector4(axisX.y, axisY.y, axisZ.y, 0.0);
        result.M3 = new Vector4(axisX.z, axisY.z, axisZ.z, 0.0);

        result.M41 = Vector3.Dot(axisX, negativeCameraPosition);
        result.M42 = Vector3.Dot(axisY, negativeCameraPosition);
        result.M43 = Vector3.Dot(axisZ, negativeCameraPosition);
        result.M44 = 1.0;

        return result;
    }

    /// <summary>
    /// Creates a right-handed view matrix.
    /// </summary>
    /// <param name="cameraPosition">The position of the camera.</param>
    /// <param name="cameraTarget">The target towards which the camera is pointing.</param>
    /// <param name="cameraUpVector">The direction that is "up" from the camera's point of view.</param>
    /// <returns>The view matrix.</returns>
    public static Matrix4x4 CreateLookAtRightHanded(Vector3 cameraPosition, Vector3 cameraTarget, Vector3 cameraUpVector)
    {
        Vector3 zaxis = Vector3.Normalize(cameraPosition - cameraTarget);
        Vector3 xaxis = Vector3.Normalize(Vector3.Cross(cameraUpVector, zaxis));
        Vector3 yaxis = Vector3.Cross(zaxis, xaxis);

        Matrix4x4 result = new();

        result.M1 = new Vector4(xaxis.x, yaxis.x, zaxis.x, 0.0);
        result.M2 = new Vector4(xaxis.y, yaxis.y, zaxis.y, 0.0);
        result.M3 = new Vector4(xaxis.z, yaxis.z, zaxis.z, 0.0);

        result.M41 = -Vector3.Dot(xaxis, cameraPosition);
        result.M42 = -Vector3.Dot(yaxis, cameraPosition);
        result.M43 = -Vector3.Dot(zaxis, cameraPosition);
        result.M44 = 1.0;

        return result;
    }

    /// <summary>
    /// Creates a left-handed view matrix.
    /// </summary>
    /// <param name="cameraPosition">The position of the camera.</param>
    /// <param name="cameraTarget">The target towards which the camera is pointing.</param>
    /// <param name="cameraUpVector">The direction that is "up" from the camera's point of view.</param>
    /// <returns>The view matrix.</returns>
    public static Matrix4x4 CreateLookAt(Vector3 cameraPosition, Vector3 cameraTarget, Vector3 cameraUpVector)
    {
        Vector3 cameraDirection = cameraTarget - cameraPosition;
        return CreateLookTo(in cameraPosition, in cameraDirection, in cameraUpVector);
    }

    /// <summary>
    /// Creates a world matrix with the specified parameters.
    /// </summary>
    /// <param name="position">The position of the object; used in translation operations.</param>
    /// <param name="forward">Forward direction of the object.</param>
    /// <param name="up">Upward direction of the object; usually [0, 1, 0].</param>
    /// <returns>The world matrix.</returns>
    public static Matrix4x4 CreateWorld(Vector3 position, Vector3 forward, Vector3 up)
    {
        Vector3 zaxis = Vector3.Normalize(-forward);
        Vector3 xaxis = Vector3.Normalize(Vector3.Cross(up, zaxis));
        Vector3 yaxis = Vector3.Cross(zaxis, xaxis);

        Matrix4x4 result = new();

        result.M1 = new Vector4(xaxis.x, xaxis.y, xaxis.z, 0.0);
        result.M2 = new Vector4(yaxis.x, yaxis.y, yaxis.z, 0.0);
        result.M3 = new Vector4(zaxis.x, zaxis.y, zaxis.z, 0.0);
        result.M4 = new Vector4(position.x, position.y, position.z, 1.0);

        return result;
    }

    /// <summary>
    /// Creates a rotation matrix from the given Quaternion rotation value.
    /// </summary>
    /// <param name="quaternion">The source Quaternion.</param>
    /// <returns>The rotation matrix.</returns>
    public static Matrix4x4 CreateFromQuaternion(Quaternion quaternion)
    {
        Matrix4x4 result = new();

        double xx = quaternion.x * quaternion.x;
        double yy = quaternion.y * quaternion.y;
        double zz = quaternion.z * quaternion.z;

        double xy = quaternion.x * quaternion.y;
        double wz = quaternion.z * quaternion.w;
        double xz = quaternion.z * quaternion.x;
        double wy = quaternion.y * quaternion.w;
        double yz = quaternion.y * quaternion.z;
        double wx = quaternion.x * quaternion.w;

        result.M11 = 1.0 - 2.0 * (yy + zz);
        result.M12 = 2.0 * (xy + wz);
        result.M13 = 2.0 * (xz - wy);
        result.M14 = 0.0;
        result.M21 = 2.0 * (xy - wz);
        result.M22 = 1.0 - 2.0 * (zz + xx);
        result.M23 = 2.0 * (yz + wx);
        result.M24 = 0.0;
        result.M31 = 2.0 * (xz + wy);
        result.M32 = 2.0 * (yz - wx);
        result.M33 = 1.0 - 2.0 * (yy + xx);
        result.M34 = 0.0;

        result.M4 = new Vector4(0.0, 0.0, 0.0, 1.0);

        return result;
    }

    /// <summary>
    /// Creates a rotation matrix from the specified yaw, pitch, and roll.
    /// </summary>
    /// <param name="yaw">Angle of rotation, in radians, around the Y-axis.</param>
    /// <param name="pitch">Angle of rotation, in radians, around the X-axis.</param>
    /// <param name="roll">Angle of rotation, in radians, around the Z-axis.</param>
    /// <returns>The rotation matrix.</returns>
    public static Matrix4x4 CreateFromYawPitchRoll(double yaw, double pitch, double roll)
    {
        Quaternion q = Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);

        return CreateFromQuaternion(q);
    }

    /// <summary>
    /// Creates a Matrix that flattens geometry into a specified Plane as if casting a shadow from a specified light source.
    /// </summary>
    /// <param name="lightDirection">The direction from which the light that will cast the shadow is coming.</param>
    /// <param name="plane">The Plane onto which the new matrix should flatten geometry so as to cast a shadow.</param>
    /// <returns>A new Matrix that can be used to flatten geometry onto the specified plane from the specified direction.</returns>
    public static Matrix4x4 CreateShadow(Vector3 lightDirection, Plane plane)
    {
        Plane p = Plane.Normalize(plane);

        double dot = p.normal.x * lightDirection.x + p.normal.y * lightDirection.y + p.normal.z * lightDirection.z;
        double a = -p.normal.x;
        double b = -p.normal.y;
        double c = -p.normal.z;
        double d = -p.distance;

        Matrix4x4 result = new();

        result.M11 = a * lightDirection.x + dot;
        result.M21 = b * lightDirection.x;
        result.M31 = c * lightDirection.x;
        result.M41 = d * lightDirection.x;

        result.M12 = a * lightDirection.y;
        result.M22 = b * lightDirection.y + dot;
        result.M32 = c * lightDirection.y;
        result.M42 = d * lightDirection.y;

        result.M13 = a * lightDirection.z;
        result.M23 = b * lightDirection.z;
        result.M33 = c * lightDirection.z + dot;
        result.M43 = d * lightDirection.z;

        result.M4 = new Vector4(0.0, 0.0, 0.0, dot);

        return result;
    }

    /// <summary>
    /// Creates a Matrix that reflects the coordinate system about a specified Plane.
    /// </summary>
    /// <param name="value">The Plane about which to create a reflection.</param>
    /// <returns>A new matrix expressing the reflection.</returns>
    public static Matrix4x4 CreateReflection(Plane value)
    {
        value = Plane.Normalize(value);

        double a = value.normal.x;
        double b = value.normal.y;
        double c = value.normal.z;

        double fa = -2.0 * a;
        double fb = -2.0 * b;
        double fc = -2.0 * c;

        Matrix4x4 result;

        result.M11 = fa * a + 1.0;
        result.M12 = fb * a;
        result.M13 = fc * a;
        result.M14 = 0.0;

        result.M21 = fa * b;
        result.M22 = fb * b + 1.0;
        result.M23 = fc * b;
        result.M24 = 0.0;

        result.M31 = fa * c;
        result.M32 = fb * c;
        result.M33 = fc * c + 1.0;
        result.M34 = 0.0;

        result.M41 = fa * value.distance;
        result.M42 = fb * value.distance;
        result.M43 = fc * value.distance;
        result.M44 = 1.0;

        return result;
    }

    /// <summary>
    /// Calculates the determinant of the matrix.
    /// </summary>
    /// <returns>The determinant of the matrix.</returns>
    public double GetDeterminant()
    {
        // | a b c d |     | f g h |     | e g h |     | e f h |     | e f g |
        // | e f g h | = a | j k l | - b | i k l | + c | i j l | - d | i j k |
        // | i j k l |     | n o p |     | m o p |     | m n p |     | m n o |
        // | m n o p |
        //
        //   | f g h |
        // a | j k l | = a ( f ( kp - lo ) - g ( jp - ln ) + h ( jo - kn ) )
        //   | n o p |
        //
        //   | e g h |
        // b | i k l | = b ( e ( kp - lo ) - g ( ip - lm ) + h ( io - km ) )
        //   | m o p |
        //
        //   | e f h |
        // c | i j l | = c ( e ( jp - ln ) - f ( ip - lm ) + h ( in - jm ) )
        //   | m n p |
        //
        //   | e f g |
        // d | i j k | = d ( e ( jo - kn ) - f ( io - km ) + g ( in - jm ) )
        //   | m n o |
        //
        // Cost of operation
        // 17 adds and 28 muls.
        //
        // add: 6 + 8 + 3 = 17
        // mul: 12 + 16 = 28

        double a = M11, b = M12, c = M13, d = M14;
        double e = M21, f = M22, g = M23, h = M24;
        double i = M31, j = M32, k = M33, l = M34;
        double m = M41, n = M42, o = M43, p = M44;

        double kp_lo = k * p - l * o;
        double jp_ln = j * p - l * n;
        double jo_kn = j * o - k * n;
        double ip_lm = i * p - l * m;
        double io_km = i * o - k * m;
        double in_jm = i * n - j * m;

        return a * (f * kp_lo - g * jp_ln + h * jo_kn) -
               b * (e * kp_lo - g * ip_lm + h * io_km) +
               c * (e * jp_ln - f * ip_lm + h * in_jm) -
               d * (e * jo_kn - f * io_km + g * in_jm);
    }

    public Matrix4x4 Invert()
    {
        Invert(this, out Matrix4x4 result);
        return result;
    }

    /// <summary>
    /// Attempts to calculate the inverse of the given matrix. If successful, result will contain the inverted matrix.
    /// </summary>
    /// <param name="matrix">The source matrix to invert.</param>
    /// <param name="result">If successful, contains the inverted matrix.</param>
    /// <returns>True if the source matrix could be inverted; False otherwise.</returns>
    public static bool Invert(Matrix4x4 matrix, out Matrix4x4 result)
    {
        //                                       -1
        // If you have matrix M, inverse Matrix M   can compute
        //
        //     -1       1
        //    M   = --------- A
        //            det(M)
        //
        // A is adjugate (adjoint) of M, where,
        //
        //      T
        // A = C
        //
        // C is Cofactor matrix of M, where,
        //           i + j
        // C   = (-1)      * det(M  )
        //  ij                    ij
        //
        //     [ a b c d ]
        // M = [ e f g h ]
        //     [ i j k l ]
        //     [ m n o p ]
        //
        // First Row
        //           2 | f g h |
        // C   = (-1)  | j k l | = + ( f ( kp - lo ) - g ( jp - ln ) + h ( jo - kn ) )
        //  11         | n o p |
        //
        //           3 | e g h |
        // C   = (-1)  | i k l | = - ( e ( kp - lo ) - g ( ip - lm ) + h ( io - km ) )
        //  12         | m o p |
        //
        //           4 | e f h |
        // C   = (-1)  | i j l | = + ( e ( jp - ln ) - f ( ip - lm ) + h ( in - jm ) )
        //  13         | m n p |
        //
        //           5 | e f g |
        // C   = (-1)  | i j k | = - ( e ( jo - kn ) - f ( io - km ) + g ( in - jm ) )
        //  14         | m n o |
        //
        // Second Row
        //           3 | b c d |
        // C   = (-1)  | j k l | = - ( b ( kp - lo ) - c ( jp - ln ) + d ( jo - kn ) )
        //  21         | n o p |
        //
        //           4 | a c d |
        // C   = (-1)  | i k l | = + ( a ( kp - lo ) - c ( ip - lm ) + d ( io - km ) )
        //  22         | m o p |
        //
        //           5 | a b d |
        // C   = (-1)  | i j l | = - ( a ( jp - ln ) - b ( ip - lm ) + d ( in - jm ) )
        //  23         | m n p |
        //
        //           6 | a b c |
        // C   = (-1)  | i j k | = + ( a ( jo - kn ) - b ( io - km ) + c ( in - jm ) )
        //  24         | m n o |
        //
        // Third Row
        //           4 | b c d |
        // C   = (-1)  | f g h | = + ( b ( gp - ho ) - c ( fp - hn ) + d ( fo - gn ) )
        //  31         | n o p |
        //
        //           5 | a c d |
        // C   = (-1)  | e g h | = - ( a ( gp - ho ) - c ( ep - hm ) + d ( eo - gm ) )
        //  32         | m o p |
        //
        //           6 | a b d |
        // C   = (-1)  | e f h | = + ( a ( fp - hn ) - b ( ep - hm ) + d ( en - fm ) )
        //  33         | m n p |
        //
        //           7 | a b c |
        // C   = (-1)  | e f g | = - ( a ( fo - gn ) - b ( eo - gm ) + c ( en - fm ) )
        //  34         | m n o |
        //
        // Fourth Row
        //           5 | b c d |
        // C   = (-1)  | f g h | = - ( b ( gl - hk ) - c ( fl - hj ) + d ( fk - gj ) )
        //  41         | j k l |
        //
        //           6 | a c d |
        // C   = (-1)  | e g h | = + ( a ( gl - hk ) - c ( el - hi ) + d ( ek - gi ) )
        //  42         | i k l |
        //
        //           7 | a b d |
        // C   = (-1)  | e f h | = - ( a ( fl - hj ) - b ( el - hi ) + d ( ej - fi ) )
        //  43         | i j l |
        //
        //           8 | a b c |
        // C   = (-1)  | e f g | = + ( a ( fk - gj ) - b ( ek - gi ) + c ( ej - fi ) )
        //  44         | i j k |
        //
        // Cost of operation
        // 53 adds, 104 muls, and 1 div.
        double a = matrix.M11, b = matrix.M12, c = matrix.M13, d = matrix.M14;
        double e = matrix.M21, f = matrix.M22, g = matrix.M23, h = matrix.M24;
        double i = matrix.M31, j = matrix.M32, k = matrix.M33, l = matrix.M34;
        double m = matrix.M41, n = matrix.M42, o = matrix.M43, p = matrix.M44;

        double kp_lo = k * p - l * o;
        double jp_ln = j * p - l * n;
        double jo_kn = j * o - k * n;
        double ip_lm = i * p - l * m;
        double io_km = i * o - k * m;
        double in_jm = i * n - j * m;

        double a11 = +(f * kp_lo - g * jp_ln + h * jo_kn);
        double a12 = -(e * kp_lo - g * ip_lm + h * io_km);
        double a13 = +(e * jp_ln - f * ip_lm + h * in_jm);
        double a14 = -(e * jo_kn - f * io_km + g * in_jm);

        double det = a * a11 + b * a12 + c * a13 + d * a14;

        if (Math.Abs(det) < double.Epsilon)
        {
            result = new Matrix4x4(double.NaN, double.NaN, double.NaN, double.NaN,
                double.NaN, double.NaN, double.NaN, double.NaN,
                double.NaN, double.NaN, double.NaN, double.NaN,
                double.NaN, double.NaN, double.NaN, double.NaN);
            return false;
        }

        double invDet = 1.0 / det;

        result.M11 = a11 * invDet;
        result.M21 = a12 * invDet;
        result.M31 = a13 * invDet;
        result.M41 = a14 * invDet;

        result.M12 = -(b * kp_lo - c * jp_ln + d * jo_kn) * invDet;
        result.M22 = +(a * kp_lo - c * ip_lm + d * io_km) * invDet;
        result.M32 = -(a * jp_ln - b * ip_lm + d * in_jm) * invDet;
        result.M42 = +(a * jo_kn - b * io_km + c * in_jm) * invDet;

        double gp_ho = g * p - h * o;
        double fp_hn = f * p - h * n;
        double fo_gn = f * o - g * n;
        double ep_hm = e * p - h * m;
        double eo_gm = e * o - g * m;
        double en_fm = e * n - f * m;

        result.M13 = +(b * gp_ho - c * fp_hn + d * fo_gn) * invDet;
        result.M23 = -(a * gp_ho - c * ep_hm + d * eo_gm) * invDet;
        result.M33 = +(a * fp_hn - b * ep_hm + d * en_fm) * invDet;
        result.M43 = -(a * fo_gn - b * eo_gm + c * en_fm) * invDet;

        double gl_hk = g * l - h * k;
        double fl_hj = f * l - h * j;
        double fk_gj = f * k - g * j;
        double el_hi = e * l - h * i;
        double ek_gi = e * k - g * i;
        double ej_fi = e * j - f * i;

        result.M14 = -(b * gl_hk - c * fl_hj + d * fk_gj) * invDet;
        result.M24 = +(a * gl_hk - c * el_hi + d * ek_gi) * invDet;
        result.M34 = -(a * fl_hj - b * el_hi + d * ej_fi) * invDet;
        result.M44 = +(a * fk_gj - b * ek_gi + c * ej_fi) * invDet;

        return true;
    }


    struct CanonicalBasis
    {
        public Vector3 Row0;
        public Vector3 Row1;
        public Vector3 Row2;
    };


    struct VectorBasis
    {
        public unsafe Vector3* Element0;
        public unsafe Vector3* Element1;
        public unsafe Vector3* Element2;
    }

    /// <summary>
    /// Attempts to extract the scale, translation, and rotation components from the given scale/rotation/translation matrix.
    /// If successful, the out parameters will contained the extracted values.
    /// </summary>
    /// <param name="matrix">The source matrix.</param>
    /// <param name="scale">The scaling component of the transformation matrix.</param>
    /// <param name="rotation">The rotation component of the transformation matrix.</param>
    /// <param name="translation">The translation component of the transformation matrix</param>
    /// <returns>True if the source matrix was successfully decomposed; False otherwise.</returns>
    public static bool Decompose(Matrix4x4 matrix, out Vector3 scale, out Quaternion rotation, out Vector3 translation)
    {
        bool result = true;

        unsafe
        {
            fixed (Vector3* scaleBase = &scale)
            {
                double* pfScales = (double*)scaleBase;
                double det;

                VectorBasis vectorBasis;
                Vector3** pVectorBasis = (Vector3**)&vectorBasis;

                Matrix4x4 matTemp = Identity;
                CanonicalBasis canonicalBasis = new CanonicalBasis();
                Vector3* pCanonicalBasis = &canonicalBasis.Row0;

                canonicalBasis.Row0 = new Vector3(1.0, 0.0, 0.0);
                canonicalBasis.Row1 = new Vector3(0.0, 1.0, 0.0);
                canonicalBasis.Row2 = new Vector3(0.0, 0.0, 1.0);

                translation = new Vector3(
                    matrix.M41,
                    matrix.M42,
                    matrix.M43);

                pVectorBasis[0] = (Vector3*)&matTemp.M11;
                pVectorBasis[1] = (Vector3*)&matTemp.M21;
                pVectorBasis[2] = (Vector3*)&matTemp.M31;

                *(pVectorBasis[0]) = new Vector3(matrix.M11, matrix.M12, matrix.M13);
                *(pVectorBasis[1]) = new Vector3(matrix.M21, matrix.M22, matrix.M23);
                *(pVectorBasis[2]) = new Vector3(matrix.M31, matrix.M32, matrix.M33);

                scale.x = pVectorBasis[0]->magnitude;
                scale.y = pVectorBasis[1]->magnitude;
                scale.z = pVectorBasis[2]->magnitude;

                uint a, b, c;
                #region Ranking
                double x = pfScales[0], y = pfScales[1], z = pfScales[2];
                if (x < y)
                {
                    if (y < z)
                    {
                        a = 2;
                        b = 1;
                        c = 0;
                    }
                    else
                    {
                        a = 1;

                        if (x < z)
                        {
                            b = 2;
                            c = 0;
                        }
                        else
                        {
                            b = 0;
                            c = 2;
                        }
                    }
                }
                else
                {
                    if (x < z)
                    {
                        a = 2;
                        b = 0;
                        c = 1;
                    }
                    else
                    {
                        a = 0;

                        if (y < z)
                        {
                            b = 2;
                            c = 1;
                        }
                        else
                        {
                            b = 1;
                            c = 2;
                        }
                    }
                }
                #endregion

                if (pfScales[a] < double.Epsilon)
                {
                    *(pVectorBasis[a]) = pCanonicalBasis[a];
                }

                *pVectorBasis[a] = Vector3.Normalize(*pVectorBasis[a]);

                if (pfScales[b] < double.Epsilon)
                {
                    uint cc;
                    double fAbsX, fAbsY, fAbsZ;

                    fAbsX = Math.Abs(pVectorBasis[a]->x);
                    fAbsY = Math.Abs(pVectorBasis[a]->y);
                    fAbsZ = Math.Abs(pVectorBasis[a]->z);

                    #region Ranking
                    if (fAbsX < fAbsY)
                    {
                        if (fAbsY < fAbsZ)
                        {
                            cc = 0;
                        }
                        else
                        {
                            if (fAbsX < fAbsZ)
                            {
                                cc = 0;
                            }
                            else
                            {
                                cc = 2;
                            }
                        }
                    }
                    else
                    {
                        if (fAbsX < fAbsZ)
                        {
                            cc = 1;
                        }
                        else
                        {
                            if (fAbsY < fAbsZ)
                            {
                                cc = 1;
                            }
                            else
                            {
                                cc = 2;
                            }
                        }
                    }
                    #endregion

                    *pVectorBasis[b] = Vector3.Cross(*pVectorBasis[a], *(pCanonicalBasis + cc));
                }

                *pVectorBasis[b] = Vector3.Normalize(*pVectorBasis[b]);

                if (pfScales[c] < double.Epsilon)
                {
                    *pVectorBasis[c] = Vector3.Cross(*pVectorBasis[a], *pVectorBasis[b]);
                }

                *pVectorBasis[c] = Vector3.Normalize(*pVectorBasis[c]);

                det = matTemp.GetDeterminant();

                // use Kramer's rule to check for handedness of coordinate system
                if (det < 0.0)
                {
                    // switch coordinate system by negating the scale and inverting the basis vector on the x-axis
                    pfScales[a] = -pfScales[a];
                    *pVectorBasis[a] = -(*pVectorBasis[a]);

                    det = -det;
                }

                det -= 1.0;
                det *= det;

                if ((det > double.Epsilon))
                {
                    // Non-SRT matrix encountered
                    rotation = Quaternion.identity;
                    result = false;
                }
                else
                {
                    // generate the quaternion from the matrix
                    rotation = Quaternion.MatrixToQuaternion(matTemp);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Transforms the given matrix by applying the given Quaternion rotation.
    /// </summary>
    /// <param name="value">The source matrix to transform.</param>
    /// <param name="rotation">The rotation to apply.</param>
    /// <returns>The transformed matrix.</returns>
    public static Matrix4x4 Transform(Matrix4x4 value, Quaternion rotation)
    {
        // Compute rotation matrix.
        double x2 = rotation.x + rotation.x;
        double y2 = rotation.y + rotation.y;
        double z2 = rotation.z + rotation.z;

        double wx2 = rotation.w * x2;
        double wy2 = rotation.w * y2;
        double wz2 = rotation.w * z2;
        double xx2 = rotation.x * x2;
        double xy2 = rotation.x * y2;
        double xz2 = rotation.x * z2;
        double yy2 = rotation.y * y2;
        double yz2 = rotation.y * z2;
        double zz2 = rotation.z * z2;

        double q11 = 1.0 - yy2 - zz2;
        double q21 = xy2 - wz2;
        double q31 = xz2 + wy2;

        double q12 = xy2 + wz2;
        double q22 = 1.0 - xx2 - zz2;
        double q32 = yz2 - wx2;

        double q13 = xz2 - wy2;
        double q23 = yz2 + wx2;
        double q33 = 1.0 - xx2 - yy2;

        Matrix4x4 result;

        // First row
        result.M11 = value.M11 * q11 + value.M12 * q21 + value.M13 * q31;
        result.M12 = value.M11 * q12 + value.M12 * q22 + value.M13 * q32;
        result.M13 = value.M11 * q13 + value.M12 * q23 + value.M13 * q33;
        result.M14 = value.M14;

        // Second row
        result.M21 = value.M21 * q11 + value.M22 * q21 + value.M23 * q31;
        result.M22 = value.M21 * q12 + value.M22 * q22 + value.M23 * q32;
        result.M23 = value.M21 * q13 + value.M22 * q23 + value.M23 * q33;
        result.M24 = value.M24;

        // Third row
        result.M31 = value.M31 * q11 + value.M32 * q21 + value.M33 * q31;
        result.M32 = value.M31 * q12 + value.M32 * q22 + value.M33 * q32;
        result.M33 = value.M31 * q13 + value.M32 * q23 + value.M33 * q33;
        result.M34 = value.M34;

        // Fourth row
        result.M41 = value.M41 * q11 + value.M42 * q21 + value.M43 * q31;
        result.M42 = value.M41 * q12 + value.M42 * q22 + value.M43 * q32;
        result.M43 = value.M41 * q13 + value.M42 * q23 + value.M43 * q33;
        result.M44 = value.M44;

        return result;
    }

    /// <summary>
    /// Transposes the rows and columns of a matrix.
    /// </summary>
    /// <param name="matrix">The source matrix.</param>
    /// <returns>The transposed matrix.</returns>
    public static Matrix4x4 Transpose(Matrix4x4 matrix)
    {
        Matrix4x4 result = new();
        result.M1 = new Vector4(matrix.M1.x, matrix.M2.x, matrix.M3.x, matrix.M4.x);
        result.M2 = new Vector4(matrix.M1.y, matrix.M2.y, matrix.M3.y, matrix.M4.y);
        result.M3 = new Vector4(matrix.M1.z, matrix.M2.z, matrix.M3.z, matrix.M4.z);
        result.M4 = new Vector4(matrix.M1.w, matrix.M2.w, matrix.M3.w, matrix.M4.w);

        return result;
    }

    /// <summary>
    /// Linearly interpolates between the corresponding values of two matrices.
    /// </summary>
    /// <param name="matrix1">The first source matrix.</param>
    /// <param name="matrix2">The second source matrix.</param>
    /// <param name="amount">The relative weight of the second source matrix.</param>
    /// <returns>The interpolated matrix.</returns>
    public static Matrix4x4 Lerp(Matrix4x4 matrix1, Matrix4x4 matrix2, double amount)
    {
        Matrix4x4 result;

        // First row
        result.M11 = matrix1.M11 + (matrix2.M11 - matrix1.M11) * amount;
        result.M12 = matrix1.M12 + (matrix2.M12 - matrix1.M12) * amount;
        result.M13 = matrix1.M13 + (matrix2.M13 - matrix1.M13) * amount;
        result.M14 = matrix1.M14 + (matrix2.M14 - matrix1.M14) * amount;

        // Second row
        result.M21 = matrix1.M21 + (matrix2.M21 - matrix1.M21) * amount;
        result.M22 = matrix1.M22 + (matrix2.M22 - matrix1.M22) * amount;
        result.M23 = matrix1.M23 + (matrix2.M23 - matrix1.M23) * amount;
        result.M24 = matrix1.M24 + (matrix2.M24 - matrix1.M24) * amount;

        // Third row
        result.M31 = matrix1.M31 + (matrix2.M31 - matrix1.M31) * amount;
        result.M32 = matrix1.M32 + (matrix2.M32 - matrix1.M32) * amount;
        result.M33 = matrix1.M33 + (matrix2.M33 - matrix1.M33) * amount;
        result.M34 = matrix1.M34 + (matrix2.M34 - matrix1.M34) * amount;

        // Fourth row
        result.M41 = matrix1.M41 + (matrix2.M41 - matrix1.M41) * amount;
        result.M42 = matrix1.M42 + (matrix2.M42 - matrix1.M42) * amount;
        result.M43 = matrix1.M43 + (matrix2.M43 - matrix1.M43) * amount;
        result.M44 = matrix1.M44 + (matrix2.M44 - matrix1.M44) * amount;

        return result;
    }

    /// <summary>
    /// Returns a new matrix with the negated elements of the given matrix.
    /// </summary>
    /// <param name="value">The source matrix.</param>
    /// <returns>The negated matrix.</returns>
    public static Matrix4x4 Negate(Matrix4x4 value)
    {
        Matrix4x4 result = new();
        result.M1 = -value.M1;
        result.M2 = -value.M2;
        result.M3 = -value.M3;
        result.M4 = -value.M4;
        return result;
    }

    /// <summary>
    /// Adds two matrices together.
    /// </summary>
    /// <param name="value1">The first source matrix.</param>
    /// <param name="value2">The second source matrix.</param>
    /// <returns>The resulting matrix.</returns>
    public static Matrix4x4 Add(Matrix4x4 value1, Matrix4x4 value2)
    {
        value1.M1 += value2.M1;
        value1.M2 += value2.M2;
        value1.M3 += value2.M3;
        value1.M4 += value2.M4;
        return value1;
    }

    /// <summary>
    /// Subtracts the second matrix from the first.
    /// </summary>
    /// <param name="value1">The first source matrix.</param>
    /// <param name="value2">The second source matrix.</param>
    /// <returns>The result of the subtraction.</returns>
    public static Matrix4x4 Subtract(Matrix4x4 value1, Matrix4x4 value2)
    {
        value1.M1 -= value2.M1;
        value1.M2 -= value2.M2;
        value1.M3 -= value2.M3;
        value1.M4 -= value2.M4;
        return value1;
    }

    /// <summary>
    /// Multiplies a matrix by another matrix.
    /// </summary>
    /// <param name="value1">The first source matrix.</param>
    /// <param name="value2">The second source matrix.</param>
    /// <returns>The result of the multiplication.</returns>
    public static Matrix4x4 Multiply(Matrix4x4 value1, Matrix4x4 value2)
    {
        Matrix4x4 result;

        // First row
        result.M11 = value1.M11 * value2.M11 + value1.M12 * value2.M21 + value1.M13 * value2.M31 + value1.M14 * value2.M41;
        result.M12 = value1.M11 * value2.M12 + value1.M12 * value2.M22 + value1.M13 * value2.M32 + value1.M14 * value2.M42;
        result.M13 = value1.M11 * value2.M13 + value1.M12 * value2.M23 + value1.M13 * value2.M33 + value1.M14 * value2.M43;
        result.M14 = value1.M11 * value2.M14 + value1.M12 * value2.M24 + value1.M13 * value2.M34 + value1.M14 * value2.M44;

        // Second row
        result.M21 = value1.M21 * value2.M11 + value1.M22 * value2.M21 + value1.M23 * value2.M31 + value1.M24 * value2.M41;
        result.M22 = value1.M21 * value2.M12 + value1.M22 * value2.M22 + value1.M23 * value2.M32 + value1.M24 * value2.M42;
        result.M23 = value1.M21 * value2.M13 + value1.M22 * value2.M23 + value1.M23 * value2.M33 + value1.M24 * value2.M43;
        result.M24 = value1.M21 * value2.M14 + value1.M22 * value2.M24 + value1.M23 * value2.M34 + value1.M24 * value2.M44;

        // Third row
        result.M31 = value1.M31 * value2.M11 + value1.M32 * value2.M21 + value1.M33 * value2.M31 + value1.M34 * value2.M41;
        result.M32 = value1.M31 * value2.M12 + value1.M32 * value2.M22 + value1.M33 * value2.M32 + value1.M34 * value2.M42;
        result.M33 = value1.M31 * value2.M13 + value1.M32 * value2.M23 + value1.M33 * value2.M33 + value1.M34 * value2.M43;
        result.M34 = value1.M31 * value2.M14 + value1.M32 * value2.M24 + value1.M33 * value2.M34 + value1.M34 * value2.M44;

        // Fourth row
        result.M41 = value1.M41 * value2.M11 + value1.M42 * value2.M21 + value1.M43 * value2.M31 + value1.M44 * value2.M41;
        result.M42 = value1.M41 * value2.M12 + value1.M42 * value2.M22 + value1.M43 * value2.M32 + value1.M44 * value2.M42;
        result.M43 = value1.M41 * value2.M13 + value1.M42 * value2.M23 + value1.M43 * value2.M33 + value1.M44 * value2.M43;
        result.M44 = value1.M41 * value2.M14 + value1.M42 * value2.M24 + value1.M43 * value2.M34 + value1.M44 * value2.M44;

        return result;
    }

    /// <summary>
    /// Multiplies a matrix by a scalar value.
    /// </summary>
    /// <param name="value1">The source matrix.</param>
    /// <param name="value2">The scaling factor.</param>
    /// <returns>The scaled matrix.</returns>
    public static Matrix4x4 Multiply(Matrix4x4 value1, double value2)
    {
        value1.M1 *= value2;
        value1.M2 *= value2;
        value1.M3 *= value2;
        value1.M4 *= value2;
        return value1;
    }

    /// <summary>
    /// Returns a new matrix with the negated elements of the given matrix.
    /// </summary>
    /// <param name="value">The source matrix.</param>
    /// <returns>The negated matrix.</returns>
    public static Matrix4x4 operator -(Matrix4x4 value) => Negate(value);

    /// <summary>
    /// Adds two matrices together.
    /// </summary>
    /// <param name="value1">The first source matrix.</param>
    /// <param name="value2">The second source matrix.</param>
    /// <returns>The resulting matrix.</returns>
    public static Matrix4x4 operator +(Matrix4x4 value1, Matrix4x4 value2) => Add(value1, value2);

    /// <summary>
    /// Subtracts the second matrix from the first.
    /// </summary>
    /// <param name="value1">The first source matrix.</param>
    /// <param name="value2">The second source matrix.</param>
    /// <returns>The result of the subtraction.</returns>
    public static Matrix4x4 operator -(Matrix4x4 value1, Matrix4x4 value2) => Subtract(value1, value2);

    /// <summary>
    /// Multiplies a matrix by another matrix.
    /// </summary>
    /// <param name="value1">The first source matrix.</param>
    /// <param name="value2">The second source matrix.</param>
    /// <returns>The result of the multiplication.</returns>
    public static Matrix4x4 operator *(Matrix4x4 value1, Matrix4x4 value2)
    {
        Matrix4x4 m;

        // First row
        m.M11 = value1.M11 * value2.M11 + value1.M12 * value2.M21 + value1.M13 * value2.M31 + value1.M14 * value2.M41;
        m.M12 = value1.M11 * value2.M12 + value1.M12 * value2.M22 + value1.M13 * value2.M32 + value1.M14 * value2.M42;
        m.M13 = value1.M11 * value2.M13 + value1.M12 * value2.M23 + value1.M13 * value2.M33 + value1.M14 * value2.M43;
        m.M14 = value1.M11 * value2.M14 + value1.M12 * value2.M24 + value1.M13 * value2.M34 + value1.M14 * value2.M44;

        // Second row
        m.M21 = value1.M21 * value2.M11 + value1.M22 * value2.M21 + value1.M23 * value2.M31 + value1.M24 * value2.M41;
        m.M22 = value1.M21 * value2.M12 + value1.M22 * value2.M22 + value1.M23 * value2.M32 + value1.M24 * value2.M42;
        m.M23 = value1.M21 * value2.M13 + value1.M22 * value2.M23 + value1.M23 * value2.M33 + value1.M24 * value2.M43;
        m.M24 = value1.M21 * value2.M14 + value1.M22 * value2.M24 + value1.M23 * value2.M34 + value1.M24 * value2.M44;

        // Third row
        m.M31 = value1.M31 * value2.M11 + value1.M32 * value2.M21 + value1.M33 * value2.M31 + value1.M34 * value2.M41;
        m.M32 = value1.M31 * value2.M12 + value1.M32 * value2.M22 + value1.M33 * value2.M32 + value1.M34 * value2.M42;
        m.M33 = value1.M31 * value2.M13 + value1.M32 * value2.M23 + value1.M33 * value2.M33 + value1.M34 * value2.M43;
        m.M34 = value1.M31 * value2.M14 + value1.M32 * value2.M24 + value1.M33 * value2.M34 + value1.M34 * value2.M44;

        // Fourth row
        m.M41 = value1.M41 * value2.M11 + value1.M42 * value2.M21 + value1.M43 * value2.M31 + value1.M44 * value2.M41;
        m.M42 = value1.M41 * value2.M12 + value1.M42 * value2.M22 + value1.M43 * value2.M32 + value1.M44 * value2.M42;
        m.M43 = value1.M41 * value2.M13 + value1.M42 * value2.M23 + value1.M43 * value2.M33 + value1.M44 * value2.M43;
        m.M44 = value1.M41 * value2.M14 + value1.M42 * value2.M24 + value1.M43 * value2.M34 + value1.M44 * value2.M44;

        return m;
    }

    /// <summary>
    /// Multiplies a matrix by a scalar value.
    /// </summary>
    /// <param name="value1">The source matrix.</param>
    /// <param name="value2">The scaling factor.</param>
    /// <returns>The scaled matrix.</returns>
    public static Matrix4x4 operator *(Matrix4x4 value1, double value2) => Multiply(value1, value2);

    /// <summary>
    /// Returns a boolean indicating whether the given two matrices are equal.
    /// </summary>
    /// <param name="value1">The first matrix to compare.</param>
    /// <param name="value2">The second matrix to compare.</param>
    /// <returns>True if the given matrices are equal; False otherwise.</returns>
    public static bool operator ==(Matrix4x4 value1, Matrix4x4 value2) => value1.Equals(value2);

    /// <summary>
    /// Returns a boolean indicating whether the given two matrices are not equal.
    /// </summary>
    /// <param name="value1">The first matrix to compare.</param>
    /// <param name="value2">The second matrix to compare.</param>
    /// <returns>True if the given matrices are not equal; False if they are equal.</returns>
    public static bool operator !=(Matrix4x4 value1, Matrix4x4 value2) => !value1.Equals(value2);

    /// <summary>
    /// Converts this matrix instance to a System.Numerics.Matrix4x4.
    /// </summary>
    /// <param name="matrix">The source matrix.</param>
    public static explicit operator System.Numerics.Matrix4x4(Matrix4x4 matrix)
    {
        return new System.Numerics.Matrix4x4(
            (float)matrix.M11, (float)matrix.M12, (float)matrix.M13, (float)matrix.M14,
            (float)matrix.M21, (float)matrix.M22, (float)matrix.M23, (float)matrix.M24,
            (float)matrix.M31, (float)matrix.M32, (float)matrix.M33, (float)matrix.M34,
            (float)matrix.M41, (float)matrix.M42, (float)matrix.M43, (float)matrix.M44);
    }

    /// <summary>
    /// Returns a boolean indicating whether this matrix instance is equal to the other given matrix.
    /// </summary>
    /// <param name="other">The matrix to compare this instance to.</param>
    /// <returns>True if the matrices are equal; False otherwise.</returns>
    public bool Equals(Matrix4x4 other) =>
        (MathD.ApproximatelyEquals(M11, other.M11) && MathD.ApproximatelyEquals(M22, other.M22) && MathD.ApproximatelyEquals(M33, other.M33) && MathD.ApproximatelyEquals(M44, other.M44) && // Check diagonal element first for early out.
         MathD.ApproximatelyEquals(M12, other.M12) && MathD.ApproximatelyEquals(M13, other.M13) && MathD.ApproximatelyEquals(M14, other.M14) &&
         MathD.ApproximatelyEquals(M21, other.M21) && MathD.ApproximatelyEquals(M23, other.M23) && MathD.ApproximatelyEquals(M24, other.M24) &&
         MathD.ApproximatelyEquals(M31, other.M31) && MathD.ApproximatelyEquals(M32, other.M32) && MathD.ApproximatelyEquals(M34, other.M34) &&
         MathD.ApproximatelyEquals(M41, other.M41) && MathD.ApproximatelyEquals(M42, other.M42) && MathD.ApproximatelyEquals(M43, other.M43));

    /// <summary>
    /// Returns a boolean indicating whether the given Object is equal to this matrix instance.
    /// </summary>
    /// <param name="obj">The Object to compare against.</param>
    /// <returns>True if the Object is equal to this matrix; False otherwise.</returns>
    public override bool Equals(object? obj) => obj is Matrix4x4 x && Equals(x);

    /// <summary>
    /// Returns a String representing this matrix instance.
    /// </summary>
    /// <returns>The string representation.</returns>
    public override string ToString()
    {
        CultureInfo ci = CultureInfo.CurrentCulture;

        return String.Format(ci, "{{ {{M11:{0} M12:{1} M13:{2} M14:{3}}} {{M21:{4} M22:{5} M23:{6} M24:{7}}} {{M31:{8} M32:{9} M33:{10} M34:{11}}} {{M41:{12} M42:{13} M43:{14} M44:{15}}} }}",
            M11.ToString(ci), M12.ToString(ci), M13.ToString(ci), M14.ToString(ci),
            M21.ToString(ci), M22.ToString(ci), M23.ToString(ci), M24.ToString(ci),
            M31.ToString(ci), M32.ToString(ci), M33.ToString(ci), M34.ToString(ci),
            M41.ToString(ci), M42.ToString(ci), M43.ToString(ci), M44.ToString(ci));
    }
    public string ToMultiLineString()
    {
        var ci = CultureInfo.CurrentCulture;


        var sb = new StringBuilder();
        sb.AppendLine($"{M11.ToString(ci),-19} {M12.ToString(ci),-19} {M13.ToString(ci),-19} {M14.ToString(ci),-19}");
        sb.AppendLine($"{M21.ToString(ci),-19} {M22.ToString(ci),-19} {M23.ToString(ci),-19} {M24.ToString(ci),-19}");
        sb.AppendLine($"{M31.ToString(ci),-19} {M32.ToString(ci),-19} {M33.ToString(ci),-19} {M34.ToString(ci),-19}");
        sb.AppendLine($"{M41.ToString(ci),-19} {M42.ToString(ci),-19} {M43.ToString(ci),-19} {M44.ToString(ci),-19}");
        return sb.ToString();
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() =>
        M11.GetHashCode() + M12.GetHashCode() + M13.GetHashCode() + M14.GetHashCode() +
        M21.GetHashCode() + M22.GetHashCode() + M23.GetHashCode() + M24.GetHashCode() +
        M31.GetHashCode() + M32.GetHashCode() + M33.GetHashCode() + M34.GetHashCode() +
        M41.GetHashCode() + M42.GetHashCode() + M43.GetHashCode() + M44.GetHashCode();
}
