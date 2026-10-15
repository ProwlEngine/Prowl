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
    /// <summary>The Frame Stats panel, which draws itself on the canvas so its cost can be told apart from <see cref="UI"/>.</summary>
    public static readonly FrameStat FrameStatsPanel = FrameStats.GetCategory("Editor/Frame Stats UI");
}
