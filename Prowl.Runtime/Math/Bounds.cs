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
}
