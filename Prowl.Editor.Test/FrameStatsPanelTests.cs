// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Diagnostics;

using Prowl.Editor.GUI.Panels;
using Prowl.Runtime.Diagnostics;

using Xunit;

using History = Prowl.Editor.GUI.Panels.FrameStatsPanel.History;

namespace Prowl.Editor.Test;

// Frame stats are static and record one thread, so these never run alongside each other.
[CollectionDefinition(nameof(FrameStatsPanelTests), DisableParallelization = true)]
public class FrameStatsPanelTestsCollection;

/// <summary>The panel's column gathering, driven by real frames, and the layout maths its chart uses.</summary>
[Collection(nameof(FrameStatsPanelTests))]
public class FrameStatsPanelTests
{
    private static readonly TimeSpan Now = TimeSpan.Zero;
    private static readonly TimeSpan Never = TimeSpan.MaxValue;
    private static bool All(FrameStat _) => true;

    private static void Spin(double ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds < ms) { }
    }

    // A history made after Start sees only the frames that follow.
    private static History Fresh(int capacity = 10)
    {
        FrameStats.Start();
        return new History(capacity);
    }

    private static void Frames(int count, FrameStat? stat = null, double spinMs = 0)
    {
        for (int i = 0; i < count; i++)
        {
            if (stat != null)
                using (stat.Scope()) Spin(spinMs);
            FrameStats.EndFrame();
        }
    }

    private static int IndexOf(FrameStat stat)
    {
        for (int i = 0; i < FrameStats.Categories.Count; i++)
            if (FrameStats.Categories[i] == stat) return i;
        return -1;
    }

    [Fact]
    public void AColumnAveragesTheFramesItGathered()
    {
        FrameStat stat = FrameStats.GetCategory("PanelTest.Average");
        History history = Fresh();

        Frames(4, stat);
        history.Sample(Now, All);

        History.Column column = Assert.IsType<History.Column>(history.Ago(0));
        Assert.Equal(1, history.Count);
        Assert.Equal(4, column.Frames);
        Assert.Equal(1, column.Calls(IndexOf(stat)));
    }

    [Fact]
    public void FramesKeepGatheringUntilTheIntervalPasses()
    {
        History history = Fresh();

        Frames(3);
        history.Sample(Never, All);
        Frames(2);
        history.Sample(Never, All);
        Assert.Equal(0, history.Count);

        history.Sample(Now, All);
        Assert.Equal(5, history.Ago(0)!.Frames);
    }

    [Fact]
    public void NoNewFramesMakesNoColumn()
    {
        History history = Fresh();

        history.Sample(Now, All);

        Assert.Equal(0, history.Count);
    }

    [Fact]
    public void OnlyTheNewestColumnsAreKept()
    {
        History history = Fresh(capacity: 3);

        // Columns of 1 to 5 frames, so each can be told apart by its size.
        for (int frames = 1; frames <= 5; frames++)
        {
            Frames(frames);
            history.Sample(Now, All);
        }

        Assert.Equal(3, history.Count);
        Assert.Equal(5, history.Ago(0)!.Frames);
        Assert.Equal(3, history.Ago(2)!.Frames);
        Assert.Null(history.Ago(3));
        Assert.Null(history.Ago(-1));
    }

    [Fact]
    public void AnIdFollowsItsColumnUntilItIsDropped()
    {
        History history = Fresh(capacity: 3);
        Frames(1);
        history.Sample(Now, All);
        long id = history.IdOf(0);

        Frames(1);
        history.Sample(Now, All);
        Assert.Equal(1, history.AgoOf(id));

        for (int i = 0; i < 2; i++)
        {
            Frames(1);
            history.Sample(Now, All);
        }
        Assert.Equal(-1, history.AgoOf(id));
        Assert.Equal(-1, history.AgoOf(-1));
    }

    [Fact]
    public void SkipDropsTheFramesSinceTheLastSample()
    {
        History history = Fresh();

        Frames(3);
        history.Sample(Never, All);
        Frames(2);
        history.Skip();
        history.Sample(Now, All);

        Assert.Equal(0, history.Count);
    }

    [Fact]
    public void ClearDropsEveryColumn()
    {
        History history = Fresh();
        Frames(2);
        history.Sample(Now, All);

        history.Clear();

        Assert.Equal(0, history.Count);
        Assert.Null(history.Ago(0));
    }

    [Fact]
    public void ThePeakCountsOnlyTheCategoriesShown()
    {
        FrameStat big = FrameStats.GetCategory("PanelTest.Big");
        FrameStat small = FrameStats.GetCategory("PanelTest.Small");
        History history = Fresh();

        using (big.Scope()) Spin(10);
        using (small.Scope()) Spin(2);
        FrameStats.EndFrame();
        history.Sample(Now, c => c == small);

        float peak = history.Ago(0)!.PeakMs;
        Assert.True(peak >= 1.9f && peak < 9, $"peak {peak}");
    }

    [Fact]
    public void TotalsAreWeightedByFrames()
    {
        FrameStat stat = FrameStats.GetCategory("PanelTest.Weighted");
        History history = Fresh();

        // One column of a single 10 ms frame, then one of three empty frames: 10 ms over 4 frames.
        Frames(1, stat, spinMs: 10);
        history.Sample(Now, All);
        Frames(3);
        history.Sample(Now, All);

        History.Totals totals = history.Summarise(FrameStats.Categories.Count);
        float avg = totals.AvgMs[IndexOf(stat)];
        Assert.True(avg >= 2.4f && avg < 4f, $"avg {avg}, an unweighted mean would be about 5");
        Assert.True(totals.MaxMs[IndexOf(stat)] >= 9.9f);
    }

    [Fact]
    public void TotalsOfNoColumnsAreZero()
    {
        History history = Fresh();

        History.Totals totals = history.Summarise(FrameStats.Categories.Count);

        Assert.Equal(0, totals.AvgFrameMs);
        Assert.All(totals.AvgMs, ms => Assert.Equal(0, ms));
    }

    [Fact]
    public void ACategoryNewerThanAColumnReadsZeroInIt()
    {
        History history = Fresh();
        Frames(1);
        history.Sample(Now, All);

        FrameStats.GetCategory("PanelTest.Late." + Guid.NewGuid());
        int late = FrameStats.Categories.Count - 1;

        History.Column column = history.Ago(0)!;
        Assert.Equal(0, column.Avg(late));
        Assert.Equal(0, column.Max(late));
        Assert.Equal(0, column.Calls(late));
        Assert.Equal(0, history.Summarise(FrameStats.Categories.Count).AvgMs[late]);
    }

    [Fact]
    public void AGapLongerThanTheRuntimeHistoryTakesWhatIsLeft()
    {
        History history = Fresh();

        Frames(FrameStats.HistoryLength + 10);
        history.Sample(Now, All);

        Assert.Equal(FrameStats.HistoryLength, history.Ago(0)!.Frames);
    }

    [Theory]
    [InlineData(0f, 8f)]
    [InlineData(7.9f, 8f)]
    [InlineData(8.1f, 12f)]
    [InlineData(16.67f, 20f)]
    [InlineData(40f, 40f)]
    public void AutoScaleRoundsUpTo4MsAndNeverGoesUnder8(float tallest, float expected)
    {
        Assert.Equal(expected, FrameStatsPanel.AutoScaleMs(tallest));
    }

    [Theory]
    [InlineData(199.5f, 0)]   // the right edge is the newest column
    [InlineData(0f, 199)]     // the left edge is the oldest
    [InlineData(100f, 99)]
    public void ColumnsCountBackFromTheRightEdge(float x, int expectedAgo)
    {
        Assert.Equal(expectedAgo, FrameStatsPanel.ColumnAt(x, width: 200f, columns: 200));
    }
}
