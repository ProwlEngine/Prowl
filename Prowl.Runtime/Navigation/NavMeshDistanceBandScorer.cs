// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>Disqualifies a candidate outside [<paramref name="min"/>, <paramref name="max"/>] world units
/// of <paramref name="origin"/> - a cover point within throwing range but outside melee range, say.</summary>
public sealed class NavMeshDistanceBandScorer(Float3 origin, float min, float max) : ITacticalScorer
{
    public float Score(Float3 point)
    {
        float distance = Float3.Distance(point, origin);
        return distance >= min && distance <= max ? 1f : -1f;
    }
}
