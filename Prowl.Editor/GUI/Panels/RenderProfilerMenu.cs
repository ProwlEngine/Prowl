// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Core;

namespace Prowl.Editor.GUI.Panels;

/// <summary>Window menu entries for Graphite's render profiler and debugger viewer.</summary>
internal static class RenderProfilerMenu
{
    [MenuItem("Window/Debug/Render Profiler", priority: 101)]
    static void OpenProfiler() => EditorApplication.Instance?.RenderProfiler?.Windows?.ShowLive();

    [MenuItem("Window/Debug/Render Snapshots", priority: 102)]
    static void OpenSnapshots() => EditorApplication.Instance?.RenderProfiler?.Windows?.ShowSnapshots();

    [MenuItem("Window/Debug/Load Render Snapshot...", priority: 103)]
    static void LoadSnapshot() => EditorApplication.Instance?.RenderProfiler?.Windows?.LoadSnapshot();
}
