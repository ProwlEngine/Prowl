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
/// Represents a triangle defined by three vertices.
/// </summary>
public struct JTriangle
{
    public JVector V0;
    public JVector V1;
    public JVector V2;

    /// <summary>
    /// Initializes a new instance of the <see cref="JTriangle"/> structure with the specified vertices.
    /// </summary>
    /// <param name="v0">The first vertex of the triangle.</param>
    /// <param name="v1">The second vertex of the triangle.</param>
    /// <param name="v2">The third vertex of the triangle.</param>
    public JTriangle(in JVector v0, in JVector v1, in JVector v2)
    {
        V0 = v0;
        V1 = v1;
        V2 = v2;
    }
}