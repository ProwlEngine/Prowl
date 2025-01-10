// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

#region License
/*
MIT License
Copyright © 2006 The Mono.Xna Team

All rights reserved.

Authors:
Olivier Dufour (Duff)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
#endregion License

using System;
using System.Text;

namespace Prowl.Vector;

public class BoundingFrustum : IEquatable<BoundingFrustum>
{
    #region Private Fields

    private Matrix4x4 matrix;
    private readonly Vector3[] corners = new Vector3[CornerCount];
    private readonly Plane[] planes = new Plane[PlaneCount];

    private const int PlaneCount = 6;

    #endregion Private Fields

    #region Public Fields
    public const int CornerCount = 8;
    #endregion

    #region Public Constructors

    public BoundingFrustum(Matrix4x4 value)
    {
        matrix = value;
        CreatePlanes();
        CreateCorners();
    }

    #endregion Public Constructors


    #region Public Properties

    public Matrix4x4 Matrix
    {
        get { return matrix; }
        set
        {
            matrix = value;
            CreatePlanes();  // FIXME: The odds are the planes will be used a lot more often than the matrix
            CreateCorners(); // is updated, so this should help performance. I hope ;)
        }
    }

    public Plane Near
    {
        get { return planes[0]; }
    }

    public Plane Far
    {
        get { return planes[1]; }
    }

    public Plane Left
    {
        get { return planes[2]; }
    }

    public Plane Right
    {
        get { return planes[3]; }
    }

    public Plane Top
    {
        get { return planes[4]; }
    }

    public Plane Bottom
    {
        get { return planes[5]; }
    }

    #endregion Public Properties


    #region Public Methods

    public static bool operator ==(BoundingFrustum a, BoundingFrustum b)
    {
        if (Equals(a, null))
            return (Equals(b, null));

        if (Equals(b, null))
            return (Equals(a, null));

        return a.matrix == (b.matrix);
    }

    public static bool operator !=(BoundingFrustum a, BoundingFrustum b)
    {
        return !(a == b);
    }

    public ContainmentType Contains(Bounds box)
    {
        var result = default(ContainmentType);
        Contains(ref box, out result);
        return result;
    }

    public void Contains(ref Bounds box, out ContainmentType result)
    {
        var intersects = false;
        for (var i = 0; i < PlaneCount; ++i)
        {
            var planeIntersectionType = default(PlaneIntersectionType);
            box.Intersects(ref planes[i], out planeIntersectionType);
            switch (planeIntersectionType)
            {
                case PlaneIntersectionType.Front:
                    result = ContainmentType.Disjoint;
                    return;
                case PlaneIntersectionType.Intersecting:
                    intersects = true;
                    break;
            }
        }
        result = intersects ? ContainmentType.Intersects : ContainmentType.Contains;
    }

    public ContainmentType Contains(BoundingFrustum frustum)
    {
        if (this == frustum)                 // We check to see if the two frustums are equal
            return ContainmentType.Contains; // If they are, there's no need to go any further.

        var intersects = false;
        for (var i = 0; i < PlaneCount; ++i)
        {
            PlaneIntersectionType planeIntersectionType;
            frustum.Intersects(ref planes[i], out planeIntersectionType);
            switch (planeIntersectionType)
            {
                case PlaneIntersectionType.Front:
                    return ContainmentType.Disjoint;
                case PlaneIntersectionType.Intersecting:
                    intersects = true;
                    break;
            }
        }
        return intersects ? ContainmentType.Intersects : ContainmentType.Contains;
    }

    public ContainmentType Contains(Vector3 point)
    {
        var result = default(ContainmentType);
        Contains(ref point, out result);
        return result;
    }

    public void Contains(ref Vector3 point, out ContainmentType result)
    {
        for (var i = 0; i < PlaneCount; ++i)
        {
            // TODO: we might want to inline this for performance reasons
            if (planes[i].GetSide(point))
            {
                result = ContainmentType.Disjoint;
                return;
            }
        }
        result = ContainmentType.Contains;
    }

    public bool Equals(BoundingFrustum? other)
    {
        return (this == other);
    }

    public override bool Equals(object? obj)
    {
        BoundingFrustum f = obj as BoundingFrustum;
        return (Equals(f, null)) ? false : (this == f);
    }

    public Vector3[] GetCorners()
    {
        return (Vector3[])corners.Clone();
    }

    public void GetCorners(Vector3[] corners)
    {
        ArgumentNullException.ThrowIfNull(corners);
        if (corners.Length < CornerCount) throw new ArgumentOutOfRangeException(nameof(corners));

        this.corners.CopyTo(corners, 0);
    }

    public override int GetHashCode()
    {
        return matrix.GetHashCode();
    }

    public bool Intersects(Bounds box)
    {
        var result = false;
        Intersects(ref box, out result);
        return result;
    }

    public void Intersects(ref Bounds box, out bool result)
    {
        var containment = default(ContainmentType);
        Contains(ref box, out containment);
        result = containment != ContainmentType.Disjoint;
    }

    public bool Intersects(BoundingFrustum frustum)
    {
        return Contains(frustum) != ContainmentType.Disjoint;
    }

    public PlaneIntersectionType Intersects(Plane plane)
    {
        PlaneIntersectionType result;
        Intersects(ref plane, out result);
        return result;
    }

    public void Intersects(ref Plane plane, out PlaneIntersectionType result)
    {
        result = plane.Intersects(ref corners[0]);
        for (int i = 1; i < corners.Length; i++)
            if (plane.Intersects(ref corners[i]) != result)
                result = PlaneIntersectionType.Intersecting;
    }

    /*
    public Nullable<float> Intersects(Ray ray)
    {
        throw new NotImplementedException();
    }

    public void Intersects(ref Ray ray, out Nullable<float> result)
    {
        throw new NotImplementedException();
    }
    */

    internal string DebugDisplayString
    {
        get
        {
            return string.Concat(
                "Near( ", planes[0].DebugDisplayString, " )  \r\n",
                "Far( ", planes[1].DebugDisplayString, " )  \r\n",
                "Left( ", planes[2].DebugDisplayString, " )  \r\n",
                "Right( ", planes[3].DebugDisplayString, " )  \r\n",
                "Top( ", planes[4].DebugDisplayString, " )  \r\n",
                "Bottom( ", planes[5].DebugDisplayString, " )  "
            );
        }
    }

    public override string ToString()
    {
        StringBuilder sb = new StringBuilder(256);
        sb.Append("{Near:");
        sb.Append(planes[0].ToString());
        sb.Append(" Far:");
        sb.Append(planes[1].ToString());
        sb.Append(" Left:");
        sb.Append(planes[2].ToString());
        sb.Append(" Right:");
        sb.Append(planes[3].ToString());
        sb.Append(" Top:");
        sb.Append(planes[4].ToString());
        sb.Append(" Bottom:");
        sb.Append(planes[5].ToString());
        sb.Append("}");
        return sb.ToString();
    }

    #endregion Public Methods


    #region Private Methods

    private void CreateCorners()
    {
        IntersectionPoint(ref planes[0], ref planes[2], ref planes[4], out corners[0]);
        IntersectionPoint(ref planes[0], ref planes[3], ref planes[4], out corners[1]);
        IntersectionPoint(ref planes[0], ref planes[3], ref planes[5], out corners[2]);
        IntersectionPoint(ref planes[0], ref planes[2], ref planes[5], out corners[3]);
        IntersectionPoint(ref planes[1], ref planes[2], ref planes[4], out corners[4]);
        IntersectionPoint(ref planes[1], ref planes[3], ref planes[4], out corners[5]);
        IntersectionPoint(ref planes[1], ref planes[3], ref planes[5], out corners[6]);
        IntersectionPoint(ref planes[1], ref planes[2], ref planes[5], out corners[7]);
    }

    private void CreatePlanes()
    {
        planes[0] = new Plane(-matrix.M13, -matrix.M23, -matrix.M33, -matrix.M43);
        planes[1] = new Plane(matrix.M13 - matrix.M14, matrix.M23 - matrix.M24, matrix.M33 - matrix.M34, matrix.M43 - matrix.M44);
        planes[2] = new Plane(-matrix.M14 - matrix.M11, -matrix.M24 - matrix.M21, -matrix.M34 - matrix.M31, -matrix.M44 - matrix.M41);
        planes[3] = new Plane(matrix.M11 - matrix.M14, matrix.M21 - matrix.M24, matrix.M31 - matrix.M34, matrix.M41 - matrix.M44);
        planes[4] = new Plane(matrix.M12 - matrix.M14, matrix.M22 - matrix.M24, matrix.M32 - matrix.M34, matrix.M42 - matrix.M44);
        planes[5] = new Plane(-matrix.M14 - matrix.M12, -matrix.M24 - matrix.M22, -matrix.M34 - matrix.M32, -matrix.M44 - matrix.M42);

        NormalizePlane(ref planes[0]);
        NormalizePlane(ref planes[1]);
        NormalizePlane(ref planes[2]);
        NormalizePlane(ref planes[3]);
        NormalizePlane(ref planes[4]);
        NormalizePlane(ref planes[5]);
    }

    private static void IntersectionPoint(ref Plane a, ref Plane b, ref Plane c, out Vector3 result)
    {
        // Formula used
        //                d1 ( N2 * N3 ) + d2 ( N3 * N1 ) + d3 ( N1 * N2 )
        //P =   -------------------------------------------------------------------------
        //                             N1 . ( N2 * N3 )
        //
        // Note: N refers to the normal, d refers to the displacement. '.' means dot product. '*' means cross product

        Vector3 v1, v2, v3;
        Vector3 cross;

        cross = Vector3.Cross(b.normal, c.normal);

        double f = Vector3.Dot(a.normal, cross);
        f *= -1.0f;

        cross = Vector3.Cross(b.normal, c.normal);
        v1 = cross * a.distance;
        //v1 = (a.D * (Vector3.Cross(b.Normal, c.Normal)));


        cross = Vector3.Cross(c.normal, a.normal);
        v2 = cross * b.distance;
        //v2 = (b.D * (Vector3.Cross(c.Normal, a.Normal)));


        cross = Vector3.Cross(a.normal, b.normal);
        v3 = cross * c.distance;
        //v3 = (c.D * (Vector3.Cross(a.Normal, b.Normal)));

        result.x = (v1.x + v2.x + v3.x) / f;
        result.y = (v1.y + v2.y + v3.y) / f;
        result.z = (v1.z + v2.z + v3.z) / f;
    }

    private void NormalizePlane(ref Plane p)
    {
        double factor = 1 / p.normal.magnitude;
        p.normal.x *= factor;
        p.normal.y *= factor;
        p.normal.z *= factor;
        p.distance *= factor;
    }

    #endregion
}
