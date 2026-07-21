// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Rendering;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>Records OnRenderCollect invocations (the collect phase is pure CPU - it just appends to
/// the provided lists - so it's headless-testable).</summary>
public sealed class RenderCollectProbe : MonoBehaviour
{
    public int Calls;
    public override void OnRenderCollect(SceneCuller culler) => Calls++;
}

/// <summary>Records DrawGizmos invocations.</summary>
public sealed class GizmoProbe : MonoBehaviour
{
    public int Calls;
    public override void DrawGizmos() => Calls++;
}

/// <summary>
/// Tests that the render/gizmo callbacks are driven by the per-scene component registry (the same
/// mechanism as Update), including enable filtering and the NoGizmos hide-flag. These callbacks are
/// NOT gameplay-gated - they run regardless of play mode.
/// </summary>
public class RegistryRenderTests : RuntimeTestBase
{
    [Fact]
    public void CollectRenderables_InvokesRegisteredComponents()
    {
        var scene = CreateScene(enable: true);
        var go = CreateGameObject();
        var probe = go.AddComponent<RenderCollectProbe>();
        scene.Add(go);

        scene.CollectRenderables();

        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public void CollectRenderables_SkipsDisabledComponent()
    {
        var scene = CreateScene(enable: true);
        var go = CreateGameObject();
        var probe = go.AddComponent<RenderCollectProbe>();
        scene.Add(go);
        probe.Enabled = false;

        scene.CollectRenderables();

        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public void CollectRenderables_RunsEvenWhenNotPlaying()
    {
        Application.IsPlaying = false; // rendering happens in edit mode too
        var scene = CreateScene(enable: true);
        var go = CreateGameObject();
        var probe = go.AddComponent<RenderCollectProbe>();
        scene.Add(go);

        scene.CollectRenderables();

        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public void DrawGizmos_InvokesRegisteredComponents()
    {
        var scene = CreateScene(enable: true);
        var go = CreateGameObject();
        var probe = go.AddComponent<GizmoProbe>();
        scene.Add(go);

        scene.DrawGizmos();

        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public void DrawGizmos_SkipsNoGizmosHideFlag()
    {
        var scene = CreateScene(enable: true);
        var go = CreateGameObject();
        var probe = go.AddComponent<GizmoProbe>();
        probe.HideFlags = HideFlags.NoGizmos;
        scene.Add(go);

        scene.DrawGizmos();

        Assert.Equal(0, probe.Calls);
    }
}
