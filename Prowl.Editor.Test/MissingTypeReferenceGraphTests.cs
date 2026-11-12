// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;
using Xunit.Abstractions;

namespace Prowl.Editor.Test;

// A component that references another component and a GameObject. When this is the FIRST thing traversed
// during serialization, Echo emits the FULL definitions of its targets inline in this component's data.
public sealed class MissRefComp : Component
{
    public Component? Target;
    public GameObject? TargetGO;
}

// A component that holds a GUID-based AssetRef (like MeshRenderer.Mesh).
public sealed class AssetHolderComp : Component
{
    public AssetRef<Texture2D> Tex;
}

/// <summary>
/// Reproduction probes for "loading a scene while a script type is missing corrupts unrelated data":
/// a GUID AssetRef on a different component going empty, and the GameObject hierarchy changing. These
/// isolate the Echo single-pass reference graph from the prefab/asset-db machinery.
/// </summary>
public class MissingTypeReferenceGraphTests
{
    private readonly ITestOutputHelper _out;
    public MissingTypeReferenceGraphTests(ITestOutputHelper o) => _out = o;

    private static GameObject RoundTripWithMissing(GameObject root, string missTypeToken)
    {
        string text = Serializer.Serialize(typeof(object), root).WriteToString();
        // Simulate the type no longer existing (compile error / renamed script).
        text = text.Replace(missTypeToken, "GhostComp_DoesNotExist");
        EchoObject echo = EchoObject.ReadFromString(text);
        return Serializer.Deserialize<GameObject>(echo)!;
    }

    [Fact]
    public void ControlNoCrossRef_AssetRefSurvives()
    {
        // Missing component does NOT reference the holder, and is added AFTER it.
        var guid = Guid.NewGuid();
        var root = new GameObject("Root");
        AssetHolderComp holder = root.AddComponent<AssetHolderComp>();
        holder.Tex = AssetDatabase.Get<Texture2D>(guid);
        root.AddComponent<MissRefComp>(); // no targets

        GameObject loaded = RoundTripWithMissing(root, nameof(MissRefComp));

        AssetHolderComp? h = loaded.GetComponent<AssetHolderComp>();
        _out.WriteLine($"[control] holder present={h is not null}, assetId={h?.Tex.AssetID}");
        Assert.NotNull(h);
        Assert.Equal(guid, h!.Tex.AssetID); // GUID AssetRef survives when nothing points into the missing comp
    }

    [Fact]
    public void CrossRefFromMissing_AssetRefOnOtherComponentIsLost()
    {
        // Missing component is FIRST and references the holder, so the holder's full definition (incl. its
        // AssetRef GUID) is emitted INLINE inside the missing component's data.
        var guid = Guid.NewGuid();
        var root = new GameObject("Root");
        MissRefComp misser = root.AddComponent<MissRefComp>();     // added FIRST
        AssetHolderComp holder = root.AddComponent<AssetHolderComp>();
        holder.Tex = AssetDatabase.Get<Texture2D>(guid);
        misser.Target = holder;                            // holder defined inline here

        GameObject loaded = RoundTripWithMissing(root, nameof(MissRefComp));

        AssetHolderComp? h = loaded.GetComponent<AssetHolderComp>();
        _out.WriteLine($"[crossref] holder present={h is not null}, assetId={h?.Tex.AssetID}, expected={guid}");
        Assert.NotNull(h);
        Assert.Equal(guid, h!.Tex.AssetID); // EXPECTED to pass; if it fails, the AssetRef was lost -> bug reproduced
    }

    [Fact]
    public void CrossRefFromMissing_SceneHierarchyPreserved()
    {
        // A realistic scene: GameObjects live in the Scene's flat array (their full definitions), while a
        // missing component that references one of them only holds a $id stub. The hierarchy must survive.
        var root = new GameObject("Root");
        var child = new GameObject("Child");
        child.SetParent(root);
        var grandchild = new GameObject("Grandchild");
        grandchild.SetParent(child);

        MissRefComp misser = root.AddComponent<MissRefComp>();
        misser.TargetGO = grandchild;

        var scene = new SceneInstance(SceneMode.Edit);
        scene.Add(root);

        string text = scene.ActiveSection.CaptureTree().WriteToString();
        text = text.Replace(nameof(MissRefComp), "GhostComp_DoesNotExist");
        SceneInstance loaded = SceneInstance.Instantiate(SceneContent.Read(EchoObject.ReadFromString(text)), SceneMode.Edit);

        GameObject? loadedRoot = loaded.Objects.FirstOrDefault(o => o.Name == "Root");
        string Structure(GameObject g, int d = 0) =>
            new string(' ', d * 2) + g.Name + "\n" + string.Concat(g.Children.Select(c => Structure(c, d + 1)));
        _out.WriteLine("[scene]\n" + (loadedRoot is null ? "<no Root>" : Structure(loadedRoot)));

        // Full recovery: the deferred two-phase back-patch populates the placeholder whose body was trapped
        // inside the missing component, so the whole hierarchy - names and all - survives.
        Assert.NotNull(loadedRoot);
        Assert.Single(loadedRoot!.Children);
        Assert.Equal("Child", loadedRoot.Children[0].Name);
        Assert.Single(loadedRoot.Children[0].Children);
        Assert.Equal("Grandchild", loadedRoot.Children[0].Children[0].Name);
    }
}
