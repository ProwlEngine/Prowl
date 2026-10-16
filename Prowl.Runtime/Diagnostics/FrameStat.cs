// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime.Diagnostics;

/// <summary>Time and scope count a category recorded in one frame.</summary>
public readonly record struct FrameStatSample(float Ms, int Calls);

/// <summary>A category's timings summarised over a run of frames.</summary>
public readonly record struct FrameStatSummary(float Min, float Max, float Avg, int Calls);

/// <summary>
/// A named bucket of main-thread time, such as Physics or Render. See <see cref="FrameStats"/>, which
/// holds the built-in ones and makes new ones with <see cref="FrameStats.GetCategory"/>.
/// </summary>
public sealed class FrameStat
{
    /// <summary>The last part of <see cref="Path"/>, such as "Gizmos".</summary>
    public string Name { get; }

    /// <summary>The full name, parents first and separated by '/', such as "Editor/Gizmos". Unique among categories.</summary>
    public string Path { get; }

    public Color Color { get; }

    /// <summary>
    /// The category this one is grouped under, or null at the top level. Grouping only: a subcategory's time is
    /// never counted in its parent's, wherever their scopes open.
    /// </summary>
    public FrameStat? Parent { get; }

    /// <summary>The categories grouped under this one, in the order they were made.</summary>
    public IReadOnlyList<FrameStat> Children => _children;

    /// <summary>
    /// Whether viewers such as the editor's Frame Stats panel leave it out until asked for: waiting rather than work,
    /// or work most people are not looking at.
    /// </summary>
    public bool HiddenByDefault { get; }

    /// <summary>Where it sits in <see cref="FrameStats.Categories"/>. Always after its parent.</summary>
    internal int Index;

    // Copy-on-write, under the registry lock, so readers on any thread can enumerate without one.
    private FrameStat[] _children = [];

    private readonly float[] _ms = new float[FrameStats.HistoryLength];
    private readonly int[] _calls = new int[FrameStats.HistoryLength];

    // The frame in progress. Main thread only.
    internal long Ticks;
    internal int Calls;

    internal FrameStat(string name, Color color, FrameStat? parent = null, bool hiddenByDefault = false)
    {
        Name = name;
        Path = parent == null ? name : $"{parent.Path}/{name}";
        Color = color;
        Parent = parent;
        HiddenByDefault = hiddenByDefault;
        FrameStats.Register(this);
    }

    internal void AddChild(FrameStat child) => _children = [.. _children, child];

    /// <summary>
    /// Starts charging main-thread time to this category until the returned scope is disposed.
    /// Use it with <c>using</c>. Does nothing off the main thread.
    /// </summary>
    public FrameStatScope Scope() => FrameStats.Enter(this);

    /// <summary>What this category recorded in a frame. 0 is the most recent frame.</summary>
    public FrameStatSample GetSample(int framesAgo)
    {
        int slot = FrameStats.Slot(framesAgo);
        return new FrameStatSample(_ms[slot], _calls[slot]);
    }

    /// <summary>Min, max and average time, and total scopes, over the most recent <paramref name="frames"/> frames.</summary>
    public FrameStatSummary GetStats(int frames)
    {
        int count = Math.Min(frames, FrameStats.HistoryCount);
        if (count <= 0) return default;

        float min = float.MaxValue, max = 0, sum = 0;
        int calls = 0;
        for (int i = 0; i < count; i++)
        {
            int slot = FrameStats.Slot(i);
            float ms = _ms[slot];
            min = Math.Min(min, ms);
            max = Math.Max(max, ms);
            sum += ms;
            calls += _calls[slot];
        }
        return new FrameStatSummary(min, max, sum / count, calls);
    }

    internal void Record(int slot, float ms)
    {
        _ms[slot] = ms;
        _calls[slot] = Calls;
    }

    public override string ToString() => Path;
}
