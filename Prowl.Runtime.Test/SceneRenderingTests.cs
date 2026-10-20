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

    // A renderer synced and left alone, as a play mode sync that found it wholly in the world leaves it
    private static MeshRenderer Settled(Scene scene, GameObject go, Mesh? mesh = null)
    {
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.Mesh = mesh;
        scene.Rendering.Settle(renderer);
        Assert.True(scene.Rendering.IsParked(renderer));
        return renderer;
    }

    [Fact]
    public void ParkedRenderers_WakeWhenTheirTransformMoves()
    {
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        scene.Add(go);
        MeshRenderer renderer = Settled(scene, go);

        go.Transform.Position = new Prowl.Vector.Float3(1, 2, 3);
        Assert.False(scene.Rendering.IsParked(renderer));
    }

    [Fact]
    public void ParkedRenderers_WakeWhenAnAncestorMoves()
    {
        Scene scene = CreateScene(enable: true);
        GameObject parent = CreateGameObject();
        scene.Add(parent);
        GameObject child = CreateGameObject();
        child.SetParent(parent);
        GameObject grandchild = CreateGameObject();
        grandchild.SetParent(child);
        MeshRenderer renderer = Settled(scene, grandchild);

        parent.Transform.Position = new Prowl.Vector.Float3(0, 5, 0);
        Assert.False(scene.Rendering.IsParked(renderer));
    }

    [Fact]
    public void ParkedRenderers_WakeOnTheirSettings_AndTheirObjectsLayer()
    {
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        scene.Add(go);
        MeshRenderer renderer = Settled(scene, go);

        renderer.ShadowCasting = ShadowCasting.Off;
        Assert.False(scene.Rendering.IsParked(renderer));

        scene.Rendering.Settle(renderer);
        go.LayerIndex = 3;
        Assert.False(scene.Rendering.IsParked(renderer));
    }

    [Fact]
    public void ParkedRenderers_WakeWhenTheMeshTheyDrawIsEdited()
    {
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        scene.Add(go);
        Mesh mesh = new() { Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)] };
        mesh.Upload();
        Mesh other = new() { Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)] };
        other.Upload();
        scene.Rendering.WakeChanged();
        MeshRenderer renderer = Settled(scene, go, mesh);

        other.Vertices = [new(0, 0, 0), new(2, 0, 0), new(0, 2, 0)];
        scene.Rendering.WakeChanged();
        Assert.True(scene.Rendering.IsParked(renderer));

        mesh.Vertices = [new(0, 0, 0), new(2, 0, 0), new(0, 2, 0)];
        scene.Rendering.WakeChanged();
        Assert.False(scene.Rendering.IsParked(renderer));
    }
}
