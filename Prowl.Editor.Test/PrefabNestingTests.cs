// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Editor.Prefabs;
using Prowl.Editor.Importers;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Prefabs do not nest. A prefab asset is one self-contained tree, so a prefab instance placed inside
/// something that becomes a prefab has its link broken and its objects become content of that prefab.
/// <para/>
/// This is a decision rather than a missing feature. Inlining is what lets a reference across the
/// would-be boundary resolve at all, and a linked-but-embedded copy went stale against the prefab it
/// embedded. See Design/PrefabAudit.md.
/// <para/>
/// These use the real <see cref="PrefabUtility.CreatePrefab"/> rather than the harness shortcut,
/// which clears prefab data across the whole tree and so could not produce a nested instance at all.
/// </summary>
public class PrefabNestingTests : EditorTestHarness
{
    private GameObject Inst(Guid guid) => GameObject.InstantiateDetached(GetPrefab(guid)!)!;

    private Scene LoadSceneWith(params GameObject[] objects)
    {
        var scene = new Scene();
        foreach (var o in objects) scene.Add(o);
        Scene.Load(scene);
        Scene.ProcessPendingLoad();
        return Scene.Current!;
    }

    private Guid Author(GameObject source, string relativePath)
    {
        LoadSceneWith(source);
        Assert.True(PrefabUtility.SaveAsPrefabAssetAndConnect(source, relativePath));
        Assets.Refresh();

        var entry = EditorAssetBackend.Instance!.GetEntry(relativePath);
        Assert.NotNull(entry);
        return entry!.Guid;
    }

    private Guid AuthorLeaf(string name, int value)
    {
        var root = new GameObject(name);
        root.AddComponent<OverrideComp>().A = value;
        return Author(root, name + ".prefab");
    }

    /// <summary>A prefab authored from a root that contained an instance of <paramref name="nested"/>.</summary>
    private Guid AuthorContaining(string name, Guid nested)
    {
        var root = new GameObject(name);
        root.AddComponent<OverrideComp>().A = 0;
        Inst(nested).SetParent(root);
        return Author(root, name + ".prefab");
    }

    private static DependencyGraph Graph => EditorAssetBackend.Instance!.Dependencies;

    #region Creating and applying flattens

    [Fact]
    public void CreatingAPrefabBreaksTheLinkOfAnyInstanceInside()
    {
        Guid inner = AuthorLeaf("Flat_Inner", 7);
        Guid outer = AuthorContaining("Flat_Outer", inner);

        GameObject instance = Inst(outer);

        // The content is there, as content.
        Assert.Single(instance.Children);
        Assert.Equal(7, instance.Children[0].GetComponent<OverrideComp>()!.A);

        // But it is not an instance of the inner prefab.
        Assert.Equal(outer, instance.Children[0].PrefabAssetId);
        Assert.NotEqual(inner, instance.Children[0].PrefabAssetId);
    }

    [Fact]
    public void AFlattenedChildIsOrdinaryPrefabContent()
    {
        Guid inner = AuthorLeaf("Content_Inner", 7);
        Guid outer = AuthorContaining("Content_Outer", inner);

        GameObject instance = Inst(outer);
        GameObject child = instance.Children[0];

        // It has to carry a source identity, or overrides on it could never resolve.
        Assert.NotEqual(Guid.Empty, child.SourceIdentifier);
        Assert.True(PrefabUtility.IsProvidedByPrefab(child));
    }

    [Fact]
    public void EditingTheOnceNestedPrefabNoLongerReachesTheOuter()
    {
        Guid inner = AuthorLeaf("Detach_Inner", 7);
        Guid outer = AuthorContaining("Detach_Outer", inner);

        GameObject instance = Inst(outer);
        LoadSceneWith(instance);

        EditPrefabSource(inner, "Detach_Inner.prefab", src => src.GetComponent<OverrideComp>()!.A = 99);
        PrefabUtility.RefreshAllInstances(inner);

        // No link, so no propagation, and no divergence between this and a freshly spawned one either.
        Assert.Equal(7, instance.Children[0].GetComponent<OverrideComp>()!.A);
        Assert.Equal(7, Inst(outer).Children[0].GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void ApplyingNeverWritesAPrefabInstanceIntoTheAsset()
    {
        Guid inner = AuthorLeaf("ApplyFlat_Inner", 7);
        Guid outer = AuthorLeaf("ApplyFlat_Outer", 1);

        GameObject instance = Inst(outer);
        LoadSceneWith(instance);

        // Placed on the instance, so it is that instance's own content and stays there. The asset
        // cannot gain a nested prefab this way, or any other.
        GameObject placed = Inst(inner);
        placed.SetParent(instance);
        PrefabUtility.ApplyOverrides(instance);

        Assert.Empty(Inst(outer).Children);
        Assert.Contains(instance.Children, c => ReferenceEquals(c, placed));
    }

    [Fact]
    public void ADeeplyPlacedInstanceIsAlsoFlattened()
    {
        Guid inner = AuthorLeaf("DeepFlat_Inner", 7);

        var root = new GameObject("DeepFlat_Outer");
        var middle = new GameObject("Middle");
        middle.SetParent(root);
        Inst(inner).SetParent(middle);
        Guid outer = Author(root, "DeepFlat_Outer.prefab");

        GameObject instance = Inst(outer);
        Assert.NotEqual(inner, instance.Children[0].Children[0].PrefabAssetId);
    }

    [Fact]
    public void NothingRecordsADependencyOnAFlattenedPrefab()
    {
        Guid inner = AuthorLeaf("NoDep_Inner", 7);
        Guid outer = AuthorContaining("NoDep_Outer", inner);

        // The content was copied in, so there is nothing left to depend on.
        Assert.DoesNotContain(inner, Graph.GetDependencies(outer));
    }

    #endregion

    #region References across what would have been a boundary

    [Fact]
    public void AReferenceOutOfTheFlattenedContentStillResolves()
    {
        var innerRoot = new GameObject("Ref_Inner");
        innerRoot.AddComponent<LinkComp>();
        Guid inner = Author(innerRoot, "Ref_Inner.prefab");

        var outerRoot = new GameObject("Ref_Outer");
        var marker = new GameObject("Marker");
        marker.SetParent(outerRoot);

        GameObject placed = Inst(inner);
        placed.SetParent(outerRoot);
        placed.GetComponent<LinkComp>()!.Target = marker;

        Guid outer = Author(outerRoot, "Ref_Outer.prefab");

        GameObject instance = Inst(outer);
        GameObject liveMarker = instance.Children.Single(c => c.Name == "Marker");
        LinkComp link = instance.Children.Single(c => c.Name != "Marker").GetComponent<LinkComp>()!;

        Assert.Same(liveMarker, link.Target);
    }

    [Fact]
    public void AReferenceIntoTheFlattenedContentStillResolves()
    {
        Guid inner = AuthorLeaf("In_Inner", 7);

        var outerRoot = new GameObject("In_Outer");
        var link = outerRoot.AddComponent<LinkComp>();
        GameObject placed = Inst(inner);
        placed.SetParent(outerRoot);
        link.Target = placed;

        Guid outer = Author(outerRoot, "In_Outer.prefab");

        GameObject instance = Inst(outer);
        Assert.Same(instance.Children[0], instance.GetComponent<LinkComp>()!.Target);
    }

    [Fact]
    public void ValuesChangedBeforeFlatteningAreKept()
    {
        Guid inner = AuthorLeaf("Val_Inner", 1);

        var outerRoot = new GameObject("Val_Outer");
        GameObject placed = Inst(inner);
        placed.SetParent(outerRoot);
        placed.GetComponent<OverrideComp>()!.A = 55;

        Guid outer = Author(outerRoot, "Val_Outer.prefab");

        Assert.Equal(55, Inst(outer).Children[0].GetComponent<OverrideComp>()!.A);
    }

    #endregion

    #region Placing one instance inside another

    [Fact]
    public void PlacingAnInstanceInsideAnotherBreaksItsLink()
    {
        Guid inner = AuthorLeaf("Place_Inner", 7);
        Guid outer = AuthorLeaf("Place_Outer", 1);

        GameObject instance = Inst(outer);
        LoadSceneWith(instance);

        GameObject placed = Inst(inner);
        placed.SetParent(instance);
        PrefabUtility.FlattenIfPlacedInsideAnInstance(placed);

        Assert.False(placed.IsPrefabInstance);
        Assert.Equal(Guid.Empty, placed.PrefabAssetId);
        Assert.Equal(7, placed.GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void PlacingAnInstanceUnderAPlainObjectLeavesItAlone()
    {
        Guid inner = AuthorLeaf("Keep_Inner", 7);

        var plain = new GameObject("Plain");
        LoadSceneWith(plain);

        GameObject placed = Inst(inner);
        placed.SetParent(plain);
        PrefabUtility.FlattenIfPlacedInsideAnInstance(placed);

        Assert.True(placed.IsPrefabInstance);
        Assert.Equal(inner, placed.PrefabAssetId);
    }

    [Fact]
    public void AFlattenedInstanceBecomesTheOutersOwnAddition()
    {
        Guid inner = AuthorLeaf("Add_Inner", 7);
        Guid outer = AuthorLeaf("Add_Outer", 1);

        GameObject instance = Inst(outer);
        LoadSceneWith(instance);

        GameObject placed = Inst(inner);
        placed.SetParent(instance);
        PrefabUtility.FlattenIfPlacedInsideAnInstance(placed);

        // No source identity means the instance added it, so a refresh leaves it alone.
        Assert.False(PrefabUtility.IsProvidedByPrefab(placed));

        PrefabUtility.RefreshAllInstances(outer);

        Assert.Contains(instance.Children, c => c.Name == "Add_Inner");
    }

    #endregion

    #region Shipping

    [Fact]
    public void ANestedLinkInAnOlderAssetIsStrippedForTheBuild()
    {
        // A payload written before flattening was enforced: an instance inside an instance.
        var echo = EchoObject.NewCompound();
        echo["Prefab"] = Link(Guid.NewGuid());

        var child = EchoObject.NewCompound();
        child["Prefab"] = Link(Guid.NewGuid());

        var children = EchoObject.NewList();
        children.ListAdd(child);
        echo["Children"] = children;

        Assert.True(ImportHelper.FlattenNestedPrefabLinks(echo));

        Assert.True(echo.TryGet("Prefab", out _));      // the outer instance is still one
        Assert.False(child.TryGet("Prefab", out _));    // the one inside it is not

        static EchoObject Link(Guid id)
        {
            var link = EchoObject.NewCompound();
            link["AssetId"] = new EchoObject(id.ToString());
            return link;
        }
    }

    #endregion

    #region Assets authored before flattening

    /// <summary>Writes a prefab file directly, the way one authored before flattening would look.</summary>
    private Guid WriteLegacy(GameObject source, string path)
    {
        System.IO.File.WriteAllText(AssetAbsolutePath(path),
            Serializer.Serialize(typeof(object), source).WriteToString());
        return Assets.ImportFile(path);
    }

    private (Guid inner, Guid outer) MakeLegacyNested()
    {
        var innerSource = new GameObject("L_Inner");
        innerSource.AddComponent<OverrideComp>().A = 1;
        Guid inner = CreatePrefabAsset(innerSource, "L_Inner.prefab");

        var outerSource = new GameObject("L_Outer");
        outerSource.AddComponent<OverrideComp>().A = 10;
        Inst(inner).SetParent(outerSource);
        return (inner, WriteLegacy(outerSource, "L_Outer.prefab"));
    }

    [Fact]
    public void AnOlderNestedAssetStillLoadsWithItsContent()
    {
        (Guid inner, Guid outer) = MakeLegacyNested();

        GameObject instance = Inst(outer);

        // Loaded flat: the content is all there, as this prefab's own.
        Assert.Single(instance.Children);
        Assert.NotEqual(inner, instance.Children[0].PrefabAssetId);
        Assert.Equal(1, instance.Children[0].GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void ApplyingAnOlderNestedInstanceKeepsItsContent()
    {
        (Guid _, Guid outer) = MakeLegacyNested();

        GameObject instance = Inst(outer);
        LoadSceneWith(instance);
        instance.GetComponent<OverrideComp>()!.A = 42;
        PrefabUtility.ReconcileInstance(instance);

        PrefabUtility.ApplyOverrides(instance);

        // The nested content came from the prefab, so applying must not mistake it for something the
        // instance added and drop it from the asset.
        GameObject fresh = Inst(outer);
        Assert.Equal(42, fresh.GetComponent<OverrideComp>()!.A);
        Assert.Single(fresh.Children);
        Assert.Equal(1, fresh.Children[0].GetComponent<OverrideComp>()!.A);
    }

    [Fact]
    public void RefreshingAnOlderNestedInstanceKeepsItsContent()
    {
        (Guid _, Guid outer) = MakeLegacyNested();

        GameObject instance = Inst(outer);
        LoadSceneWith(instance);

        PrefabUtility.RefreshAllInstances(outer);

        Assert.Single(instance.Children);
    }

    [Fact]
    public void ASceneRoundTripKeepsBothLinks()
    {
        (Guid inner, Guid outer) = MakeLegacyNested();

        GameObject instance = Inst(outer);
        Scene scene = LoadSceneWith(instance);

        EchoObject echo = Serializer.Serialize(typeof(object), scene);
        var reloaded = Serializer.Deserialize<Scene>(echo)!;
        Scene.Load(reloaded);
        Scene.ProcessPendingLoad();

        GameObject live = Scene.Current!.AllObjects.First(o => o.Parent == null && o.IsPrefabInstance);
        Assert.Equal(outer, live.PrefabAssetId);
        Assert.Single(live.Children);

        // The older asset was flattened when it was imported, so its content belongs to the outer
        // prefab now rather than to the one it was nested from.
        Assert.NotEqual(inner, live.Children[0].PrefabAssetId);
    }

    #endregion
}
