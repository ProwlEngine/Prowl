// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;

using Prowl.Echo;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public sealed class CollisionProbeComponent : MonoBehaviour
{
    public int Marker;
}

// A component whose type name resolves to a non-MonoBehaviour (a user script "World" binding to
// Jitter2.World) used to throw out of the flat GameObject array and wipe the whole scene to zero objects.
public class TypeCollisionSceneLoadTests : RuntimeTestBase
{
    private static string JitterWorldName => typeof(Jitter2.World).AssemblyQualifiedName!;

    [Fact]
    public void FindType_HonorsRecordedAssembly_ForCollidingSimpleName()
    {
        Assert.Equal(typeof(Jitter2.World), RuntimeUtils.FindType("World, Jitter2"));
    }

    [Fact]
    public void Scene_WithComponentTypeResolvingToNonMonoBehaviour_StillLoadsEveryObject()
    {
        var scene = CreateScene();

        var withGoodComp = CreateGameObject("Healthy");
        withGoodComp.AddComponent<CollisionProbeComponent>().Marker = 7;
        scene.Add(withGoodComp);

        var willGetBadComp = CreateGameObject("HasBadComponent");
        scene.Add(willGetBadComp);

        var alsoHealthy = CreateGameObject("AlsoHealthy");
        scene.Add(alsoHealthy);

        var echo = Serializer.Serialize(scene);

        // Give one object a component whose $type resolves to a non-MonoBehaviour (Jitter2.World).
        InjectBadComponent(echo, "HasBadComponent", JitterWorldName);

        var clone = Serializer.Deserialize<Scene>(echo);

        Assert.NotNull(clone);
        var objs = clone.AllObjects.ToList();
        Assert.Equal(3, objs.Count);
        Assert.Contains(objs, g => g.Name == "Healthy");
        Assert.Contains(objs, g => g.Name == "HasBadComponent");
        Assert.Contains(objs, g => g.Name == "AlsoHealthy");

        // The healthy component still deserializes with its data intact.
        var healthy = objs.First(g => g.Name == "Healthy");
        Assert.Equal(7, healthy.GetComponent<CollisionProbeComponent>()!.Marker);

        // The bad component is kept as a MissingMonobehaviour so its data survives a re-save.
        var bad = objs.First(g => g.Name == "HasBadComponent");
        Assert.Contains(bad.GetComponents<MonoBehaviour>(), c => c is MissingMonobehaviour);
    }

    private static void InjectBadComponent(EchoObject sceneEcho, string goName, string typeName)
    {
        foreach (EchoObject go in sceneEcho["serializeObj"]["array"].List)
        {
            if (!go.TryGet("Name", out var n) || n.StringValue != goName)
                continue;

            var badComp = EchoObject.NewCompound();
            badComp.Add("$type", new EchoObject(typeName));
            go["Components"].ListAdd(badComp);
            return;
        }

        Assert.Fail($"GameObject '{goName}' not found in serialized scene");
    }
}
