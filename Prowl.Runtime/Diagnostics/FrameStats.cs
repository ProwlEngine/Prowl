// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;

using Prowl.Vector;

namespace Prowl.Runtime.Diagnostics;

/// <summary>
/// Where each frame's main-thread time goes, by category, over the last <see cref="HistoryLength"/> frames.
/// <code>
/// using FrameStatScope stats = FrameStats.Physics.Scope();  // built-in category, to the end of the method
/// using (FrameStats.Scope("AI")) UpdateAgents();             // your own, created on first use
/// using (FrameStats.Scope("AI/Paths")) FindPaths();          // grouped under AI in viewers
/// float avg = FrameStats.Render.GetStats(60).Avg;            // ms per frame, last 60 frames
/// </code>
/// Nested scopes count only their own time, so a frame's categories add up to its length, with unscoped
/// time in <see cref="Other"/> and GC pauses in <see cref="GC"/>. Scopes off the main thread record nothing.
/// </summary>
public static class FrameStats
{
    /// <summary>How many frames of history are kept.</summary>
    public const int HistoryLength = 300;

    // Declared ahead of the built-in categories below: static initializers run in order, and each
    // category registers itself as it is created.
    private static readonly object s_registryLock = new();
    private static readonly Dictionary<string, FrameStat> s_byName = new(StringComparer.OrdinalIgnoreCase);
    // Copy-on-write so readers on any thread can enumerate without a lock.
    private static FrameStat[] s_categories = [];

    private static readonly Color[] s_palette =
    [
        Color32.FromArgb(255, 0xff, 0xd6, 0x6b), Color32.FromArgb(255, 0x9b, 0xf6, 0xff),
        Color32.FromArgb(255, 0xff, 0xad, 0xad), Color32.FromArgb(255, 0xca, 0xff, 0xbf),
        Color32.FromArgb(255, 0xa0, 0xc4, 0xff), Color32.FromArgb(255, 0xff, 0xc6, 0xff),
        Color32.FromArgb(255, 0xfd, 0xff, 0xb6), Color32.FromArgb(255, 0xff, 0xd6, 0xa5),
    ];

    public static readonly FrameStat Input = new("Input", Hex(0xe9, 0xff, 0x70));
    public static readonly FrameStat Async = new("Async", Hex(0xe9, 0xed, 0xc9));
    public static readonly FrameStat Assets = new("Assets", Hex(0xb5, 0x83, 0x8d));
    public static readonly FrameStat Update = new("Update", Hex(0x56, 0xcb, 0xf9));
    public static readonly FrameStat Physics = new("Physics", Hex(0xf3, 0x77, 0x48));
    public static readonly FrameStat Animation = new("Animation", Hex(0xff, 0x70, 0xa6));
    public static readonly FrameStat Navigation = new("Navigation", Hex(0x73, 0x8d, 0x45));
    public static readonly FrameStat Audio = new("Audio", Hex(0xbd, 0xb2, 0xff));
    public static readonly FrameStat Render = new("Render", Hex(0x8a, 0xc9, 0x26));
    public static readonly FrameStat UI = new("UI", Hex(0xb4, 0x86, 0x9f));
    /// <summary>The editor's own work. Its subcategories are made by the editor.</summary>
    public static readonly FrameStat Editor = new("Editor", Hex(0x9a, 0x9c, 0xa8), hiddenByDefault: true);

    /// <summary>Main thread blocked until the render thread has finished and presented earlier frames, vsync included.</summary>
    public static readonly FrameStat RenderWait = new("Render Wait", Hex(0x2a, 0x9d, 0x8f), hiddenByDefault: true);

    /// <summary>The frame limiter sleeping to hold the target frame rate.</summary>
    public static readonly FrameStat Idle = new("Idle", Hex(0x5c, 0x5c, 0x66), hiddenByDefault: true);

    /// <summary>Garbage collector pauses. Filled in at frame end, never scoped.</summary>
    public static readonly FrameStat GC = new("GC", Hex(0x00, 0xf5, 0xd4));

    /// <summary>Frame time no scope claimed. Filled in at frame end, never scoped.</summary>
    public static readonly FrameStat Other = new("Other", Hex(0x3e, 0x3e, 0x48));

    private static readonly float[] s_frameMs = new float[HistoryLength];
    private static int s_head;
    private static long s_frameCount;

    private static int s_mainThreadId;
    private static FrameStat? s_current;
    private static long s_segmentStart;
    private static long s_frameStart;
    private static long s_gcAtSegment;
    private static long s_gcAtFrame;

    /// <summary>Every category registered so far, subcategories included, in registration order. A parent always comes before its subcategories.</summary>
    public static IReadOnlyList<FrameStat> Categories => s_categories;

    /// <summary>Number of frames recorded since startup, including those that have left the history.</summary>
    public static long FrameCount => s_frameCount;

    /// <summary>Number of frames currently held in the history, at most <see cref="HistoryLength"/>.</summary>
    public static int HistoryCount => (int)Math.Min(s_frameCount, HistoryLength);

    /// <summary>Length of a recorded frame in milliseconds. 0 is the most recent frame.</summary>
    public static float GetFrameMs(int framesAgo) => s_frameMs[Slot(framesAgo)];

    /// <summary>
    /// Returns the category at this path, creating it the first time it is asked for. A path such as
    /// "Editor/Gizmos" makes a subcategory, and its parents too if they do not exist yet. Paths match whatever
    /// the case. <paramref name="color"/> is used only when the category is created; otherwise one is picked
    /// from its path.
    /// </summary>
    public static FrameStat GetCategory(string path, Color? color = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (s_registryLock)
        {
            if (s_byName.TryGetValue(path, out FrameStat? existing))
                return existing;

            int slash = path.LastIndexOf('/');
            string name = path[(slash + 1)..];
            if (string.IsNullOrWhiteSpace(name) || (slash >= 0 && string.IsNullOrWhiteSpace(path[..slash])))
                throw new ArgumentException($"Frame stat path '{path}' has an empty part.", nameof(path));

            FrameStat? parent = slash < 0 ? null : GetCategory(path[..slash]);
            return new FrameStat(name, color ?? DefaultColor(path), parent);
        }
    }

    /// <summary>Opens a scope on the category at this path. See <see cref="FrameStat.Scope"/>.</summary>
    public static FrameStatScope Scope(string category) => GetCategory(category).Scope();

    internal static void Register(FrameStat category)
    {
        lock (s_registryLock)
        {
            if (!s_byName.TryAdd(category.Path, category))
                throw new ArgumentException($"A frame stat named '{category.Path}' already exists.");
            category.Index = s_categories.Length;
            s_categories = [.. s_categories, category];
            category.Parent?.AddChild(category);
        }
    }

    /// <summary>
    /// Makes the calling thread the recorded one and starts the first frame. Called once by the
    /// main loop before it runs; until then every scope records nothing.
    /// </summary>
    internal static void Start()
    {
        s_mainThreadId = Environment.CurrentManagedThreadId;
        s_current = null;
        // A loop that ran before (an earlier headless run) can leave a frame half counted.
        foreach (FrameStat c in s_categories)
        {
            c.Ticks = 0;
            c.Calls = 0;
        }
        s_frameStart = s_segmentStart = Stopwatch.GetTimestamp();
        s_gcAtFrame = s_gcAtSegment = System.GC.GetTotalPauseDuration().Ticks;
    }

    internal static FrameStatScope Enter(FrameStat category)
    {
        if (Environment.CurrentManagedThreadId != s_mainThreadId)
            return default;

        CloseSegment(Stopwatch.GetTimestamp());
        FrameStat? parent = s_current;
        s_current = category;
        category.Calls++;
        return new FrameStatScope(parent);
    }

    internal static void Exit(FrameStat? parent)
    {
        CloseSegment(Stopwatch.GetTimestamp());
        s_current = parent;
    }

    // Charges the time since the last boundary to whichever category is on top, minus any GC pause
    // that happened in it. Unclaimed time is charged to nothing and turns up as Other at frame end.
    private static void CloseSegment(long now)
    {
        long elapsed = now - s_segmentStart;
        s_segmentStart = now;

        long gc = System.GC.GetTotalPauseDuration().Ticks;
        long gcTicks = gc - s_gcAtSegment;
        s_gcAtSegment = gc;

        if (s_current == null) return;

        if (gcTicks > 0)
            elapsed -= Math.Min(elapsed, gcTicks * Stopwatch.Frequency / TimeSpan.TicksPerSecond);
        s_current.Ticks += elapsed;
    }

    /// <summary>Closes the current frame and pushes it into the history. Called once per frame by the main loop.</summary>
    internal static void EndFrame()
    {
        if (Environment.CurrentManagedThreadId != s_mainThreadId)
            return;

        long now = Stopwatch.GetTimestamp();
        CloseSegment(now);

        float frameMs = ToMs(now - s_frameStart);
        s_frameStart = now;

        long gc = s_gcAtSegment;
        GC.Ticks = (gc - s_gcAtFrame) * Stopwatch.Frequency / TimeSpan.TicksPerSecond;
        s_gcAtFrame = gc;

        FrameStat[] categories = s_categories;

        float claimedMs = 0;
        foreach (FrameStat c in categories)
            if (c != Other)
                claimedMs += ToMs(c.Ticks);

        Other.Ticks = 0;
        Other.Calls = 0;

        int slot = s_head;
        s_frameMs[slot] = frameMs;
        foreach (FrameStat c in categories)
        {
            c.Record(slot, c == Other ? Math.Max(0, frameMs - claimedMs) : ToMs(c.Ticks));
            c.Ticks = 0;
            c.Calls = 0;
        }

        s_head = (s_head + 1) % HistoryLength;
        s_frameCount++;
    }

    internal static int Slot(int framesAgo)
    {
        if ((uint)framesAgo >= HistoryLength)
            throw new ArgumentOutOfRangeException(nameof(framesAgo), framesAgo, $"History holds {HistoryLength} frames.");
        return (s_head - 1 - framesAgo + HistoryLength * 2) % HistoryLength;
    }

    private static float ToMs(long ticks) => (float)(ticks * 1000.0 / Stopwatch.Frequency);

    private static Color Hex(byte r, byte g, byte b) => Color32.FromArgb(255, r, g, b);

    // Categories made on the fly get a stable colour from their path, so they keep it across runs.
    private static Color DefaultColor(string path)
    {
        uint hash = 2166136261;
        foreach (char ch in path)
            hash = (hash ^ char.ToLowerInvariant(ch)) * 16777619;
        return s_palette[hash % (uint)s_palette.Length];
    }
}

/// <summary>
/// An open frame stats scope, returned by <see cref="FrameStat.Scope"/>. Dispose it, normally
/// with <c>using</c>, to close it. Scopes must close in the reverse order they were opened.
/// </summary>
public ref struct FrameStatScope
{
    private readonly FrameStat? _parent;
    private bool _active;

    internal FrameStatScope(FrameStat? parent)
    {
        _parent = parent;
        _active = true;
    }

    public void Dispose()
    {
        // Once only: a second Exit would close whatever scope was opened after this one.
        if (!_active) return;
        _active = false;
        FrameStats.Exit(_parent);
    }
}
