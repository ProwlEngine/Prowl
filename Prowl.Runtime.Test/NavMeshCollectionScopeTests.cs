// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Navigation;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Exercises <see cref="NavMeshCollectObjects"/> and <see cref="NavMeshGeometrySource"/> - the scoping a
/// <see cref="NavMeshSurface"/> applies to which GameObjects (and which of their own geometry sources)
/// actually reach a bake, on top of the plain <see cref="NavMeshSurface.LayerMask"/> filter every
/// collection already applied. Checked directly against <see cref="NavMeshBuildInput.Triangles"/> - how
/// many triangles a scope let through - rather than a full bake, since that is exactly what these fields
/// change and nothing else about the pipeline downstream of collection needs re-proving here.
/// </summary>
public class NavMeshCollectionScopeTests : RuntimeTestBase
{
    private static Mesh CreateQuad(float size = 4f)
    {
        float h = size * 0.5f;
        return new Mesh
        {
            Vertices = [new(-h, 0, -h), new(h, 0, -h), new(h, 0, h), new(-h, 0, h)],
            Indices = [0, 2, 1, 0, 3, 2],
        };
    }

    private GameObject AddQuadRenderer(Scene scene, string name, Float3 position)
    {
        GameObject go = CreateGameObject(name);
        go.Transform.Position = position;
        go.AddComponent<MeshRenderer>().Mesh = CreateQuad();
        scene.Add(go);
        return go;
    }

    [Fact]
    public void ChildrenScope_OnlyCollectsGeometryUnderTheSurfacesOwnHierarchy()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        surface.CollectObjects = NavMeshCollectObjects.Children;
        scene.Add(surface.GameObject);

        GameObject child = AddQuadRenderer(scene, "Child", new Float3(0, 0, 0));
        child.Transform.SetParent(surface.Transform);

        AddQuadRenderer(scene, "Unrelated", new Float3(50, 0, 50)); // sibling, not under the surface

        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(scene, surface);

        Assert.Equal(6, input.Triangles.Length); // one quad's worth (2 triangles * 3 verts) - the child only
    }

    [Fact]
    public void VolumeScope_OnlyCollectsGeometryOverlappingTheDeclaredBox()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        surface.CollectObjects = NavMeshCollectObjects.Volume;
        surface.Center = Float3.Zero;
        surface.Size = new Float3(10f, 10f, 10f);
        scene.Add(surface.GameObject);

        AddQuadRenderer(scene, "Inside", Float3.Zero);
        AddQuadRenderer(scene, "Outside", new Float3(100, 0, 100)); // well outside the declared volume

        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(scene, surface);

        Assert.Equal(6, input.Triangles.Length); // the inside quad only
    }

    [Fact]
    public void UseGeometry_RenderMeshesOnly_IgnoresColliders()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        surface.UseGeometry = NavMeshGeometrySource.RenderMeshes;
        scene.Add(surface.GameObject);

        AddQuadRenderer(scene, "Rendered", Float3.Zero);

        GameObject colliderOnly = CreateGameObject("ColliderOnly");
        colliderOnly.Transform.Position = new Float3(20, 0, 0);
        colliderOnly.AddComponent<BoxCollider>().Size = new Float3(4, 1, 4);
        scene.Add(colliderOnly);

        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(scene, surface);

        Assert.Equal(6, input.Triangles.Length); // the rendered quad only - the box collider is ignored
    }

    [Fact]
    public void UseGeometry_PhysicsCollidersOnly_IgnoresRenderMeshes()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        surface.UseGeometry = NavMeshGeometrySource.PhysicsColliders;
        scene.Add(surface.GameObject);

        AddQuadRenderer(scene, "Rendered", Float3.Zero);

        GameObject colliderOnly = CreateGameObject("ColliderOnly");
        colliderOnly.Transform.Position = new Float3(20, 0, 0);
        colliderOnly.AddComponent<BoxCollider>().Size = new Float3(4, 1, 4);
        scene.Add(colliderOnly);

        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(scene, surface);

        Assert.Equal(36, input.Triangles.Length); // the box collider only (12 triangles * 3 verts) - the rendered quad is ignored
    }

    [Fact]
    public void LegacyOverloads_StillCollectBothGeometrySourcesFromTheWholeScene()
    {
        // The two/three-argument overloads (and any surface with default field values) must reproduce
        // the collector's original, unscoped behavior exactly - see NavMeshCollectionScope's own doc
        // comment. This is the regression guard for that promise.
        Scene scene = CreateScene(enable: true);

        AddQuadRenderer(scene, "Rendered", Float3.Zero);

        GameObject colliderOnly = CreateGameObject("ColliderOnly");
        colliderOnly.Transform.Position = new Float3(20, 0, 0);
        colliderOnly.AddComponent<BoxCollider>().Size = new Float3(4, 1, 4);
        scene.Add(colliderOnly);

        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(scene, LayerMask.Everything);

        Assert.Equal(6 + 36, input.Triangles.Length); // both, unconditionally
    }
}
