using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Veldrid.Tests;

internal class ColorFuzzyComparer : IEqualityComparer<Color>
{
    public static ColorFuzzyComparer Instance = new();

    public bool Equals(Color x, Color y)
    {
        return FuzzyEquals(x.R, y.R)
            && FuzzyEquals(x.G, y.G)
            && FuzzyEquals(x.B, y.B)
            && FuzzyEquals(x.A, y.A);
    }

    private bool FuzzyEquals(float x, float y)
    {
        return Math.Abs(x - y) < 1e-5;
    }

    public int GetHashCode(Color obj)
    {
        return obj.GetHashCode();
    }
}
