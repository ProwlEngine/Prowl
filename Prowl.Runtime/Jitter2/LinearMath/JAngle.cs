/*
 * Copyright (c) Thorben Linneweber and others
 *
 * Permission is hereby granted, free of charge, to any person obtaining
 * a copy of this software and associated documentation files (the
 * "Software"), to deal in the Software without restriction, including
 * without limitation the rights to use, copy, modify, merge, publish,
 * distribute, sublicense, and/or sell copies of the Software, and to
 * permit persons to whom the Software is furnished to do so, subject to
 * the following conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
 * LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
 * OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
 * WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System;

namespace Jitter2.LinearMath;

/// <summary>
/// A 32-bit floating point variable representing an angle. This structure exists to eliminate
/// ambiguity between radians and degrees in the Jitter API.
/// </summary>
public struct JAngle : IEquatable<JAngle>
{
    public float Radiant { get; set; }

    public readonly override bool Equals(object? obj)
    {
        return obj is JAngle other && Equals(other);
    }

    public readonly bool Equals(JAngle p)
    {
        return p.Radiant == Radiant;
    }

    public readonly override int GetHashCode()
    {
        return Radiant.GetHashCode();
    }

    public float Degree
    {
        readonly get => Radiant / MathF.PI * 180.0f;
        set => Radiant = value / 180.0f * MathF.PI;
    }

    public static JAngle FromRadiant(float rad)
    {
        return new JAngle { Radiant = rad };
    }

    public static JAngle FromDegree(float deg)
    {
        return new JAngle { Degree = deg };
    }

    public static explicit operator JAngle(float angle)
    {
        return FromRadiant(angle);
    }

    public static JAngle operator -(JAngle a)
    {
        return FromRadiant(-a.Radiant);
    }

    public static JAngle operator +(JAngle a, JAngle b)
    {
        return FromRadiant(a.Radiant + b.Radiant);
    }

    public static JAngle operator -(JAngle a, JAngle b)
    {
        return FromRadiant(a.Radiant - b.Radiant);
    }

    public static bool operator ==(JAngle l, JAngle r)
    {
        return (float)l == (float)r;
    }

    public static bool operator !=(JAngle l, JAngle r)
    {
        return (float)l != (float)r;
    }

    public static bool operator <(JAngle l, JAngle r)
    {
        return (float)l < (float)r;
    }

    public static bool operator >(JAngle l, JAngle r)
    {
        return (float)l > (float)r;
    }

    public static bool operator >=(JAngle l, JAngle r)
    {
        return (float)l >= (float)r;
    }

    public static bool operator <=(JAngle l, JAngle r)
    {
        return (float)l <= (float)r;
    }

    public static explicit operator float(JAngle angle)
    {
        return angle.Radiant;
    }
}