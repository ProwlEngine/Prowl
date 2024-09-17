// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Diagnostics.CodeAnalysis;

namespace Prowl.Runtime.GUI;

public struct Spacing
{
    public readonly double Left;
    public readonly double Right;
    public readonly double Top;
    public readonly double Bottom;

    public readonly double Horizontal => Left + Right;
    public readonly double Vertical => Top + Bottom;
    public readonly Vector2 TopLeft => new(Left, Top);
    public readonly Vector2 TopRight => new(Right, Top);
    public readonly Vector2 BottomLeft => new(Left, Bottom);
    public readonly Vector2 BottomRight => new(Right, Bottom);

    public Spacing() { }

    public Spacing(double left, double right, double top, double bottom)
    {
        Left = left;
        Right = right;
        Top = top;
        Bottom = bottom;
    }

    public static bool operator ==(Spacing s1, Spacing s2)
    {
        return s1.Equals(s2);
    }

    public static bool operator !=(Spacing s1, Spacing s2)
    {
        return !s1.Equals(s2);
    }

    public override bool Equals([AllowNull] object obj)
    {
        return obj is Spacing spacing && Equals(spacing);
    }

    public readonly bool Equals(Spacing other)
    {
        return Left == other.Left &&
               Right == other.Right &&
               Top == other.Top &&
               Bottom == other.Bottom;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Left, Right, Top, Bottom);
    }
}
