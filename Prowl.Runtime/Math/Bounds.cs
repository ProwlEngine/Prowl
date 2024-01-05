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
        public Vector3 min;

        [DataMember]
        public Vector3 max;

        public const int CornerCount = 8;

        #endregion Public Fields

        #region Public Properties
        public Vector3 center { get { return (this.min + this.max) * 0.5f; } set { var s = this.size * 0.5f; this.min = value - s; this.max = value + s; } }
        public Vector3 extents { get { return (this.max - this.min) * 0.5f; } set { var c = this.center; this.min = c - value; this.max = c + value; } }
        public Vector3 size { get { return this.max - this.min; } set { var c = this.center; var s = value * 0.5f; this.min = c - s; this.max = c + s; } }
        #endregion


        #region Public Constructors

        public Bounds(Vector3 center, Vector3 size)
        {
            var hs = size * 0.5f;
            min = center - hs;
            max = center + hs;
        }

        #endregion Public Constructors


        #region Public Methods

        public ContainmentType Contains(Bounds box)
        {
            //test if all corner is in the same side of a face by just checking min and max
            if (box.max.x < min.x
                || box.min.x > max.x
                || box.max.y < min.y
                || box.min.y > max.y
                || box.max.z < min.z
                || box.min.z > max.z)
                return ContainmentType.Disjoint;


            if (box.min.x >= min.x
                && box.max.x <= max.x
                && box.min.y >= min.y
                && box.max.y <= max.y
                && box.min.z >= min.z
                && box.max.z <= max.z)
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
            if (point.x < this.min.x
                || point.x > this.max.x
                || point.y < this.min.y
                || point.y > this.max.y
                || point.z < this.min.z
                || point.z > this.max.z)
            {
                result = ContainmentType.Disjoint;
            }//or if point is on box because coordonate of point is lesser or equal
            else if (point.x == this.min.x
                || point.x == this.max.x
                || point.y == this.min.y
                || point.y == this.max.y
                || point.z == this.min.z
                || point.z == this.max.z)
                result = ContainmentType.Intersects;
            else
                result = ContainmentType.Contains;
        }

        private static readonly Vector3 MaxVector3 = new Vector3(double.MaxValue);
        private static readonly Vector3 MinVector3 = new Vector3(double.MinValue);

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


        public void Encapsulate(Vector3 point)
        {
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }

        public void Encapsulate(Bounds bounds)
        {
            Encapsulate(bounds.center - bounds.extents);
            Encapsulate(bounds.center + bounds.extents);
        }

        public void Expand(double amount)
        {
            extents += new Vector3(amount, amount, amount) * .5;
        }

        public void Expand(Vector3 amount)
        {
            extents += amount * .5;
        }

        public static Bounds CreateMerged(Bounds original, Bounds additional)
        {
            Bounds result;
            CreateMerged(ref original, ref additional, out result);
            return result;
        }

        public static void CreateMerged(ref Bounds original, ref Bounds additional, out Bounds result)
        {
            result.min.x = Math.Min(original.min.x, additional.min.x);
            result.min.y = Math.Min(original.min.y, additional.min.y);
            result.min.z = Math.Min(original.min.z, additional.min.z);
            result.max.x = Math.Max(original.max.x, additional.max.x);
            result.max.y = Math.Max(original.max.y, additional.max.y);
            result.max.z = Math.Max(original.max.z, additional.max.z);
        }

        public bool Equals(Bounds other)
        {
            return (this.min == other.min) && (this.max == other.max);
        }

        public override bool Equals(object obj)
        {
            return (obj is Bounds) ? this.Equals((Bounds)obj) : false;
        }

        public Vector3[] GetCorners()
        {
            return new Vector3[] {
                new Vector3(this.min.x, this.max.y, this.max.z),
                new Vector3(this.max.x, this.max.y, this.max.z),
                new Vector3(this.max.x, this.min.y, this.max.z),
                new Vector3(this.min.x, this.min.y, this.max.z),
                new Vector3(this.min.x, this.max.y, this.min.z),
                new Vector3(this.max.x, this.max.y, this.min.z),
                new Vector3(this.max.x, this.min.y, this.min.z),
                new Vector3(this.min.x, this.min.y, this.min.z)
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
            corners[0].x = this.min.x;
            corners[0].y = this.max.y;
            corners[0].z = this.max.z;
            corners[1].x = this.max.x;
            corners[1].y = this.max.y;
            corners[1].z = this.max.z;
            corners[2].x = this.max.x;
            corners[2].y = this.min.y;
            corners[2].z = this.max.z;
            corners[3].x = this.min.x;
            corners[3].y = this.min.y;
            corners[3].z = this.max.z;
            corners[4].x = this.min.x;
            corners[4].y = this.max.y;
            corners[4].z = this.min.z;
            corners[5].x = this.max.x;
            corners[5].y = this.max.y;
            corners[5].z = this.min.z;
            corners[6].x = this.max.x;
            corners[6].y = this.min.y;
            corners[6].z = this.min.z;
            corners[7].x = this.min.x;
            corners[7].y = this.min.y;
            corners[7].z = this.min.z;
        }

        public override int GetHashCode()
        {
            return this.min.GetHashCode() + this.max.GetHashCode();
        }

        public bool Intersects(Bounds box)
        {
            bool result;
            Intersects(ref box, out result);
            return result;
        }

        public void Intersects(ref Bounds box, out bool result)
        {
            if ((this.max.x >= box.min.x) && (this.min.x <= box.max.x))
            {
                if ((this.max.y < box.min.y) || (this.min.y > box.max.y))
                {
                    result = false;
                    return;
                }

                result = (this.max.z >= box.min.z) && (this.min.z <= box.max.z);
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

            if (plane.normal.x >= 0)
            {
                positiveVertex.x = max.x;
                negativeVertex.x = min.x;
            }
            else
            {
                positiveVertex.x = min.x;
                negativeVertex.x = max.x;
            }

            if (plane.normal.y >= 0)
            {
                positiveVertex.y = max.y;
                negativeVertex.y = min.y;
            }
            else
            {
                positiveVertex.y = min.y;
                negativeVertex.y = max.y;
            }

            if (plane.normal.z >= 0)
            {
                positiveVertex.z = max.z;
                negativeVertex.z = min.z;
            }
            else
            {
                positiveVertex.z = min.z;
                negativeVertex.z = max.z;
            }

            // Inline Vector3.Dot(plane.Normal, negativeVertex) + plane.D;
            var distance = plane.normal.x * negativeVertex.x + plane.normal.y * negativeVertex.y + plane.normal.z * negativeVertex.z + plane.distance;
            if (distance > 0)
            {
                result = PlaneIntersectionType.Front;
                return;
            }

            // Inline Vector3.Dot(plane.Normal, positiveVertex) + plane.D;
            distance = plane.normal.x * positiveVertex.x + plane.normal.y * positiveVertex.y + plane.normal.z * positiveVertex.z + plane.distance;
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
                    "Min( ", this.min.ToString(), " )  \r\n",
                    "Max( ", this.max.ToString(), " )"
                    );
            }
        }

        public override string ToString()
        {
            return "{{Min:" + this.min.ToString() + " Max:" + this.max.ToString() + "}}";
        }

        #endregion Public Methods
    }
}
