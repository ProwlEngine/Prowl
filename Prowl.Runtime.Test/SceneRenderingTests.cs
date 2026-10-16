// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite.Rendering;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// A scene's link to Graphite must follow the GameObject lifecycle: a renderer joins its scene's
/// rendering when enabled and leaves when disabled, removed or destroyed. Anything left behind keeps
/// its handle in the RenderWorld and draws forever.
/// </summary>
public class SceneRenderingTests : RuntimeTestBase
{
    private sealed class TrackingSync : Component, IRenderSync
    {
        public int SyncCount;
        public int DetachCount;
        public bool Sync(RenderWorld world) { SyncCount++; return false; }
        public void Detach(RenderWorld world) { DetachCount++; }
    }

    [Fact]
    public void Enable_Adds_And_Destroy_Removes()
    {
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        scene.Add(go);

        TrackingSync sync = go.AddComponent<TrackingSync>();
        Assert.True(scene.Rendering.IsMember(sync));

        go.Destroy();
        EngineObject.ProcessDestroyed();

        Assert.True(go.IsDisposed);
        Assert.False(scene.Rendering.IsMember(sync));
    }

    [Fact]
    public void Disable_Removes_And_Reenable_Readds()
    {
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        scene.Add(go);

        TrackingSync sync = go.AddComponent<TrackingSync>();
        Assert.True(scene.Rendering.IsMember(sync));

        go.Enabled = false;
        Assert.False(scene.Rendering.IsMember(sync));

        go.Enabled = true;
        Assert.True(scene.Rendering.IsMember(sync));
    }

    [Fact]
    public void RemoveComponent_Removes()
    {
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        scene.Add(go);

        TrackingSync sync = go.AddComponent<TrackingSync>();
        Assert.True(scene.Rendering.IsMember(sync));

        go.RemoveComponent(sync);
        EngineObject.ProcessDestroyed();

        Assert.False(scene.Rendering.IsMember(sync));
    }

    [Fact]
    public void Scene_Remove_Removes()
    {
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        scene.Add(go);

        TrackingSync sync = go.AddComponent<TrackingSync>();
        Assert.True(scene.Rendering.IsMember(sync));

        scene.Remove(go);
        Assert.False(scene.Rendering.IsMember(sync));
    }

    // SpriteRenderer.OnDispose used to skip its base call, so destroying the component itself (rather
    // than its GameObject) left it joined to the scene's rendering with a live world handle.
    [Fact]
    public void SpriteRenderer_ComponentDestroy_Removes()
    {
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        scene.Add(go);

        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        Assert.True(scene.Rendering.IsMember(renderer));

        renderer.Destroy();
        EngineObject.ProcessDestroyed();

        Assert.True(renderer.IsDisposed);
        Assert.False(scene.Rendering.IsMember(renderer));
    }

    [Fact]
    public void MeshRenderer_GameObjectDestroy_Removes()
    {
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        scene.Add(go);

        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        Assert.True(scene.Rendering.IsMember(renderer));

        go.Destroy();
        EngineObject.ProcessDestroyed();

        Assert.True(go.IsDisposed);
        Assert.False(scene.Rendering.IsMember(renderer));
    }
}
