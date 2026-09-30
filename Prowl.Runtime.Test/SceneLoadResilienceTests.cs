// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public sealed class ResilienceMarker : MonoBehaviour
{
    public int Value = 1;
    [SerializeIgnore] public bool AddedToScene;
    public override void OnAddedToScene() => AddedToScene = true;
}

public sealed class ResilienceLinker : MonoBehaviour
{
    public GameObject? Target;
    public MonoBehaviour? Other;
    public ResilienceOwned? Owned;
    public float Value;
}

public sealed class ResilienceOwned
{
    public string Text = "";
    public ResilienceOwned? Self;
}

public sealed class ResiliencePointer : MonoBehaviour
{
    public MonoBehaviour? Other;
}

public sealed class ResilienceEmpty : MonoBehaviour { }

public sealed class ResilienceThrowsOnSave : MonoBehaviour
{
    public override void OnBeforeSerialize() => throw new InvalidOperationException("save bug");
}

public sealed class ResilienceThrowsOnLoad : MonoBehaviour
{
    public override void OnAfterDeserialize() => throw new InvalidOperationException("load bug");
}

/// <summary>
/// A broken script, a missing script or a damaged scene file must only cost the part that is broken.
/// </summary>
public class SceneLoadResilienceTests : RuntimeTestBase
{
    private const string Ghost = "Ghost_DoesNotExist";

    private static string Save(Scene scene) => Serializer.Serialize(typeof(object), scene).WriteToString();

    private static Scene Load(string text) => Serializer.Deserialize<Scene>(EchoObject.ReadFromString(text), new SerializationContext())!;

    private static GameObject Find(Scene scene, string name) => scene.AllObjects.Single(g => g.Name == name);

    [Fact]
    public void ThrowingOnBeforeSerialize_KeepsEveryObject()
    {
        var scene = CreateScene();
        for (int i = 0; i < 5; i++)
            scene.Add(CreateGameObject("GO" + i));
        Find(scene, "GO3").AddComponent<ResilienceThrowsOnSave>();

        var loaded = Load(Save(scene));

        Assert.Equal(5, loaded.AllObjects.Count());
    }

    [Fact]
    public void ThrowerReachedThroughAReference_KeepsTheObjectItsChildrenAndTheReference()
    {
        var scene = CreateScene();
        var a = CreateGameObject("A");
        var b = CreateGameObject("B");
        var kid = CreateGameObject("Kid");
        kid.SetParent(b);
        a.AddComponent<ResilienceLinker>().Target = b;
        b.AddComponent<ResilienceThrowsOnSave>();
        scene.Add(a);
        scene.Add(b);

        var loaded = Load(Save(scene));

        var loadedB = Find(loaded, "B");
        Assert.Same(loadedB, Find(loaded, "A").GetComponent<ResilienceLinker>()!.Target);
        Assert.Same(loadedB, Find(loaded, "Kid").Parent);
    }

    [Fact]
    public void RecoveredScriptThrowingOnAfterDeserialize_KeepsTheHierarchy()
    {
        var scene = CreateScene();
        var root = CreateGameObject("Root");
        var mid = CreateGameObject("Mid");
        var leaf = CreateGameObject("Leaf");
        mid.SetParent(root);
        leaf.SetParent(mid);
        leaf.AddComponent<ResilienceThrowsOnLoad>();
        scene.Add(root);

        string savedWhileMissing = Save(Load(Save(scene).Replace(nameof(ResilienceThrowsOnLoad), Ghost)));
        var loaded = Load(savedWhileMissing.Replace(Ghost, nameof(ResilienceThrowsOnLoad)));

        Assert.Same(Find(loaded, "Root"), Find(loaded, "Mid").Parent);
        Assert.Same(Find(loaded, "Mid"), Find(loaded, "Leaf").Parent);
    }

    [Fact]
    public void MissingScript_SurvivesSavesWhileMissing_AndKeepsItsDataWhenBack()
    {
        var scene = CreateScene();
        var a = CreateGameObject("A");
        a.AddComponent<ResilienceLinker>().Value = 42;
        var marker = a.AddComponent<ResilienceMarker>();
        scene.Add(a);

        string whileMissing = Save(scene).Replace(nameof(ResilienceLinker), Ghost);
        string savedTwice = Save(Load(Save(Load(whileMissing))));
        var stillMissing = Load(savedTwice);
        var restored = Load(Save(stillMissing).Replace(Ghost, nameof(ResilienceLinker)));

        Assert.IsType<MissingMonobehaviour>(Find(stillMissing, "A").GetComponents<MonoBehaviour>().First());
        Assert.Equal(marker.Identifier, Find(stillMissing, "A").GetComponent<ResilienceMarker>()!.Identifier);
        Assert.Equal(42, Find(restored, "A").GetComponent<ResilienceLinker>()!.Value);
    }

    [Fact]
    public void MissingScriptHoldingAnotherObjectsDefinition_KeepsItsIdentifiersAndAddsItToTheScene()
    {
        var scene = CreateScene();
        var a = CreateGameObject("A");
        var b = CreateGameObject("B");
        var marker = b.AddComponent<ResilienceMarker>();
        a.AddComponent<ResilienceLinker>().Target = b;
        scene.Add(a);
        scene.Add(b);

        var loaded = Load(Save(scene).Replace(nameof(ResilienceLinker), Ghost));

        var loadedB = Find(loaded, "B");
        var loadedMarker = loadedB.GetComponent<ResilienceMarker>()!;
        Assert.Equal(b.Identifier, loadedB.Identifier);
        Assert.Equal(marker.Identifier, loadedMarker.Identifier);
        Assert.True(loadedMarker.AddedToScene);
    }

    private static Scene MissingForTwoSaves(Scene scene)
    {
        string whileMissing = Save(scene).Replace(nameof(ResilienceLinker), Ghost);
        string savedTwice = Save(Load(Save(Load(whileMissing))));
        return Load(savedTwice.Replace(Ghost, nameof(ResilienceLinker)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RecoveredScript_KeepsItsReferencesToTheScene(bool missingScriptWrittenFirst)
    {
        var scene = CreateScene();
        var a = CreateGameObject("A");
        var b = CreateGameObject("B");
        var kid = CreateGameObject("Kid");
        kid.SetParent(b);
        var marker = b.AddComponent<ResilienceMarker>();
        var owned = new ResilienceOwned { Text = "owned" };
        owned.Self = owned;
        var linker = a.AddComponent<ResilienceLinker>();
        linker.Target = kid;
        linker.Other = marker;
        linker.Owned = owned;
        linker.Value = 7;
        var c = CreateGameObject("C");
        c.AddComponent<ResiliencePointer>().Other = linker;
        foreach (var go in missingScriptWrittenFirst ? new[] { a, b, c } : new[] { c, b, a })
            scene.Add(go);

        var loaded = MissingForTwoSaves(scene);

        var loadedLinker = Find(loaded, "A").GetComponent<ResilienceLinker>()!;
        Assert.Equal(7, loadedLinker.Value);
        Assert.Same(Find(loaded, "Kid"), loadedLinker.Target);
        Assert.Same(Find(loaded, "B").GetComponent<ResilienceMarker>(), loadedLinker.Other);
        Assert.Equal("owned", loadedLinker.Owned!.Text);
        Assert.Same(loadedLinker.Owned, loadedLinker.Owned.Self);
        // A live field cannot hold a type that does not exist, so the reference only survives when it was not the definition.
        if (missingScriptWrittenFirst)
            Assert.Same(loadedLinker, Find(loaded, "C").GetComponent<ResiliencePointer>()!.Other);
        Assert.Equal(linker.Identifier, loadedLinker.Identifier);
        Assert.Single(Find(loaded, "B").Children);
    }

    [Fact]
    public void MissingScript_WritesEveryFieldAPlainComponentWrites()
    {
        var plain = CreateGameObject("Plain").AddComponent<ResilienceEmpty>();
        var missing = new MissingMonobehaviour { ComponentData = EchoObject.NewCompound() };

        var plainKeys = Serializer.Serialize(typeof(MonoBehaviour), plain).GetNames().Where(k => k != "$type");
        var missingKeys = Serializer.Serialize(typeof(MonoBehaviour), missing).GetNames();

        Assert.Empty(plainKeys.Except(missingKeys));
    }

    [Fact]
    public void DuplicatedObjectBlock_KeepsTheOtherIdentifiers()
    {
        var scene = CreateScene();
        var objects = Enumerable.Range(0, 3).Select(i => CreateGameObject("GO" + i)).ToList();
        objects.ForEach(scene.Add);

        var echo = Serializer.Serialize(typeof(object), scene);
        var array = echo["serializeObj"]["array"];
        array.ListAdd(EchoObject.ReadFromString(array[0].WriteToString()));
        var loaded = Load(echo.WriteToString());

        Assert.All(objects, o => Assert.Equal(o.Identifier, Find(loaded, o.Name).Identifier));
    }
}
