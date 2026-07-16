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
public sealed class MissRefComp : MonoBehaviour
{
    public MonoBehaviour? Target;
    public GameObject? TargetGO;
}

// A component that holds a GUID-based AssetRef (like MeshRenderer.Mesh).
public sealed class AssetHolderComp : MonoBehaviour
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
    public void CallerOwnedContext_EditorPath_StillRecovers()
    {
        // The editor deserializes scenes with its own SerializationContext via the 3-arg overload (for
        // asset-dependency tracking). Deferred back-patches must still fire on that path, not only on the
        // context-creating overload.
        var root = new GameObject("Root");
        var child = new GameObject("Child");
        child.SetParent(root);
        var misser = root.AddComponent<MissRefComp>();
        misser.TargetGO = child;

        var scene = new Scene();
        scene.Add(root);

        string text = Serializer.Serialize(typeof(object), scene).WriteToString()
            .Replace(nameof(MissRefComp), "GhostComp_DoesNotExist");

        // Mimic the editor: a caller-supplied context passed to the 3-arg Deserialize.
        var loaded = Serializer.Deserialize<Scene>(EchoObject.ReadFromString(text), new SerializationContext())!;

        Assert.Empty(loaded.AllObjects.Where(o => o.Name == "New GameObject")); // no phantom placeholders
        var loadedRoot = loaded.AllObjects.FirstOrDefault(o => o.Name == "Root");
        Assert.NotNull(loadedRoot);
        Assert.Single(loadedRoot!.Children);
        Assert.Equal("Child", loadedRoot.Children[0].Name);
    }

    [Fact]
    public void ControlNoCrossRef_AssetRefSurvives()
    {
        // Missing component does NOT reference the holder, and is added AFTER it.
        var guid = Guid.NewGuid();
        var root = new GameObject("Root");
        var holder = root.AddComponent<AssetHolderComp>();
        holder.Tex = new AssetRef<Texture2D>(guid);
        root.AddComponent<MissRefComp>(); // no targets

        var loaded = RoundTripWithMissing(root, nameof(MissRefComp));

        var h = loaded.GetComponent<AssetHolderComp>();
        _out.WriteLine($"[control] holder present={h != null}, assetId={h?.Tex.AssetID}");
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
        var misser = root.AddComponent<MissRefComp>();     // added FIRST
        var holder = root.AddComponent<AssetHolderComp>();
        holder.Tex = new AssetRef<Texture2D>(guid);
        misser.Target = holder;                            // holder defined inline here

        var loaded = RoundTripWithMissing(root, nameof(MissRefComp));

        var h = loaded.GetComponent<AssetHolderComp>();
        _out.WriteLine($"[crossref] holder present={h != null}, assetId={h?.Tex.AssetID}, expected={guid}");
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

        var misser = root.AddComponent<MissRefComp>();
        misser.TargetGO = grandchild;

        var scene = new Scene();
        scene.Add(root);

        string text = Serializer.Serialize(typeof(object), scene).WriteToString();
        text = text.Replace(nameof(MissRefComp), "GhostComp_DoesNotExist");
        var loaded = Serializer.Deserialize<Scene>(EchoObject.ReadFromString(text))!;

        var loadedRoot = loaded.AllObjects.FirstOrDefault(o => o.Name == "Root");
        string Structure(GameObject g, int d = 0) =>
            new string(' ', d * 2) + g.Name + "\n" + string.Concat(g.Children.Select(c => Structure(c, d + 1)));
        _out.WriteLine("[scene]\n" + (loadedRoot == null ? "<no Root>" : Structure(loadedRoot)));

        // Full recovery: the deferred two-phase back-patch populates the placeholder whose body was trapped
        // inside the missing component, so the whole hierarchy - names and all - survives.
        Assert.NotNull(loadedRoot);
        Assert.Single(loadedRoot!.Children);
        Assert.Equal("Child", loadedRoot.Children[0].Name);
        Assert.Single(loadedRoot.Children[0].Children);
        Assert.Equal("Grandchild", loadedRoot.Children[0].Children[0].Name);
    }
}
