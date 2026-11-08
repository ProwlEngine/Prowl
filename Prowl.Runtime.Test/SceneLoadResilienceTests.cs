// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public sealed class ResilienceMarker : Component
{
    public int Value = 1;
    [SerializeIgnore] public bool AddedToScene;
    public override void OnAddedToScene() => AddedToScene = true;
}

public sealed class ResilienceLinker : Component
{
    public GameObject? Target;
    public Component? Other;
    public ResilienceOwned? Owned;
    public float Value;
}

public sealed class ResilienceOwned
{
    public string Text = "";
    public ResilienceOwned? Self;
}

public sealed class ResiliencePointer : Component
{
    public Component? Other;
}

public sealed class ResilienceEmpty : Component { }

public sealed class ResilienceThrowsOnSave : Component
{
    public override void OnBeforeSerialize() => throw new InvalidOperationException("save bug");
}

public sealed class ResilienceThrowsOnLoad : Component
{
    public override void OnAfterDeserialize() => throw new InvalidOperationException("load bug");
}

/// <summary>
/// A broken script, a missing script or a damaged scene file must only cost the part that is broken.
/// </summary>
public class SceneLoadResilienceTests : RuntimeTestBase
{
    private const string Ghost = "Ghost_DoesNotExist";

    private static string Save(SceneInstance scene) => scene.ActiveSection.CaptureTree().WriteToString();

    private static SceneInstance Load(string text) => SceneInstance.Instantiate(SceneContent.Read(EchoObject.ReadFromString(text), new SerializationContext()));

    private static GameObject Find(SceneInstance scene, string name) => scene.Objects.Single(g => g.Name == name);

    [Fact]
    public void ThrowingOnBeforeSerialize_KeepsEveryObject()
    {
        SceneInstance scene = CreateScene();
        for (int i = 0; i < 5; i++)
            scene.Add(CreateGameObject("GO" + i));
        Find(scene, "GO3").AddComponent<ResilienceThrowsOnSave>();

        SceneInstance loaded = Load(Save(scene));

        Assert.Equal(5, loaded.Objects.Count());
    }

    [Fact]
    public void ThrowerReachedThroughAReference_KeepsTheObjectItsChildrenAndTheReference()
    {
        SceneInstance scene = CreateScene();
        GameObject a = CreateGameObject("A");
        GameObject b = CreateGameObject("B");
        GameObject kid = CreateGameObject("Kid");
        kid.SetParent(b);
        a.AddComponent<ResilienceLinker>().Target = b;
        b.AddComponent<ResilienceThrowsOnSave>();
        scene.Add(a);
        scene.Add(b);

        SceneInstance loaded = Load(Save(scene));

        GameObject loadedB = Find(loaded, "B");
        Assert.Same(loadedB, Find(loaded, "A").GetComponent<ResilienceLinker>()!.Target);
        Assert.Same(loadedB, Find(loaded, "Kid").Parent);
    }

    [Fact]
    public void RecoveredScriptThrowingOnAfterDeserialize_KeepsTheHierarchy()
    {
        SceneInstance scene = CreateScene();
        GameObject root = CreateGameObject("Root");
        GameObject mid = CreateGameObject("Mid");
        GameObject leaf = CreateGameObject("Leaf");
        mid.SetParent(root);
        leaf.SetParent(mid);
        leaf.AddComponent<ResilienceThrowsOnLoad>();
        scene.Add(root);

        string savedWhileMissing = Save(Load(Save(scene).Replace(nameof(ResilienceThrowsOnLoad), Ghost)));
        SceneInstance loaded = Load(savedWhileMissing.Replace(Ghost, nameof(ResilienceThrowsOnLoad)));

        Assert.Same(Find(loaded, "Root"), Find(loaded, "Mid").Parent);
        Assert.Same(Find(loaded, "Mid"), Find(loaded, "Leaf").Parent);
    }

    [Fact]
    public void MissingScript_SurvivesSavesWhileMissing_AndKeepsItsDataWhenBack()
    {
        SceneInstance scene = CreateScene();
        GameObject a = CreateGameObject("A");
        a.AddComponent<ResilienceLinker>().Value = 42;
        ResilienceMarker marker = a.AddComponent<ResilienceMarker>();
        scene.Add(a);

        string whileMissing = Save(scene).Replace(nameof(ResilienceLinker), Ghost);
        string savedTwice = Save(Load(Save(Load(whileMissing))));
        SceneInstance stillMissing = Load(savedTwice);
        SceneInstance restored = Load(Save(stillMissing).Replace(Ghost, nameof(ResilienceLinker)));

        Assert.IsType<MissingComponent>(Find(stillMissing, "A").GetComponents<Component>().First());
        Assert.Equal(marker.Identifier, Find(stillMissing, "A").GetComponent<ResilienceMarker>()!.Identifier);
        Assert.Equal(42, Find(restored, "A").GetComponent<ResilienceLinker>()!.Value);
    }

    [Fact]
    public void MissingScriptHoldingAnotherObjectsDefinition_KeepsItsIdentifiersAndAddsItToTheScene()
    {
        SceneInstance scene = CreateScene();
        GameObject a = CreateGameObject("A");
        GameObject b = CreateGameObject("B");
        ResilienceMarker marker = b.AddComponent<ResilienceMarker>();
        a.AddComponent<ResilienceLinker>().Target = b;
        scene.Add(a);
        scene.Add(b);

        SceneInstance loaded = Load(Save(scene).Replace(nameof(ResilienceLinker), Ghost));

        GameObject loadedB = Find(loaded, "B");
        ResilienceMarker loadedMarker = loadedB.GetComponent<ResilienceMarker>()!;
        Assert.Equal(b.Identifier, loadedB.Identifier);
        Assert.Equal(marker.Identifier, loadedMarker.Identifier);
        Assert.True(loadedMarker.AddedToScene);
    }

    private static SceneInstance MissingForTwoSaves(SceneInstance scene)
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
        SceneInstance scene = CreateScene();
        GameObject a = CreateGameObject("A");
        GameObject b = CreateGameObject("B");
        GameObject kid = CreateGameObject("Kid");
        kid.SetParent(b);
        ResilienceMarker marker = b.AddComponent<ResilienceMarker>();
        var owned = new ResilienceOwned { Text = "owned" };
        owned.Self = owned;
        ResilienceLinker linker = a.AddComponent<ResilienceLinker>();
        linker.Target = kid;
        linker.Other = marker;
        linker.Owned = owned;
        linker.Value = 7;
        GameObject c = CreateGameObject("C");
        c.AddComponent<ResiliencePointer>().Other = linker;
        foreach (GameObject? go in missingScriptWrittenFirst ? new[] { a, b, c } : [c, b, a])
            scene.Add(go);

        SceneInstance loaded = MissingForTwoSaves(scene);

        ResilienceLinker loadedLinker = Find(loaded, "A").GetComponent<ResilienceLinker>()!;
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
        ResilienceEmpty plain = CreateGameObject("Plain").AddComponent<ResilienceEmpty>();
        var missing = new MissingComponent { ComponentData = EchoObject.NewCompound() };

        IEnumerable<string> plainKeys = Serializer.Serialize(typeof(Component), plain).GetNames().Where(k => k != "$type");
        IEnumerable<string> missingKeys = Serializer.Serialize(typeof(Component), missing).GetNames();

        Assert.Empty(plainKeys.Except(missingKeys));
    }

    [Fact]
    public void DuplicatedObjectBlock_KeepsTheOtherIdentifiers()
    {
        SceneInstance scene = CreateScene();
        var objects = Enumerable.Range(0, 3).Select(i => CreateGameObject("GO" + i)).ToList();
        objects.ForEach(go => scene.Add(go));

        EchoObject echo = scene.ActiveSection.CaptureTree();
        EchoObject array = StoredObjects(echo);
        array.ListAdd(EchoObject.ReadFromString(array[0].WriteToString()));
        SceneInstance loaded = Load(echo.WriteToString());

        Assert.All(objects, o => Assert.Equal(o.Identifier, Find(loaded, o.Name).Identifier));
    }
}
