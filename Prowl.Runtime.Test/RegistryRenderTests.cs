// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>Records DrawGizmos invocations.</summary>
public sealed class GizmoProbe : Component
{
    public int Calls;
    public override void DrawGizmos() => Calls++;
}

/// <summary>
/// Gizmo callbacks are driven by the per scene component registry (the same mechanism as Update), honour the
/// NoGizmos hide flag and run regardless of play mode.
/// </summary>
public class RegistryRenderTests : RuntimeTestBase
{
    [Fact]
    public void DrawGizmos_InvokesRegisteredComponents()
    {
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        GizmoProbe probe = go.AddComponent<GizmoProbe>();
        scene.Add(go);

        scene.DrawGizmos();

        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public void DrawGizmos_RunsEvenWhenNotPlaying()
    {
        Application.IsPlaying = false;
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        GizmoProbe probe = go.AddComponent<GizmoProbe>();
        scene.Add(go);

        scene.DrawGizmos();

        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public void DrawGizmos_SkipsNoGizmosHideFlag()
    {
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        GizmoProbe probe = go.AddComponent<GizmoProbe>();
        probe.HideFlags = HideFlags.NoGizmos;
        scene.Add(go);

        scene.DrawGizmos();

        Assert.Equal(0, probe.Calls);
    }
}
