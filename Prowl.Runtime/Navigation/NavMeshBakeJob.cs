// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Threading.Tasks;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// Runs a navmesh bake's expensive Recast pipeline on a thread pool thread instead of blocking the
/// caller. <see cref="NavMeshGeometryCollector.Collect"/> and resolving a surface's
/// <see cref="NavMeshSurface.EffectiveBakeSettings"/> must both still happen on the caller's own thread
/// first - they read the live scene and the mutable <see cref="NavMeshAgentTypes"/>/<see cref="NavMeshAreas"/>
/// registries, neither of which is safe to touch from here. What crosses into this job is only the
/// immutable snapshot those two steps already produce (a plain triangle soup and a settings struct), so
/// <see cref="RecastNavMeshBuilder.Build"/> itself never reaches back into mutable engine state.
/// <para/>
/// If <see cref="Tasks.MainThreadContext"/> is installed (a Play session or a headless run), awaiting
/// the returned task resumes the caller there automatically - ordinary C# <c>await</c> behavior, not
/// anything this class does itself. Outside a Play session (an editor-only bake, the common case)
/// nothing installs a synchronization context, so the continuation resumes on whatever thread pool
/// thread finished the work; a caller in that situation must marshal back to its own main thread itself
/// - see <c>NavMeshSurfaceEditor</c>'s bake pump for how the editor does that.
/// </summary>
public static class NavMeshBakeJob
{
    /// <summary>Builds <paramref name="input"/> with <paramref name="settings"/> on a thread pool thread.</summary>
    public static Task<NavMeshBuildResult> RunAsync(NavMeshBuildInput input, NavMeshBakeSettings settings) =>
        Task.Run(() => new RecastNavMeshBuilder().Build(input, settings));
}
