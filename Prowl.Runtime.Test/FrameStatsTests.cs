// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Diagnostics;
using System.Threading.Tasks;

using Prowl.Runtime.Diagnostics;

using Xunit;

namespace Prowl.Runtime.Test;

// Frame stats are static and record one thread, and a headless run elsewhere would take it over.
[CollectionDefinition(nameof(FrameStatsTests), DisableParallelization = true)]
public class FrameStatsTestsCollection;

[Collection(nameof(FrameStatsTests))]
public class FrameStatsTests
{
    private static void Spin(double ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds < ms) { }
    }

    [Fact]
    public void NestedScopesCountOnlyTheirOwnTime()
    {
        FrameStat outer = FrameStats.GetCategory("Test.Outer");
        FrameStat inner = FrameStats.GetCategory("Test.Inner");

        FrameStats.Start();
        using (outer.Scope())
        {
            Spin(30);
            using (inner.Scope())
                Spin(30);
        }
        FrameStats.EndFrame();

        float outerMs = outer.GetSample(0).Ms;
        float innerMs = inner.GetSample(0).Ms;
        Assert.True(outerMs >= 29, $"outer {outerMs}");
        Assert.True(innerMs >= 29, $"inner {innerMs}");
        // Counting the inner time in the outer scope too would take the pair past the frame.
        Assert.True(outerMs + innerMs <= FrameStats.GetFrameMs(0) + 0.01f);
    }

    [Fact]
    public void CategoriesAddUpToTheFrame()
    {
        FrameStat work = FrameStats.GetCategory("Test.Work");

        FrameStats.Start();
        using (work.Scope()) Spin(10);
        Spin(10);
        FrameStats.EndFrame();

        float sum = 0;
        foreach (FrameStat c in FrameStats.Categories)
            sum += c.GetSample(0).Ms;

        Assert.Equal(FrameStats.GetFrameMs(0), sum, 0.05f);
        Assert.True(FrameStats.Other.GetSample(0).Ms >= 9, "unscoped time goes to Other");
        Assert.Equal(1, work.GetSample(0).Calls);
    }

    [Fact]
    public void TransferMovesAtMostWhatTheSourceHolds()
    {
        FrameStat from = FrameStats.GetCategory("Test.From");
        FrameStat to = FrameStats.GetCategory("Test.To");

        FrameStats.Start();
        using (from.Scope()) Spin(10);
        FrameStats.Transfer(from, to, long.MaxValue);
        FrameStats.EndFrame();

        Assert.Equal(0, from.GetSample(0).Ms);
        Assert.True(to.GetSample(0).Ms >= 9, $"to {to.GetSample(0).Ms}");
    }

    [Fact]
    public void ScopesOffTheMainThreadRecordNothing()
    {
        FrameStat offThread = FrameStats.GetCategory("Test.OffThread");

        FrameStats.Start();
        // Waited on rather than awaited, so EndFrame runs on the thread Start made the main one.
        Task.Run(() =>
        {
            using (offThread.Scope()) Spin(5);
        }).Wait();
        FrameStats.EndFrame();

        Assert.Equal(new FrameStatSample(0, 0), offThread.GetSample(0));
    }

    [Fact]
    public void CategoriesAreLookedUpByNameIgnoringCase()
    {
        Assert.Same(FrameStats.GetCategory("Test.Lookup"), FrameStats.GetCategory("test.lookup"));
        Assert.Same(FrameStats.Physics, FrameStats.GetCategory("physics"));
    }

    [Fact]
    public void RepeatedScopesAddUp()
    {
        FrameStat repeated = FrameStats.GetCategory("Test.Repeated");

        FrameStats.Start();
        for (int i = 0; i < 3; i++)
            using (repeated.Scope()) Spin(5);
        FrameStats.EndFrame();

        FrameStatSample sample = repeated.GetSample(0);
        Assert.Equal(3, sample.Calls);
        Assert.True(sample.Ms >= 14, $"repeated {sample.Ms}");
    }

    [Fact]
    public void ACategoryNestedInItselfIsNotCountedTwice()
    {
        FrameStat self = FrameStats.GetCategory("Test.Self");

        FrameStats.Start();
        using (self.Scope())
        {
            Spin(10);
            using (self.Scope()) Spin(10);
        }
        FrameStats.EndFrame();

        Assert.Equal(2, self.GetSample(0).Calls);
        Assert.True(self.GetSample(0).Ms <= FrameStats.GetFrameMs(0) + 0.01f);
    }

    [Fact]
    public void AnExceptionStillClosesTheScopeAndHandsBackToTheParent()
    {
        FrameStat outer = FrameStats.GetCategory("Test.ThrowOuter");
        FrameStat inner = FrameStats.GetCategory("Test.ThrowInner");

        FrameStats.Start();
        using (outer.Scope())
        {
            try
            {
                using (inner.Scope())
                {
                    Spin(5);
                    throw new InvalidOperationException();
                }
            }
            catch (InvalidOperationException) { }

            // Back in the outer scope: this time must land there, not in the scope that threw.
            Spin(20);
        }
        FrameStats.EndFrame();

        Assert.True(inner.GetSample(0).Ms < 15, $"inner {inner.GetSample(0).Ms}");
        Assert.True(outer.GetSample(0).Ms >= 19, $"outer {outer.GetSample(0).Ms}");
    }

    [Fact]
    public void GCPausesAreTakenOutOfTheScopeTheyHappenIn()
    {
        FrameStat work = FrameStats.GetCategory("Test.Collecting");

        FrameStats.Start();
        using (work.Scope())
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        FrameStats.EndFrame();

        float gcMs = FrameStats.GC.GetSample(0).Ms;
        Assert.True(gcMs > 0, "a blocking collection pauses the program");
        Assert.True(work.GetSample(0).Ms + gcMs <= FrameStats.GetFrameMs(0) + 0.01f);
    }

    [Fact]
    public void SamplesAreNewestFirst()
    {
        FrameStat stat = FrameStats.GetCategory("Test.Order");

        FrameStats.Start();
        using (stat.Scope()) Spin(5);
        FrameStats.EndFrame();
        FrameStats.EndFrame();

        Assert.Equal(0, stat.GetSample(0).Calls);
        Assert.Equal(1, stat.GetSample(1).Calls);
        Assert.True(stat.GetSample(1).Ms >= 4);
    }

    [Fact]
    public void StatsSummariseTheFramesAskedFor()
    {
        FrameStat stat = FrameStats.GetCategory("Test.Summary");

        FrameStats.Start();
        for (int frame = 1; frame <= 3; frame++)
        {
            for (int i = 0; i < frame; i++)
                using (stat.Scope()) Spin(2);
            FrameStats.EndFrame();
        }

        FrameStatSummary all = stat.GetStats(3);
        Assert.Equal(6, all.Calls);
        Assert.True(all.Min <= all.Avg && all.Avg <= all.Max, $"{all}");
        Assert.True(all.Max >= 5.5f, $"max {all.Max}");

        Assert.Equal(3, stat.GetStats(1).Calls);
        Assert.Equal(default, stat.GetStats(0));
    }

    [Fact]
    public void HistoryKeepsOnlyTheLastFrames()
    {
        FrameStats.Start();
        for (int i = 0; i < FrameStats.HistoryLength + 10; i++)
            FrameStats.EndFrame();

        Assert.Equal(FrameStats.HistoryLength, FrameStats.HistoryCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameStats.GetFrameMs(FrameStats.HistoryLength));
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameStats.GetFrameMs(-1));
    }

    [Fact]
    public void OtherIsNeverNegative()
    {
        FrameStat from = FrameStats.GetCategory("Test.Busy");

        FrameStats.Start();
        using (from.Scope()) Spin(5);
        FrameStats.EndFrame();

        Assert.True(FrameStats.Other.GetSample(0).Ms >= 0);
    }

    [Fact]
    public void TransferFromAnEmptyCategoryMovesNothing()
    {
        FrameStat empty = FrameStats.GetCategory("Test.Empty");
        FrameStat to = FrameStats.GetCategory("Test.EmptyTo");

        FrameStats.Start();
        FrameStats.Transfer(empty, to, 1_000_000);
        FrameStats.EndFrame();

        Assert.Equal(0, empty.GetSample(0).Ms);
        Assert.Equal(0, to.GetSample(0).Ms);
    }

    [Fact]
    public void StartDropsAHalfCountedFrame()
    {
        FrameStat leftover = FrameStats.GetCategory("Test.Leftover");

        FrameStats.Start();
        using (leftover.Scope()) Spin(5);
        // A new run starts without the old one ending its frame.
        FrameStats.Start();
        FrameStats.EndFrame();

        Assert.Equal(new FrameStatSample(0, 0), leftover.GetSample(0));
    }

    [Fact]
    public void NewCategoriesAreListedAndKeepTheirColour()
    {
        FrameStat made = FrameStats.GetCategory("Test.Listed");

        Assert.Contains(made, FrameStats.Categories);
        Assert.Equal(made.Color, FrameStats.GetCategory("TEST.LISTED").Color);
        Assert.Contains(FrameStats.Render, FrameStats.Categories);
    }

    // ---- Edge cases ----

    [Fact]
    public void AScopeOpenAcrossAFrameEndIsSplitBetweenTheFrames()
    {
        FrameStat stat = FrameStats.GetCategory("Test.AcrossFrames");

        FrameStats.Start();
        using (stat.Scope())
        {
            Spin(10);
            FrameStats.EndFrame();
            Spin(10);
        }
        FrameStats.EndFrame();

        // The call belongs to the frame the scope opened in; the time to whichever frame it was spent in.
        Assert.Equal(1, stat.GetSample(1).Calls);
        Assert.Equal(0, stat.GetSample(0).Calls);
        Assert.True(stat.GetSample(1).Ms >= 9, $"first {stat.GetSample(1).Ms}");
        Assert.True(stat.GetSample(0).Ms >= 9, $"second {stat.GetSample(0).Ms}");
    }

    [Fact]
    public void DisposingAScopeTwiceDoesNotCloseTheNextOne()
    {
        FrameStat first = FrameStats.GetCategory("Test.DisposedTwice");
        FrameStat second = FrameStats.GetCategory("Test.OpenedAfter");

        FrameStats.Start();
        FrameStatScope a = first.Scope();
        a.Dispose();
        using (second.Scope())
        {
            a.Dispose();
            Spin(10);
        }
        FrameStats.EndFrame();

        Assert.True(second.GetSample(0).Ms >= 9, $"second {second.GetSample(0).Ms}");
    }

    [Fact]
    public void DisposingADefaultScopeDoesNothing()
    {
        FrameStat outer = FrameStats.GetCategory("Test.AroundDefault");

        FrameStats.Start();
        using (outer.Scope())
        {
            default(FrameStatScope).Dispose();
            Spin(10);
        }
        FrameStats.EndFrame();

        Assert.True(outer.GetSample(0).Ms >= 9, $"outer {outer.GetSample(0).Ms}");
    }

    [Fact]
    public void StartInsideAnOpenScopeDoesNotBreakTheNextFrames()
    {
        FrameStat stat = FrameStats.GetCategory("Test.StartInside");

        FrameStats.Start();
        using (stat.Scope())
            FrameStats.Start();
        FrameStats.EndFrame();
        Spin(5);
        FrameStats.EndFrame();

        // Nothing is left charging the closed scope: the second frame is all Other.
        Assert.Equal(0, stat.GetSample(0).Ms);
        Assert.True(FrameStats.Other.GetSample(0).Ms >= 4);
    }

    [Fact]
    public void AScopeOffTheMainThreadDoesNotDisturbTheMainThreadsScope()
    {
        FrameStat main = FrameStats.GetCategory("Test.MainSide");
        FrameStat worker = FrameStats.GetCategory("Test.WorkerSide");

        FrameStats.Start();
        using (main.Scope())
        {
            Task.Run(() => { using (worker.Scope()) Spin(5); }).Wait();
            Spin(5);
        }
        FrameStats.EndFrame();

        Assert.Equal(0, worker.GetSample(0).Calls);
        Assert.True(main.GetSample(0).Ms >= 9, $"main {main.GetSample(0).Ms}");
    }

    [Fact]
    public void TransferOffTheMainThreadMovesNothing()
    {
        FrameStat from = FrameStats.GetCategory("Test.TransferFrom");
        FrameStat to = FrameStats.GetCategory("Test.TransferTo");

        FrameStats.Start();
        using (from.Scope()) Spin(5);
        Task.Run(() => FrameStats.Transfer(from, to, long.MaxValue)).Wait();
        FrameStats.EndFrame();

        Assert.Equal(0, to.GetSample(0).Ms);
        Assert.True(from.GetSample(0).Ms >= 4);
    }

    [Fact]
    public void TransferOfANegativeAmountOrToItselfChangesNothing()
    {
        FrameStat stat = FrameStats.GetCategory("Test.TransferSelf");
        FrameStat other = FrameStats.GetCategory("Test.TransferNegative");

        FrameStats.Start();
        using (stat.Scope()) Spin(10);
        FrameStats.Transfer(stat, stat, long.MaxValue);
        FrameStats.Transfer(stat, other, -1_000_000);
        FrameStats.EndFrame();

        Assert.True(stat.GetSample(0).Ms >= 9, $"stat {stat.GetSample(0).Ms}");
        Assert.Equal(0, other.GetSample(0).Ms);
    }

    [Fact]
    public void ACategoryCreatedMidRunHasEmptyHistory()
    {
        FrameStats.Start();
        for (int i = 0; i < 10; i++) FrameStats.EndFrame();

        FrameStat late = FrameStats.GetCategory("Test.Late." + Guid.NewGuid());

        Assert.Equal(new FrameStatSample(0, 0), late.GetSample(5));
        Assert.Equal(0, late.GetStats(10).Max);
    }

    [Fact]
    public void ConcurrentLookupsOfANewNameAllGetTheSameCategory()
    {
        string name = "Test.Concurrent." + Guid.NewGuid();
        var found = new FrameStat[64];

        Parallel.For(0, found.Length, i => found[i] = FrameStats.GetCategory(name));

        Assert.All(found, c => Assert.Same(found[0], c));
        Assert.Single(FrameStats.Categories, c => c.Name == name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ACategoryNeedsAName(string? name)
    {
        Assert.ThrowsAny<ArgumentException>(() => FrameStats.GetCategory(name!));
    }

    [Fact]
    public void StatsAskedForMoreFramesThanHeldUseWhatIsHeld()
    {
        FrameStat stat = FrameStats.GetCategory("Test.Clamp");

        FrameStats.Start();
        for (int i = 0; i < 5; i++)
        {
            using (stat.Scope()) { }
            FrameStats.EndFrame();
        }

        Assert.Equal(5, stat.GetStats(int.MaxValue).Calls);
        Assert.Equal(default, stat.GetStats(-1));
    }

    [Fact]
    public void BackToBackFrameEndsRecordEmptyFrames()
    {
        FrameStats.Start();
        FrameStats.EndFrame();
        FrameStats.EndFrame();

        Assert.True(FrameStats.GetFrameMs(0) >= 0);
        Assert.True(FrameStats.Other.GetSample(0).Ms >= 0);
        Assert.True(float.IsFinite(FrameStats.Other.GetStats(2).Avg));
    }
}
