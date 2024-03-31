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

using Jitter2.Collision.Shapes;
using Jitter2.LinearMath;

namespace Jitter2.Collision;

/// <summary>
/// Interface to facilitate the implementation of a generic filter. This filter can either exclude certain pairs of shapes or modify collision
/// information subsequent to Jitter's execution of narrow phase collision detection between the shapes.
/// </summary>
public interface INarrowPhaseFilter
{
    /// <summary>
    /// Invoked following the narrow phase of collision detection in Jitter. This allows for the modification of collision information.
    /// Refer to the corresponding <see cref="NarrowPhase"/> methods for details on the parameters.
    /// </summary>
    /// <returns>False if the collision should be filtered out, true otherwise.</returns>
    bool Filter(Shape shapeA, Shape shapeB,
        ref JVector pAA, ref JVector pBB,
        ref JVector normal, ref float penetration);
}