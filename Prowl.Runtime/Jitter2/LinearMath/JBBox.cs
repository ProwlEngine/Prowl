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

namespace Jitter2.LinearMath;

/// <summary>
/// Represents an axis-aligned bounding box (AABB), a rectangular bounding box whose edges are parallel to the coordinate axes.
/// </summary>
public struct JBBox
{
    public const float Epsilon = 1e-12f;

    public enum ContainmentType
    {
        Disjoint,
        Contains,
        Intersects
    }

    public JVector Min;
    public JVector Max;

    public static readonly JBBox LargeBox;

    public static readonly JBBox SmallBox;

    static JBBox()
    {
        LargeBox.Min = new JVector(float.MinValue);
        LargeBox.Max = new JVector(float.MaxValue);
        SmallBox.Min = new JVector(float.MaxValue);
        SmallBox.Max = new JVector(float.MinValue);
    }

    public JBBox(JVector min, JVector max)
    {
        Min = min;
        Max = max;
    }

    internal void InverseTransform(ref JVector position, ref JMatrix orientation)
    {
        JVector.Subtract(Max, position, out Max);
        JVector.Subtract(Min, position, out Min);

        JVector.Add(Max, Min, out JVector center);
        center.X *= 0.5f;
        center.Y *= 0.5f;
        center.Z *= 0.5f;

        JVector.Subtract(Max, Min, out JVector halfExtents);
        halfExtents.X *= 0.5f;
        halfExtents.Y *= 0.5f;
        halfExtents.Z *= 0.5f;

        JVector.TransposedTransform(center, orientation, out center);

        JMatrix.Absolute(orientation, out JMatrix abs);
        JVector.TransposedTransform(halfExtents, abs, out halfExtents);

        JVector.Add(center, halfExtents, out Max);
        JVector.Subtract(center, halfExtents, out Min);
    }

    public void Transform(ref JMatrix orientation)
    {
        JVector halfExtents = 0.5f * (Max - Min);
        JVector center = 0.5f * (Max + Min);

        JVector.Transform(center, orientation, out center);

        JMatrix.Absolute(orientation, out var abs);
        JVector.Transform(halfExtents, abs, out halfExtents);

        Max = center + halfExtents;
        Min = center - halfExtents;
    }

    private bool Intersect1D(float start, float dir, float min, float max,
        ref float enter, ref float exit)
    {
        if (dir * dir < Epsilon * Epsilon) return start >= min && start <= max;

        float t0 = (min - start) / dir;
        float t1 = (max - start) / dir;

        if (t0 > t1)
        {
            (t0, t1) = (t1, t0);
        }

        if (t0 > exit || t1 < enter) return false;

        if (t0 > enter) enter = t0;
        if (t1 < exit) exit = t1;
        return true;
    }

    public bool SegmentIntersect(in JVector origin, in JVector direction)
    {
        float enter = 0.0f, exit = 1.0f;

        if (!Intersect1D(origin.X, direction.X, Min.X, Max.X, ref enter, ref exit))
            return false;

        if (!Intersect1D(origin.Y, direction.Y, Min.Y, Max.Y, ref enter, ref exit))
            return false;

        if (!Intersect1D(origin.Z, direction.Z, Min.Z, Max.Z, ref enter, ref exit))
            return false;

        return true;
    }

    public bool RayIntersect(in JVector origin, in JVector direction)
    {
        float enter = 0.0f, exit = float.MaxValue;

        if (!Intersect1D(origin.X, direction.X, Min.X, Max.X, ref enter, ref exit))
            return false;

        if (!Intersect1D(origin.Y, direction.Y, Min.Y, Max.Y, ref enter, ref exit))
            return false;

        if (!Intersect1D(origin.Z, direction.Z, Min.Z, Max.Z, ref enter, ref exit))
            return false;

        return true;
    }

    public bool RayIntersect(in JVector origin, in JVector direction, out float enter)
    {
        enter = 0.0f;
        float exit = float.MaxValue;

        if (!Intersect1D(origin.X, direction.X, Min.X, Max.X, ref enter, ref exit))
            return false;

        if (!Intersect1D(origin.Y, direction.Y, Min.Y, Max.Y, ref enter, ref exit))
            return false;

        if (!Intersect1D(origin.Z, direction.Z, Min.Z, Max.Z, ref enter, ref exit))
            return false;

        return true;
    }

    public ContainmentType Contains(in JVector point)
    {
        return Min.X <= point.X && point.X <= Max.X &&
               Min.Y <= point.Y && point.Y <= Max.Y &&
               Min.Z <= point.Z && point.Z <= Max.Z
            ? ContainmentType.Contains
            : ContainmentType.Disjoint;
    }

    public void GetCorners(JVector[] corners)
    {
        corners[0].Set(Min.X, Max.Y, Max.Z);
        corners[1].Set(Max.X, Max.Y, Max.Z);
        corners[2].Set(Max.X, Min.Y, Max.Z);
        corners[3].Set(Min.X, Min.Y, Max.Z);
        corners[4].Set(Min.X, Max.Y, Min.Z);
        corners[5].Set(Max.X, Max.Y, Min.Z);
        corners[6].Set(Max.X, Min.Y, Min.Z);
        corners[7].Set(Min.X, Min.Y, Min.Z);
    }

    public void AddPoint(in JVector point)
    {
        JVector.Max(Max, point, out Max);
        JVector.Min(Min, point, out Min);
    }

    public static JBBox CreateFromPoints(JVector[] points)
    {
        JVector vector3 = new JVector(float.MaxValue);
        JVector vector2 = new JVector(float.MinValue);

        for (int i = 0; i < points.Length; i++)
        {
            JVector.Min(vector3, points[i], out vector3);
            JVector.Max(vector2, points[i], out vector2);
        }

        return new JBBox(vector3, vector2);
    }

    public readonly ContainmentType Contains(in JBBox box)
    {
        ContainmentType result = ContainmentType.Disjoint;
        if (Max.X >= box.Min.X && Min.X <= box.Max.X && Max.Y >= box.Min.Y && Min.Y <= box.Max.Y &&
            Max.Z >= box.Min.Z && Min.Z <= box.Max.Z)
        {
            result = Min.X <= box.Min.X && box.Max.X <= Max.X && Min.Y <= box.Min.Y && box.Max.Y <= Max.Y &&
                     Min.Z <= box.Min.Z && box.Max.Z <= Max.Z
                ? ContainmentType.Contains
                : ContainmentType.Intersects;
        }

        return result;
    }

    public readonly bool NotDisjoint(in JBBox box)
    {
        return Max.X >= box.Min.X && Min.X <= box.Max.X && Max.Y >= box.Min.Y && Min.Y <= box.Max.Y &&
               Max.Z >= box.Min.Z && Min.Z <= box.Max.Z;
    }

    public readonly bool Disjoint(in JBBox box)
    {
        return !(Max.X >= box.Min.X && Min.X <= box.Max.X && Max.Y >= box.Min.Y && Min.Y <= box.Max.Y &&
                 Max.Z >= box.Min.Z && Min.Z <= box.Max.Z);
    }

    public static JBBox CreateMerged(in JBBox original, in JBBox additional)
    {
        CreateMerged(original, additional, out JBBox result);
        return result;
    }

    public static void CreateMerged(in JBBox original, in JBBox additional, out JBBox result)
    {
        JVector.Min(original.Min, additional.Min, out result.Min);
        JVector.Max(original.Max, additional.Max, out result.Max);
    }

    public readonly JVector Center => (Min + Max) * (1.0f / 2.0f);

    internal readonly float Perimeter =>
        2.0f * ((Max.X - Min.X) * (Max.Y - Min.Y) +
                (Max.X - Min.X) * (Max.Z - Min.Z) +
                (Max.Z - Min.Z) * (Max.Y - Min.Y));
}