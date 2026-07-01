// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>Tests for <see cref="AnimationCurve"/> evaluation, looping, and key management.</summary>
public class AnimationCurveTests
{
    // PostLoop Linear extrapolation must use the LAST key's tangent, not the first's.
    [Fact]
    public void PostLoopLinear_UsesLastKeyTangent()
    {
        var c = new AnimationCurve(new[] { new KeyFrame(0, 0), new KeyFrame(10, 5) });
        c.PostLoop = CurveLoopType.Linear;
        c.Keys[0].TangentOut = 100f; // first key - must NOT be used past the end
        c.Keys[1].TangentOut = 2f;   // last key - the correct slope

        Assert.Equal(7f, c.Evaluate(11f), 3); // last.Value(5) + last.TangentOut(2) * 1
    }

    // Step continuity holds the previous key's value across the whole segment, not until a magic 1.0.
    [Fact]
    public void StepContinuity_HoldsPreviousValueAcrossSegment()
    {
        var c = new AnimationCurve(new[] { new KeyFrame(0, 0), new KeyFrame(5, 1), new KeyFrame(10, 2) });
        c.Keys[1].Continuity = CurveContinuity.Step; // governs the [5,10] segment

        Assert.Equal(1f, c.Evaluate(7f), 3); // holds key@5's value until reaching key@10
    }

    // Assigning through the indexer to a new position must keep keys sorted (sorted insert, not append).
    [Fact]
    public void IndexerAssignment_KeepsKeysSorted()
    {
        var c = new AnimationCurve(new[] { new KeyFrame(0, 0), new KeyFrame(5, 5), new KeyFrame(10, 10) });
        c.Keys[0] = new KeyFrame(7, 7); // move first key to position 7

        for (int i = 1; i < c.Keys.Count; i++)
            Assert.True(c.Keys[i - 1].Position <= c.Keys[i].Position, "Keys must remain sorted by position.");
    }

    // Cycle looping must floor the cycle count (no double-floor at exact negative integer boundaries).
    [Fact]
    public void CyclePreLoop_AtExactBoundary_MapsToStart()
    {
        var c = new AnimationCurve(new[] { new KeyFrame(0, 0), new KeyFrame(1, 10) });
        c.PreLoop = CurveLoopType.Cycle;

        // One full period below the start maps back to the start value (0), not the end value (10).
        Assert.Equal(0f, c.Evaluate(-1.0f), 3);
    }
}
