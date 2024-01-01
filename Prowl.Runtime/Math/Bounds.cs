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
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace Prowl.Runtime
{
    public enum ContainmentType
    {
        Disjoint,
        Contains,
        Intersects
    }

    public struct Bounds : IEquatable<Bounds>
    {

        #region Public Fields

        [DataMember]
        public Vector3 Min;

        [DataMember]
        public Vector3 Max;

        public const int CornerCount = 8;

        #endregion Public Fields


        #region Public Constructors

        public Bounds(Vector3 min, Vector3 max)
        {
            this.Min = min;
            this.Max = max;
        }

        #endregion Public Constructors


        #region Public Methods

        public ContainmentType Contains(Bounds box)
        {
            //test if all corner is in the same side of a face by just checking min and max
            if (box.Max.x < Min.x
                || box.Min.x > Max.x
                || box.Max.y < Min.y
                || box.Min.y > Max.y
                || box.Max.z < Min.z
                || box.Min.z > Max.z)
                return ContainmentType.Disjoint;


            if (box.Min.x >= Min.x
                && box.Max.x <= Max.x
                && box.Min.y >= Min.y
                && box.Max.y <= Max.y
                && box.Min.z >= Min.z
                && box.Max.z <= Max.z)
                return ContainmentType.Contains;

            return ContainmentType.Intersects;
        }

        public void Contains(ref Bounds box, out ContainmentType result)
        {
            result = Contains(box);
        }

        public ContainmentType Contains(BoundingFrustum frustum)
        {
            //TODO: bad done here need a fix. 
            //Because question is not frustum contain box but reverse and this is not the same
            int i;
            ContainmentType contained;
            Vector3[] corners = frustum.GetCorners();

            // First we check if frustum is in box
            for (i = 0; i < corners.Length; i++)
            {
                this.Contains(ref corners[i], out contained);
                if (contained == ContainmentType.Disjoint)
                    break;
            }

            if (i == corners.Length) // This means we checked all the corners and they were all contain or instersect
                return ContainmentType.Contains;

            if (i != 0)             // if i is not equal to zero, we can fastpath and say that this box intersects
                return ContainmentType.Intersects;


            // If we get here, it means the first (and only) point we checked was actually contained in the frustum.
            // So we assume that all other points will also be contained. If one of the points is disjoint, we can
            // exit immediately saying that the result is Intersects
            i++;
            for (; i < corners.Length; i++)
            {
                this.Contains(ref corners[i], out contained);
                if (contained != ContainmentType.Contains)
                    return ContainmentType.Intersects;

            }

            // If we get here, then we know all the points were actually contained, therefore result is Contains
            return ContainmentType.Contains;
        }

        public ContainmentType Contains(Vector3 point)
        {
            ContainmentType result;
            this.Contains(ref point, out result);
            return result;
        }

        public void Contains(ref Vector3 point, out ContainmentType result)
        {
            //first we get if point is out of box
            if (point.x < this.Min.x
                || point.x > this.Max.x
                || point.y < this.Min.y
                || point.y > this.Max.y
                || point.z < this.Min.z
                || point.z > this.Max.z)
            {
                result = ContainmentType.Disjoint;
            }//or if point is on box because coordonate of point is lesser or equal
            else if (point.x == this.Min.x
                || point.x == this.Max.x
                || point.y == this.Min.y
                || point.y == this.Max.y
                || point.z == this.Min.z
                || point.z == this.Max.z)
                result = ContainmentType.Intersects;
            else
                result = ContainmentType.Contains;
        }

        private static readonly Vector3 MaxVector3 = new Vector3(float.MaxValue);
        private static readonly Vector3 MinVector3 = new Vector3(float.MinValue);

        /// <summary>
        /// Create a bounding box from the given list of points.
        /// </summary>
        /// <param name="points">The list of Vector3 instances defining the point cloud to bound</param>
        /// <returns>A bounding box that encapsulates the given point cloud.</returns>
        /// <exception cref="System.ArgumentException">Thrown if the given list has no points.</exception>
        public static Bounds CreateFromPoints(IEnumerable<Vector3> points)
        {
            if (points == null)
                throw new ArgumentNullException();

            var empty = true;
            var minVec = MaxVector3;
            var maxVec = MinVector3;
            foreach (var ptVector in points)
            {
                minVec.x = (minVec.x < ptVector.x) ? minVec.x : ptVector.x;
                minVec.y = (minVec.y < ptVector.y) ? minVec.y : ptVector.y;
                minVec.z = (minVec.z < ptVector.z) ? minVec.z : ptVector.z;

                maxVec.x = (maxVec.x > ptVector.x) ? maxVec.x : ptVector.x;
                maxVec.y = (maxVec.y > ptVector.y) ? maxVec.y : ptVector.y;
                maxVec.z = (maxVec.z > ptVector.z) ? maxVec.z : ptVector.z;

                empty = false;
            }
            if (empty)
                throw new ArgumentException();

            return new Bounds(minVec, maxVec);
        }

        public static Bounds CreateMerged(Bounds original, Bounds additional)
        {
            Bounds result;
            CreateMerged(ref original, ref additional, out result);
            return result;
        }

        public static void CreateMerged(ref Bounds original, ref Bounds additional, out Bounds result)
        {
            result.Min.x = Math.Min(original.Min.x, additional.Min.x);
            result.Min.y = Math.Min(original.Min.y, additional.Min.y);
            result.Min.z = Math.Min(original.Min.z, additional.Min.z);
            result.Max.x = Math.Max(original.Max.x, additional.Max.x);
            result.Max.y = Math.Max(original.Max.y, additional.Max.y);
            result.Max.z = Math.Max(original.Max.z, additional.Max.z);
        }

        public bool Equals(Bounds other)
        {
            return (this.Min == other.Min) && (this.Max == other.Max);
        }

        public override bool Equals(object obj)
        {
            return (obj is Bounds) ? this.Equals((Bounds)obj) : false;
        }

        public Vector3[] GetCorners()
        {
            return new Vector3[] {
                new Vector3(this.Min.x, this.Max.y, this.Max.z),
                new Vector3(this.Max.x, this.Max.y, this.Max.z),
                new Vector3(this.Max.x, this.Min.y, this.Max.z),
                new Vector3(this.Min.x, this.Min.y, this.Max.z),
                new Vector3(this.Min.x, this.Max.y, this.Min.z),
                new Vector3(this.Max.x, this.Max.y, this.Min.z),
                new Vector3(this.Max.x, this.Min.y, this.Min.z),
                new Vector3(this.Min.x, this.Min.y, this.Min.z)
            };
        }

        public void GetCorners(Vector3[] corners)
        {
            if (corners == null)
            {
                throw new ArgumentNullException("corners");
            }
            if (corners.Length < 8)
            {
                throw new ArgumentOutOfRangeException("corners", "Not Enought Corners");
            }
            corners[0].x = this.Min.x;
            corners[0].y = this.Max.y;
            corners[0].z = this.Max.z;
            corners[1].x = this.Max.x;
            corners[1].y = this.Max.y;
            corners[1].z = this.Max.z;
            corners[2].x = this.Max.x;
            corners[2].y = this.Min.y;
            corners[2].z = this.Max.z;
            corners[3].x = this.Min.x;
            corners[3].y = this.Min.y;
            corners[3].z = this.Max.z;
            corners[4].x = this.Min.x;
            corners[4].y = this.Max.y;
            corners[4].z = this.Min.z;
            corners[5].x = this.Max.x;
            corners[5].y = this.Max.y;
            corners[5].z = this.Min.z;
            corners[6].x = this.Max.x;
            corners[6].y = this.Min.y;
            corners[6].z = this.Min.z;
            corners[7].x = this.Min.x;
            corners[7].y = this.Min.y;
            corners[7].z = this.Min.z;
        }

        public override int GetHashCode()
        {
            return this.Min.GetHashCode() + this.Max.GetHashCode();
        }

        public bool Intersects(Bounds box)
        {
            bool result;
            Intersects(ref box, out result);
            return result;
        }

        public void Intersects(ref Bounds box, out bool result)
        {
            if ((this.Max.x >= box.Min.x) && (this.Min.x <= box.Max.x))
            {
                if ((this.Max.y < box.Min.y) || (this.Min.y > box.Max.y))
                {
                    result = false;
                    return;
                }

                result = (this.Max.z >= box.Min.z) && (this.Min.z <= box.Max.z);
                return;
            }

            result = false;
            return;
        }

        public bool Intersects(BoundingFrustum frustum)
        {
            return frustum.Intersects(this);
        }

        public PlaneIntersectionType Intersects(Plane plane)
        {
            PlaneIntersectionType result;
            Intersects(ref plane, out result);
            return result;
        }

        public void Intersects(ref Plane plane, out PlaneIntersectionType result)
        {
            // See http://zach.in.tu-clausthal.de/teaching/cg_literatur/lighthouse3d_view_frustum_culling/index.html

            Vector3 positiveVertex;
            Vector3 negativeVertex;

            if (plane.Normal.x >= 0)
            {
                positiveVertex.x = Max.x;
                negativeVertex.x = Min.x;
            }
            else
            {
                positiveVertex.x = Min.x;
                negativeVertex.x = Max.x;
            }

            if (plane.Normal.y >= 0)
            {
                positiveVertex.y = Max.y;
                negativeVertex.y = Min.y;
            }
            else
            {
                positiveVertex.y = Min.y;
                negativeVertex.y = Max.y;
            }

            if (plane.Normal.z >= 0)
            {
                positiveVertex.z = Max.z;
                negativeVertex.z = Min.z;
            }
            else
            {
                positiveVertex.z = Min.z;
                negativeVertex.z = Max.z;
            }

            // Inline Vector3.Dot(plane.Normal, negativeVertex) + plane.D;
            var distance = plane.Normal.x * negativeVertex.x + plane.Normal.y * negativeVertex.y + plane.Normal.z * negativeVertex.z + plane.D;
            if (distance > 0)
            {
                result = PlaneIntersectionType.Front;
                return;
            }

            // Inline Vector3.Dot(plane.Normal, positiveVertex) + plane.D;
            distance = plane.Normal.x * positiveVertex.x + plane.Normal.y * positiveVertex.y + plane.Normal.z * positiveVertex.z + plane.D;
            if (distance < 0)
            {
                result = PlaneIntersectionType.Back;
                return;
            }

            result = PlaneIntersectionType.Intersecting;
        }

        public Nullable<double> Intersects(Ray ray)
        {
            return ray.Intersects(this);
        }

        public void Intersects(ref Ray ray, out Nullable<double> result)
        {
            result = Intersects(ray);
        }

        public static bool operator ==(Bounds a, Bounds b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(Bounds a, Bounds b)
        {
            return !a.Equals(b);
        }

        internal string DebugDisplayString
        {
            get
            {
                return string.Concat(
                    "Min( ", this.Min.ToString(), " )  \r\n",
                    "Max( ", this.Max.ToString(), " )"
                    );
            }
        }

        public override string ToString()
        {
            return "{{Min:" + this.Min.ToString() + " Max:" + this.Max.ToString() + "}}";
        }

        #endregion Public Methods
    }

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
            this.matrix = value;
            this.CreatePlanes();
            this.CreateCorners();
        }

        #endregion Public Constructors


        #region Public Properties

        public Matrix4x4 Matrix
        {
            get { return this.matrix; }
            set
            {
                this.matrix = value;
                this.CreatePlanes();    // FIXME: The odds are the planes will be used a lot more often than the matrix
                this.CreateCorners();   // is updated, so this should help performance. I hope ;)
            }
        }

        public Plane Near
        {
            get { return this.planes[0]; }
        }

        public Plane Far
        {
            get { return this.planes[1]; }
        }

        public Plane Left
        {
            get { return this.planes[2]; }
        }

        public Plane Right
        {
            get { return this.planes[3]; }
        }

        public Plane Top
        {
            get { return this.planes[4]; }
        }

        public Plane Bottom
        {
            get { return this.planes[5]; }
        }

        #endregion Public Properties


        #region Public Methods

        public static bool operator ==(BoundingFrustum a, BoundingFrustum b)
        {
            if (object.Equals(a, null))
                return (object.Equals(b, null));

            if (object.Equals(b, null))
                return (object.Equals(a, null));

            return a.matrix == (b.matrix);
        }

        public static bool operator !=(BoundingFrustum a, BoundingFrustum b)
        {
            return !(a == b);
        }

        public ContainmentType Contains(Bounds box)
        {
            var result = default(ContainmentType);
            this.Contains(ref box, out result);
            return result;
        }

        public void Contains(ref Bounds box, out ContainmentType result)
        {
            var intersects = false;
            for (var i = 0; i < PlaneCount; ++i)
            {
                var planeIntersectionType = default(PlaneIntersectionType);
                box.Intersects(ref this.planes[i], out planeIntersectionType);
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
            if (this == frustum)                // We check to see if the two frustums are equal
                return ContainmentType.Contains;// If they are, there's no need to go any further.

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
            this.Contains(ref point, out result);
            return result;
        }

        public void Contains(ref Vector3 point, out ContainmentType result)
        {
            for (var i = 0; i < PlaneCount; ++i)
            {
                // TODO: we might want to inline this for performance reasons
                if (PlaneHelper.ClassifyPoint(ref point, ref this.planes[i]) > 0)
                {
                    result = ContainmentType.Disjoint;
                    return;
                }
            }
            result = ContainmentType.Contains;
        }

        public bool Equals(BoundingFrustum other)
        {
            return (this == other);
        }

        public override bool Equals(object obj)
        {
            BoundingFrustum f = obj as BoundingFrustum;
            return (object.Equals(f, null)) ? false : (this == f);
        }

        public Vector3[] GetCorners()
        {
            return (Vector3[])this.corners.Clone();
        }

        public void GetCorners(Vector3[] corners)
        {
            if (corners == null) throw new ArgumentNullException("corners");
            if (corners.Length < CornerCount) throw new ArgumentOutOfRangeException("corners");

            this.corners.CopyTo(corners, 0);
        }

        public override int GetHashCode()
        {
            return this.matrix.GetHashCode();
        }

        public bool Intersects(Bounds box)
        {
            var result = false;
            this.Intersects(ref box, out result);
            return result;
        }

        public void Intersects(ref Bounds box, out bool result)
        {
            var containment = default(ContainmentType);
            this.Contains(ref box, out containment);
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
                    "Near( ", this.planes[0].DebugDisplayString, " )  \r\n",
                    "Far( ", this.planes[1].DebugDisplayString, " )  \r\n",
                    "Left( ", this.planes[2].DebugDisplayString, " )  \r\n",
                    "Right( ", this.planes[3].DebugDisplayString, " )  \r\n",
                    "Top( ", this.planes[4].DebugDisplayString, " )  \r\n",
                    "Bottom( ", this.planes[5].DebugDisplayString, " )  "
                    );
            }
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder(256);
            sb.Append("{Near:");
            sb.Append(this.planes[0].ToString());
            sb.Append(" Far:");
            sb.Append(this.planes[1].ToString());
            sb.Append(" Left:");
            sb.Append(this.planes[2].ToString());
            sb.Append(" Right:");
            sb.Append(this.planes[3].ToString());
            sb.Append(" Top:");
            sb.Append(this.planes[4].ToString());
            sb.Append(" Bottom:");
            sb.Append(this.planes[5].ToString());
            sb.Append("}");
            return sb.ToString();
        }

        #endregion Public Methods


        #region Private Methods

        private void CreateCorners()
        {
            IntersectionPoint(ref this.planes[0], ref this.planes[2], ref this.planes[4], out this.corners[0]);
            IntersectionPoint(ref this.planes[0], ref this.planes[3], ref this.planes[4], out this.corners[1]);
            IntersectionPoint(ref this.planes[0], ref this.planes[3], ref this.planes[5], out this.corners[2]);
            IntersectionPoint(ref this.planes[0], ref this.planes[2], ref this.planes[5], out this.corners[3]);
            IntersectionPoint(ref this.planes[1], ref this.planes[2], ref this.planes[4], out this.corners[4]);
            IntersectionPoint(ref this.planes[1], ref this.planes[3], ref this.planes[4], out this.corners[5]);
            IntersectionPoint(ref this.planes[1], ref this.planes[3], ref this.planes[5], out this.corners[6]);
            IntersectionPoint(ref this.planes[1], ref this.planes[2], ref this.planes[5], out this.corners[7]);
        }

        private void CreatePlanes()
        {
            this.planes[0] = new Plane(-this.matrix.M13, -this.matrix.M23, -this.matrix.M33, -this.matrix.M43);
            this.planes[1] = new Plane(this.matrix.M13 - this.matrix.M14, this.matrix.M23 - this.matrix.M24, this.matrix.M33 - this.matrix.M34, this.matrix.M43 - this.matrix.M44);
            this.planes[2] = new Plane(-this.matrix.M14 - this.matrix.M11, -this.matrix.M24 - this.matrix.M21, -this.matrix.M34 - this.matrix.M31, -this.matrix.M44 - this.matrix.M41);
            this.planes[3] = new Plane(this.matrix.M11 - this.matrix.M14, this.matrix.M21 - this.matrix.M24, this.matrix.M31 - this.matrix.M34, this.matrix.M41 - this.matrix.M44);
            this.planes[4] = new Plane(this.matrix.M12 - this.matrix.M14, this.matrix.M22 - this.matrix.M24, this.matrix.M32 - this.matrix.M34, this.matrix.M42 - this.matrix.M44);
            this.planes[5] = new Plane(-this.matrix.M14 - this.matrix.M12, -this.matrix.M24 - this.matrix.M22, -this.matrix.M34 - this.matrix.M32, -this.matrix.M44 - this.matrix.M42);

            this.NormalizePlane(ref this.planes[0]);
            this.NormalizePlane(ref this.planes[1]);
            this.NormalizePlane(ref this.planes[2]);
            this.NormalizePlane(ref this.planes[3]);
            this.NormalizePlane(ref this.planes[4]);
            this.NormalizePlane(ref this.planes[5]);
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

            cross = Vector3.Cross(b.Normal, c.Normal);

            double f = Vector3.Dot(a.Normal, cross);
            f *= -1.0f;

            cross = Vector3.Cross(b.Normal, c.Normal );
            v1 = Vector3.Multiply(cross, a.D);
            //v1 = (a.D * (Vector3.Cross(b.Normal, c.Normal)));


            cross = Vector3.Cross(c.Normal, a.Normal);
            v2 = Vector3.Multiply(cross, b.D);
            //v2 = (b.D * (Vector3.Cross(c.Normal, a.Normal)));


            cross = Vector3.Cross(a.Normal, b.Normal);
            v3 = Vector3.Multiply(cross, c.D);
            //v3 = (c.D * (Vector3.Cross(a.Normal, b.Normal)));

            result.x = (v1.x + v2.x + v3.x) / f;
            result.y = (v1.y + v2.y + v3.y) / f;
            result.z = (v1.z + v2.z + v3.z) / f;
        }

        private void NormalizePlane(ref Plane p)
        {
            double factor = 1 / p.Normal.Length();
            p.Normal.x *= factor;
            p.Normal.y *= factor;
            p.Normal.z *= factor;
            p.D *= factor;
        }

        #endregion
    }
}
