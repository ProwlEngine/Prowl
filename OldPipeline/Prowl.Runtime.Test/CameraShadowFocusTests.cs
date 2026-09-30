// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests for <see cref="Camera.ShadowFocus"/>, the optional transform directional shadow cascades
/// center on instead of the camera. Covers the resolver's fallbacks (no target, destroyed target),
/// that it tracks a live target, and that the reference survives a scene save/load - the field is
/// a cross-object Transform reference, so Echo has to rewire it to the deserialized instance rather
/// than clone a detached copy.
/// </summary>
public class CameraShadowFocusTests : RuntimeTestBase
{
    private Camera CreateCamera(Float3 position, string name = "Camera")
    {
        GameObject go = CreateGameObject(name);
        go.Transform.Position = position;
        return go.AddComponent<Camera>();
    }

    [Fact]
    public void Camera_GetShadowFocusPosition_NoTarget_ReturnsCameraPosition()
    {
        Camera camera = CreateCamera(new Float3(3f, 5f, -7f));

        Assert.Null(camera.ShadowFocus);
        Assert.Equal(new Float3(3f, 5f, -7f), camera.GetShadowFocusPosition());
    }

    [Fact]
    public void Camera_GetShadowFocusPosition_WithTarget_ReturnsTargetPosition()
    {
        Camera camera = CreateCamera(new Float3(0f, 6f, -10f));
        GameObject player = CreateGameObject("Player");
        player.Transform.Position = new Float3(0f, 0f, 25f);

        camera.ShadowFocus = player.Transform;

        Assert.Equal(new Float3(0f, 0f, 25f), camera.GetShadowFocusPosition());

        // The focus point has to follow the target, not latch onto where it was when assigned.
        player.Transform.Position = new Float3(12f, 1f, 30f);
        Assert.Equal(new Float3(12f, 1f, 30f), camera.GetShadowFocusPosition());
    }

    [Fact]
    public void Camera_GetShadowFocusPosition_TargetDestroyed_FallsBackToCameraPosition()
    {
        Camera camera = CreateCamera(new Float3(-2f, 4f, 9f));
        GameObject player = CreateGameObject("Player");
        player.Transform.Position = new Float3(50f, 0f, 50f);
        camera.ShadowFocus = player.Transform;

        player.Destroy();
        EngineObject.ProcessDestroyed();

        // A stale Transform on a destroyed GameObject must not be read - and must not throw.
        Assert.Equal(new Float3(-2f, 4f, 9f), camera.GetShadowFocusPosition());
    }

    [Fact]
    public void Camera_ShadowFocus_SceneRoundTrip_RewiresReference()
    {
        Scene scene = CreateScene();
        Camera camera = CreateCamera(new Float3(0f, 6f, -10f), "Main Camera");
        GameObject player = CreateGameObject("Player");
        player.Transform.Position = new Float3(0f, 0f, 25f);
        camera.ShadowFocus = player.Transform;

        // Separate roots, so the reference crosses object boundaries inside the scene graph.
        scene.Add(camera.GameObject);
        scene.Add(player);

        Scene clone = Serializer.Deserialize<Scene>(Serializer.Serialize(scene));

        GameObject clonedCameraGO = Assert.Single(clone.AllObjects, g => g.Name == "Main Camera");
        GameObject clonedPlayer = Assert.Single(clone.AllObjects, g => g.Name == "Player");
        Camera? clonedCamera = clonedCameraGO.GetComponent<Camera>();

        Assert.NotNull(clonedCamera);
        Assert.NotNull(clonedCamera!.ShadowFocus);
        // Must be the deserialized player's own Transform, not a detached copy of it.
        Assert.Same(clonedPlayer.Transform, clonedCamera.ShadowFocus);
        Assert.Equal(new Float3(0f, 0f, 25f), clonedCamera.GetShadowFocusPosition());
    }
}
