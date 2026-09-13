// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Prowl.Runtime;
using Prowl.Runtime.Navigation;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Drains <see cref="NavMeshBakeJob"/> tasks kicked off by <see cref="NavMeshSurfaceEditor"/>'s Bake
/// button back onto the editor's main thread. Exists because <see cref="Tasks.MainThreadContext"/> -
/// the engine's normal mechanism for this - is only installed during a Play session or a headless run
/// (see its own doc comment); a plain editor bake happens with neither, so nothing else would ever
/// marshal the finished result back. <see cref="EditorApplication.BeginGui"/> calls
/// <see cref="ProcessCompleted"/> once per editor frame, the same way it drains
/// <c>ThumbnailGenerator</c>'s queue - not <see cref="NavMeshSurfaceEditor.OnGUI"/> itself, since that
/// only runs while the surface happens to be the selected, inspected object, and a bake must still
/// finish if the user clicks away from it mid-bake.
/// </summary>
internal static class NavMeshBakePump
{
    /// <summary>A bake in flight for one surface: the task tracking it and what to run once it lands.</summary>
    private readonly record struct PendingBake(Task<NavMeshBuildResult> Task, Action<NavMeshBuildResult> OnCompleted);

    /// <summary>Every bake currently in flight, keyed by the surface it's for.</summary>
    private static readonly Dictionary<NavMeshSurface, PendingBake> s_pending = [];

    /// <summary>Whether a bake started by <see cref="Enqueue"/> for this surface hasn't been picked up
    /// by <see cref="ProcessCompleted"/> yet.</summary>
    internal static bool IsBaking(NavMeshSurface surface) => s_pending.ContainsKey(surface);

    /// <summary>Registers a running bake. <paramref name="onCompleted"/> runs on the main thread, once,
    /// the first time <see cref="ProcessCompleted"/> observes <paramref name="task"/> as finished
    /// successfully. A second <see cref="Enqueue"/> for the same surface (a rebake started before the
    /// first one lands) replaces the pending entry - only the newest bake's result is ever committed.</summary>
    internal static void Enqueue(NavMeshSurface surface, Task<NavMeshBuildResult> task, Action<NavMeshBuildResult> onCompleted) =>
        s_pending[surface] = new PendingBake(task, onCompleted);

    /// <summary>Picks up every bake that has finished since the last call and runs its completion
    /// callback. Safe to call every frame regardless of whether anything is pending.</summary>
    internal static void ProcessCompleted()
    {
        if (s_pending.Count == 0) return;

        List<NavMeshSurface>? finished = null;
        foreach (KeyValuePair<NavMeshSurface, PendingBake> entry in s_pending)
        {
            if (entry.Value.Task.IsCompleted)
                (finished ??= []).Add(entry.Key);
        }
        if (finished == null) return;

        foreach (NavMeshSurface surface in finished)
        {
            PendingBake pending = s_pending[surface];
            s_pending.Remove(surface);

            if (pending.Task.IsFaulted)
                Debug.LogError($"[NavMeshSurface] Bake threw: {pending.Task.Exception?.GetBaseException().Message}");
            else if (!pending.Task.IsCanceled)
                pending.OnCompleted(pending.Task.Result);
        }
    }
}
