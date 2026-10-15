// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Diagnostics;
using Prowl.Vector;

namespace Prowl.Editor.Core.Diagnostics;

/// <summary>
/// Cached subcategories of <see cref="FrameStats.Editor"/>, registered with the shared frame profiler. Editor work too
/// small or too rare for its own category is scoped to <see cref="FrameStats.Editor"/> itself.
/// </summary>
internal static class EditorFrameStats
{
    /// <summary>The editor's Paper frame, excluding work claimed by nested scopes.</summary>
    public static readonly FrameStat UI = FrameStats.GetCategory("Editor/UI", Color32.FromArgb(255, 0x73, 0xb5, 0xc4));
    public static readonly FrameStat Gizmos = FrameStats.GetCategory("Editor/Gizmos");
    public static readonly FrameStat Assets = FrameStats.GetCategory("Editor/Assets");
    public static readonly FrameStat Scripts = FrameStats.GetCategory("Editor/Scripts");

    /// <summary>
    /// The Frame Stats panel's own work, kept apart from <see cref="UI"/> so the profiler skews the editor's
    /// numbers as little as possible. Paper's layout and upload of its elements still count as UI.
    /// </summary>
    public static readonly FrameStat FrameStatsUI = FrameStats.GetCategory("Editor/Frame Stats UI");
}
