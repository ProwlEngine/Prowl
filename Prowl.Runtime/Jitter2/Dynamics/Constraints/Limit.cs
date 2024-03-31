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
using Jitter2.LinearMath;

namespace Jitter2.Dynamics.Constraints;

public struct AngularLimit
{
    public JAngle From { get; set; }
    public JAngle To { get; set; }

    public static readonly AngularLimit Full =
        new(JAngle.FromRadiant(-MathF.PI), JAngle.FromRadiant(MathF.PI));

    public static readonly AngularLimit Fixed =
        new(JAngle.FromRadiant(+1e-6f), JAngle.FromRadiant(-1e-6f));

    public static AngularLimit FromDegree(float min, float max)
    {
        return new AngularLimit(JAngle.FromDegree(min), JAngle.FromDegree(max));
    }

    public AngularLimit(JAngle from, JAngle to)
    {
        From = from;
        To = to;
    }

    internal readonly void Deconstruct(out JAngle LimitMin, out JAngle LimitMax)
    {
        LimitMin = From;
        LimitMax = To;
    }
}

public struct LinearLimit
{
    public float From { get; set; }
    public float To { get; set; }

    public static readonly LinearLimit Full =
        new(float.NegativeInfinity, float.PositiveInfinity);

    public static readonly LinearLimit Fixed =
        new(1e-6f, -1e-6f);

    public LinearLimit(float from, float to)
    {
        From = from;
        To = to;
    }

    public static LinearLimit FromMinMax(float min, float max)
    {
        return new LinearLimit(min, max);
    }

    internal readonly void Deconstruct(out float LimitMin, out float LimitMax)
    {
        LimitMin = From;
        LimitMax = To;
    }
}