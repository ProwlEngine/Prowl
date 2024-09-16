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

namespace Prowl.Runtime;

public struct Ray : IEquatable<Ray>
{
    #region Public Fields

    public Vector3 direction;

    public Vector3 origin;

    #endregion


    #region Public Constructors

    public Ray(Vector3 position, Vector3 direction)
    {
        origin = position;
        this.direction = direction;
    }

    #endregion


    #region Public Methods

    public override bool Equals(object? obj)
    {
        return (obj is Ray ray) ? Equals(ray) : false;
    }


    public bool Equals(Ray other)
    {
        return origin.Equals(other.origin) && direction.Equals(other.direction);
    }


    public override int GetHashCode()
    {
        return origin.GetHashCode() ^ direction.GetHashCode();
    }

    // adapted from http://www.scratchapixel.com/lessons/3d-basic-lessons/lesson-7-intersecting-simple-shapes/ray-box-intersection/
    public double? Intersects(Bounds box)
    {
        const double Epsilon = 1e-6;

        double? tMin = null, tMax = null;

        if (Math.Abs(direction.x) < Epsilon)
        {
            if (origin.x < box.min.x || origin.x > box.max.x)
                return null;
        }
        else
        {
            tMin = (box.min.x - origin.x) / direction.x;
            tMax = (box.max.x - origin.x) / direction.x;

            if (tMin > tMax)
            {
                var temp = tMin;
                tMin = tMax;
                tMax = temp;
            }
        }

        if (Math.Abs(direction.y) < Epsilon)
        {
            if (origin.y < box.min.y || origin.y > box.max.y)
                return null;
        }
        else
        {
            var tMinY = (box.min.y - origin.y) / direction.y;
            var tMaxY = (box.max.y - origin.y) / direction.y;

            if (tMinY > tMaxY)
            {
                var temp = tMinY;
                tMinY = tMaxY;
                tMaxY = temp;
            }

            if ((tMin.HasValue && tMin > tMaxY) || (tMax.HasValue && tMinY > tMax))
                return null;

            if (!tMin.HasValue || tMinY > tMin) tMin = tMinY;
            if (!tMax.HasValue || tMaxY < tMax) tMax = tMaxY;
        }

        if (Math.Abs(direction.z) < Epsilon)
        {
            if (origin.z < box.min.z || origin.z > box.max.z)
                return null;
        }
        else
        {
            var tMinZ = (box.min.z - origin.z) / direction.z;
            var tMaxZ = (box.max.z - origin.z) / direction.z;

            if (tMinZ > tMaxZ)
            {
                var temp = tMinZ;
                tMinZ = tMaxZ;
                tMaxZ = temp;
            }

            if ((tMin.HasValue && tMin > tMaxZ) || (tMax.HasValue && tMinZ > tMax))
                return null;

            if (!tMin.HasValue || tMinZ > tMin) tMin = tMinZ;
            if (!tMax.HasValue || tMaxZ < tMax) tMax = tMaxZ;
        }

        // having a positive tMin and a negative tMax means the ray is inside the box
        // we expect the intesection distance to be 0 in that case
        if ((tMin.HasValue && tMin < 0) && tMax > 0) return 0;

        // a negative tMin means that the intersection point is behind the ray's origin
        // we discard these as not hitting the AABB
        if (tMin < 0) return null;

        return tMin;
    }


    public void Intersects(ref Bounds box, out double? result)
    {
        result = Intersects(box);
    }

    public double? Intersects(Plane plane)
    {
        double? result;
        Intersects(ref plane, out result);
        return result;
    }

    public void Intersects(ref Plane plane, out double? result)
    {
        var den = Vector3.Dot(direction, plane.normal);
        if (Math.Abs(den) < 0.00001)
        {
            result = null;
            return;
        }

        result = (-plane.distance - Vector3.Dot(plane.normal, origin)) / den;

        if (result < 0.0)
        {
            if (result < -0.00001)
            {
                result = null;
                return;
            }

            result = 0.0;
        }
    }

    public static bool operator !=(Ray a, Ray b)
    {
        return !a.Equals(b);
    }


    public static bool operator ==(Ray a, Ray b)
    {
        return a.Equals(b);
    }


    public override string ToString()
    {
        return string.Format("{{Position:{0} Direction:{1}}}", origin.ToString(), direction.ToString());
    }

    #endregion
}
