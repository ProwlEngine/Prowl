// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>One ranked candidate from <see cref="NavMeshSystem.QueryTacticalPositions"/>.</summary>
public readonly struct NavMeshTacticalResult
{
    /// <summary>World-space position of this candidate.</summary>
    public Float3 Position { get; }

    /// <summary>Sum of every scorer's own <see cref="ITacticalScorer.Score"/> for this position - what
    /// results are ranked by, highest first.</summary>
    public float TotalScore { get; }

    /// <summary>Each scorer's own individual score for this position, in the same order the scorer list
    /// was passed to the query - for a caller that wants to know why a candidate ranked where it did,
    /// not just that it did.</summary>
    public IReadOnlyList<float> ScorerBreakdown { get; }

    internal NavMeshTacticalResult(Float3 position, float totalScore, IReadOnlyList<float> scorerBreakdown)
    {
        Position = position;
        TotalScore = totalScore;
        ScorerBreakdown = scorerBreakdown;
    }
}
