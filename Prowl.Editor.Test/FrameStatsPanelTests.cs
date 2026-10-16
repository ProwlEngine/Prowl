// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;

using Prowl.Editor.Core.Diagnostics;
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
    private static readonly HashSet<FrameStat> NoneHidden = [];

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
        history.Sample(Now, NoneHidden);

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
        history.Sample(Never, NoneHidden);
        Frames(2);
        history.Sample(Never, NoneHidden);
        Assert.Equal(0, history.Count);

        history.Sample(Now, NoneHidden);
        Assert.Equal(5, history.Ago(0)!.Frames);
    }

    [Fact]
    public void NoNewFramesMakesNoColumn()
    {
        History history = Fresh();

        history.Sample(Now, NoneHidden);

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
            history.Sample(Now, NoneHidden);
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
        history.Sample(Now, NoneHidden);
        long id = history.IdOf(0);

        Frames(1);
        history.Sample(Now, NoneHidden);
        Assert.Equal(1, history.AgoOf(id));

        for (int i = 0; i < 2; i++)
        {
            Frames(1);
            history.Sample(Now, NoneHidden);
        }
        Assert.Equal(-1, history.AgoOf(id));
        Assert.Equal(-1, history.AgoOf(-1));
    }

    [Fact]
    public void SkipDropsTheFramesSinceTheLastSample()
    {
        History history = Fresh();

        Frames(3);
        history.Sample(Never, NoneHidden);
        Frames(2);
        history.Skip();
        history.Sample(Now, NoneHidden);

        Assert.Equal(0, history.Count);
    }

    [Fact]
    public void ClearDropsEveryColumn()
    {
        History history = Fresh();
        Frames(2);
        history.Sample(Now, NoneHidden);

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
        history.Sample(Now, new HashSet<FrameStat>(FrameStats.Categories.Where(c => c != small)));

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
        history.Sample(Now, NoneHidden);
        Frames(3);
        history.Sample(Now, NoneHidden);

        History.Totals totals = history.Summarise(FrameStats.Categories.Count, history.Count);
        float avg = totals.AvgMs[IndexOf(stat)];
        Assert.True(avg >= 2.4f && avg < 4f, $"avg {avg}, an unweighted mean would be about 5");
        Assert.True(totals.MaxMs[IndexOf(stat)] >= 9.9f);
    }

    [Fact]
    public void TotalsFollowTheColumnsAfterAClear()
    {
        FrameStat stat = FrameStats.GetCategory("PanelTest.Recount");
        History history = Fresh();
        int i = IndexOf(stat);

        Frames(1, stat, spinMs: 10);
        history.Sample(Now, NoneHidden);
        Assert.True(history.Summarise(FrameStats.Categories.Count, 1).AvgMs[i] >= 9.9f);

        // Same number of columns as before the clear, so only the new data can tell the totals apart.
        history.Clear();
        Frames(1);
        history.Sample(Now, NoneHidden);

        Assert.Equal(1, history.Count);
        Assert.True(history.Summarise(FrameStats.Categories.Count, 1).AvgMs[i] < 1f);
    }

    [Fact]
    public void TotalsOfNoColumnsAreZero()
    {
        History history = Fresh();

        History.Totals totals = history.Summarise(FrameStats.Categories.Count, history.Count);

        Assert.Equal(0, totals.AvgFrameMs);
        Assert.All(totals.AvgMs, ms => Assert.Equal(0, ms));
    }

    [Fact]
    public void ACategoryNewerThanAColumnReadsZeroInIt()
    {
        History history = Fresh();
        Frames(1);
        history.Sample(Now, NoneHidden);

        FrameStats.GetCategory("PanelTest.Late." + Guid.NewGuid());
        int late = FrameStats.Categories.Count - 1;

        History.Column column = history.Ago(0)!;
        Assert.Equal(0, column.Avg(late));
        Assert.Equal(0, column.Max(late));
        Assert.Equal(0, column.Calls(late));
        Assert.Equal(0, history.Summarise(FrameStats.Categories.Count, history.Count).AvgMs[late]);
    }

    [Fact]
    public void AGapLongerThanTheRuntimeHistoryTakesWhatIsLeft()
    {
        History history = Fresh();

        Frames(FrameStats.HistoryLength + 10);
        history.Sample(Now, NoneHidden);

        Assert.Equal(FrameStats.HistoryLength, history.Ago(0)!.Frames);
    }

    [Theory]
    [InlineData(0f, 0.5f)]       // nothing to show still gets a usable scale
    [InlineData(0.3f, 0.5f)]     // under the floor
    [InlineData(1.4f, 2f)]       // fills the chart, rather than a sixth of an 8 ms one
    [InlineData(2.5f, 3f)]
    [InlineData(4f, 5f)]
    [InlineData(6f, 8f)]
    [InlineData(8f, 10f)]        // headroom, so the tallest column never touches the top
    [InlineData(16.67f, 20f)]
    [InlineData(40f, 50f)]
    [InlineData(95f, 150f)]
    public void AutoScaleFitsTheTallestColumnToARoundNumber(float tallest, float expected)
    {
        Assert.Equal(expected, FrameStatsPanel.AutoScaleMs(tallest), 0.001f);
    }

    [Fact]
    public void AutoScaleAlwaysLeavesRoomAboveTheTallestColumn()
    {
        for (float tallest = 0.5f; tallest < 200f; tallest *= 1.07f)
        {
            float scale = FrameStatsPanel.AutoScaleMs(tallest);
            Assert.True(scale >= tallest * 1.09f, $"{tallest} ms got {scale} ms");
            Assert.True(scale <= tallest * 1.1f * 1.5f + 0.001f, $"{tallest} ms got {scale} ms, more than half empty");
        }
    }

    [Theory]
    [InlineData(199.5f, 1f, 0)]   // the right edge is the newest column
    [InlineData(0.5f, 1f, 199)]   // and the left edge the oldest that fits
    [InlineData(196.5f, 4f, 0)]   // wider columns: still the newest within its 4 px
    [InlineData(195.5f, 4f, 1)]
    public void ColumnsCountBackFromTheRightEdge(float x, float columnW, int expectedAgo)
    {
        Assert.Equal(expectedAgo, FrameStatsPanel.ColumnAt(x, width: 200f, columnW));
    }

    [Fact]
    public void SummariseCoversOnlyTheNewestColumnsAskedFor()
    {
        History history = Fresh();
        Frames(3);
        history.Sample(Now, NoneHidden);
        Frames(1);
        history.Sample(Now, NoneHidden);

        // Only the newest column, of one frame, against both columns' four frames.
        History.Totals newest = history.Summarise(FrameStats.Categories.Count, 1);
        History.Totals both = history.Summarise(FrameStats.Categories.Count, 2);
        Assert.Equal(FrameStats.GetFrameMs(0), newest.AvgFrameMs, 0.001f);
        Assert.True(both.AvgFrameMs >= 0);
    }

    [Fact]
    public void TakingInColumnsAllocatesNothingOnceTheRingIsFull()
    {
        History history = Fresh(capacity: 4);
        // Fill the ring, and let every per-category buffer grow to size.
        for (int i = 0; i < 8; i++)
        {
            Frames(1);
            history.Sample(Now, NoneHidden);
        }
        history.Summarise(FrameStats.Categories.Count, history.Count);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            FrameStats.EndFrame();
            history.Sample(Now, NoneHidden);
            history.Summarise(FrameStats.Categories.Count, history.Count);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    // Through JSON text, the way the dock layout file stores it.
    private static JsonObject RoundTrip(FrameStatsPanel panel)
    {
        var state = new JsonObject();
        Assert.True(panel.SerializeState(state));
        return (JsonObject)JsonNode.Parse(state.ToJsonString())!;
    }

    [Fact]
    public void SettingsSurviveSavingAndRestoring()
    {
        var saved = new JsonObject
        {
            ["rate"] = 4,
            ["scale"] = 2,
            ["stackMax"] = true,
            ["hideUnselected"] = true,
            ["columnWidth"] = 12.5,
            ["split"] = 0.25,
            ["shown"] = new JsonObject { ["Physics"] = false, ["render wait"] = true },
            ["otherHidden"] = new JsonArray("Editor"),
            ["expanded"] = new JsonArray("Editor"),
        };
        var panel = new FrameStatsPanel();
        panel.RestoreState((JsonObject)JsonNode.Parse(saved.ToJsonString())!);

        JsonObject restored = RoundTrip(panel);

        Assert.Equal(4, restored["rate"]!.GetValue<int>());
        Assert.Equal(2, restored["scale"]!.GetValue<int>());
        Assert.True(restored["stackMax"]!.GetValue<bool>());
        Assert.True(restored["hideUnselected"]!.GetValue<bool>());
        Assert.Equal(12.5, restored["columnWidth"]!.GetValue<double>(), 3);
        Assert.Equal(0.25, restored["split"]!.GetValue<double>(), 3);
        Assert.False(restored["shown"]!["Physics"]!.GetValue<bool>());
        Assert.Equal(["Editor"], restored["otherHidden"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(["Editor"], restored["expanded"]!.AsArray().Select(n => n!.GetValue<string>()));

        // Paths match whatever the case.
        var reopened = new FrameStatsPanel();
        reopened.RestoreState(restored);
        Assert.True(reopened.IsHidden(FrameStats.Physics));
        Assert.False(reopened.IsHidden(FrameStats.RenderWait));
        Assert.True(reopened.IsExpanded(FrameStats.Editor));
    }

    [Fact]
    public void HidingUnselectedLeavesOnlySwitchedOnRowsListed()
    {
        var panel = new FrameStatsPanel();
        // Every row is listed until asked otherwise, switched off or not.
        Assert.True(panel.IsListed(FrameStats.Idle));
        Assert.True(panel.IsListed(FrameStats.Editor, other: true));

        panel.ToggleVisibility(EditorFrameStats.UI);
        panel.HideUnselected = true;

        Assert.False(panel.IsListed(FrameStats.Idle));
        Assert.True(panel.IsListed(FrameStats.Render));
        // Showing one row under a hidden group brings the group back with only that row on.
        Assert.True(panel.IsListed(FrameStats.Editor));
        Assert.True(panel.IsListed(EditorFrameStats.UI));
        Assert.False(panel.IsListed(EditorFrameStats.Gizmos));
        Assert.False(panel.IsListed(FrameStats.Editor, other: true));

        panel.HideUnselected = false;
        Assert.True(panel.IsListed(EditorFrameStats.Gizmos));
    }

    [Fact]
    public void ANewPanelFollowsEachCategoryDefault()
    {
        var panel = new FrameStatsPanel();

        Assert.True(panel.IsHidden(FrameStats.Idle));
        Assert.True(panel.IsHidden(FrameStats.RenderWait));
        Assert.True(panel.IsRowHidden(FrameStats.Editor));
        Assert.True(panel.IsHidden(EditorFrameStats.UI));
        Assert.False(panel.IsHidden(FrameStats.Render));
        Assert.False(panel.IsExpanded(FrameStats.Editor));

        // Defaults are not written out, so a later change to one still reaches saved layouts.
        JsonObject state = RoundTrip(panel);
        Assert.Equal(2, state["rate"]!.GetValue<int>());
        Assert.Null(state["split"]);
        Assert.Empty(state["shown"]!.AsObject());
    }

    [Fact]
    public void LayoutsFromBeforeGroupsKeepTheirHiddenRowsAndDefaultTheRest()
    {
        var panel = new FrameStatsPanel();
        panel.RestoreState(new JsonObject { ["hidden"] = new JsonArray("Physics", "editor") });

        Assert.True(panel.IsHidden(FrameStats.Physics));
        Assert.True(panel.IsHidden(EditorFrameStats.UI));
        Assert.True(panel.IsHidden(FrameStats.RenderWait));
        Assert.False(panel.IsHidden(FrameStats.Render));
    }

    [Fact]
    public void BadSavedValuesAreIgnored()
    {
        var saved = new JsonObject
        {
            ["rate"] = 99,
            ["scale"] = -1,
            ["stackMax"] = "yes",
            ["columnWidth"] = 1000.0,
            ["split"] = 1.5,
            ["shown"] = new JsonObject { ["Physics"] = "no" },
            ["expanded"] = new JsonArray(7),
        };
        var panel = new FrameStatsPanel();
        int categoriesBefore = FrameStats.Categories.Count;

        panel.RestoreState((JsonObject)JsonNode.Parse(saved.ToJsonString())!);
        JsonObject state = RoundTrip(panel);

        Assert.Equal(2, state["rate"]!.GetValue<int>());
        Assert.Equal(0, state["scale"]!.GetValue<int>());
        Assert.False(state["stackMax"]!.GetValue<bool>());
        Assert.Equal(32.0, state["columnWidth"]!.GetValue<double>(), 3);
        Assert.Null(state["split"]);
        Assert.Empty(state["shown"]!.AsObject());
        Assert.Empty(state["expanded"]!.AsArray());
        Assert.Equal(categoriesBefore, FrameStats.Categories.Count);
    }

    [Fact]
    public void ASavedChoiceReachesACategoryMadeLater()
    {
        string path = "PanelTest.Later." + Guid.NewGuid();
        var panel = new FrameStatsPanel();
        int categoriesBefore = FrameStats.Categories.Count;

        panel.RestoreState(new JsonObject { ["shown"] = new JsonObject { [path] = false } });
        // Restoring does not make the category.
        Assert.Equal(categoriesBefore, FrameStats.Categories.Count);

        Assert.True(panel.IsHidden(FrameStats.GetCategory(path)));
    }

    [Fact]
    public void AGroupHidesEverythingUnderItIncludingLaterSubcategories()
    {
        string root = "PanelTest.Group." + Guid.NewGuid();
        FrameStat group = FrameStats.GetCategory(root);
        FrameStat child = FrameStats.GetCategory($"{root}/Child");
        var panel = new FrameStatsPanel();

        panel.ToggleVisibility(group);
        FrameStat late = FrameStats.GetCategory($"{root}/Late/Deeper");

        Assert.True(panel.IsHidden(group));
        Assert.True(panel.IsHidden(child));
        Assert.True(panel.IsHidden(late));
        // Subcategories keep their own switch, so they come back with the group.
        panel.ToggleVisibility(group);
        Assert.False(panel.IsHidden(child));
        Assert.False(panel.IsHidden(late));
    }

    [Fact]
    public void ShowingARowUnderAHiddenGroupShowsOnlyThatRow()
    {
        var panel = new FrameStatsPanel();
        panel.ToggleVisibility(EditorFrameStats.UI);

        Assert.False(panel.IsHidden(EditorFrameStats.UI));
        Assert.False(panel.IsRowHidden(FrameStats.Editor));
        // The group's own time and its other subcategories stay off.
        Assert.True(panel.IsHidden(FrameStats.Editor));
        foreach (FrameStat sibling in FrameStats.Editor.Children)
            if (sibling != EditorFrameStats.UI)
                Assert.True(panel.IsHidden(sibling));

        // The choice survives reopening the panel.
        var restored = new FrameStatsPanel();
        restored.RestoreState(RoundTrip(panel));
        Assert.False(restored.IsHidden(EditorFrameStats.UI));
        Assert.True(restored.IsHidden(FrameStats.Editor));

        // Further clicks toggle just that row.
        restored.ToggleVisibility(EditorFrameStats.UI);
        Assert.True(restored.IsHidden(EditorFrameStats.UI));
        Assert.False(restored.IsRowHidden(FrameStats.Editor));
    }

    [Fact]
    public void ShowingTheOtherRowOfAHiddenGroupShowsOnlyItsOwnTime()
    {
        var panel = new FrameStatsPanel();
        Assert.True(panel.IsOtherHidden(FrameStats.Editor));

        panel.ToggleVisibility(FrameStats.Editor, other: true);

        Assert.False(panel.IsHidden(FrameStats.Editor));
        foreach (FrameStat child in FrameStats.Editor.Children)
            Assert.True(panel.IsHidden(child));

        panel.ToggleVisibility(FrameStats.Editor, other: true);
        Assert.True(panel.IsHidden(FrameStats.Editor));
        Assert.False(panel.IsRowHidden(FrameStats.Editor));
    }

    [Fact]
    public void AGroupTotalAddsUpEverythingUnderItWithinEachFrame()
    {
        string root = "PanelTest.Tree." + Guid.NewGuid();
        FrameStat group = FrameStats.GetCategory(root);
        FrameStat first = FrameStats.GetCategory($"{root}/First");
        FrameStat second = FrameStats.GetCategory($"{root}/Sub/Second");
        History history = Fresh();
        Frames(1, first, 2);
        Frames(1, second, 2);
        float firstMs = first.GetSample(1).Ms;
        float secondMs = second.GetSample(0).Ms;

        history.Sample(Now, NoneHidden);
        History.Column column = Assert.IsType<History.Column>(history.Ago(0));
        int g = IndexOf(group), sub = IndexOf(second.Parent!);
        Assert.Equal((firstMs + secondMs) / 2, column.TreeAvg(g), 4);
        Assert.Equal(Math.Max(firstMs, secondMs), column.TreeMax(g), 4);
        Assert.Equal(1, column.TreeCalls(g));
        Assert.Equal(secondMs / 2, column.TreeAvg(sub), 4);
        // A category without subcategories totals only itself.
        Assert.Equal(column.Avg(IndexOf(first)), column.TreeAvg(IndexOf(first)));

        History.Totals totals = history.Summarise(FrameStats.Categories.Count, 1);
        Assert.Equal(column.TreeAvg(g), totals.TreeAvgMs[g]);
        Assert.Equal(column.TreeMax(g), totals.TreeMaxMs[g]);
    }
}
