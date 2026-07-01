using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Graphite.Tests;

// Compares Colors component-wise within a tolerance. Readbacks from float render targets are
// not bit-exact across backends (rasterization rules, normalization rounding), so the migrated
// render tests assert fuzzy equality rather than exact equality.
internal sealed class ColorFuzzyComparer : IEqualityComparer<Color>
{
    public static readonly ColorFuzzyComparer Instance = new(0.01f);

    private readonly float _tolerance;

    public ColorFuzzyComparer(float tolerance) => _tolerance = tolerance;

    public bool Equals(Color a, Color b)
        => Math.Abs(a.R - b.R) <= _tolerance
        && Math.Abs(a.G - b.G) <= _tolerance
        && Math.Abs(a.B - b.B) <= _tolerance
        && Math.Abs(a.A - b.A) <= _tolerance;

    public int GetHashCode(Color color) => 0;
}
