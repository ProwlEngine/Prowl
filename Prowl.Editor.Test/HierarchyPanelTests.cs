// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>The rows the Hierarchy panel builds each frame: what a frame costs, and that each row's id follows its object.</summary>
public class HierarchyPanelTests : EditorTestHarness
{
    private readonly HierarchyPanel _panel = new();
    private readonly List<TreeNode> _nodes = [];

    private SceneAsset Author(string path, params string[] roots)
    {
        var world = new Scene(SceneMode.Edit);
        foreach (string root in roots) world.Create(root);
        return AssetDatabase.Get<SceneAsset>(CreateSceneAsset(world, path))!;
    }

    private List<TreeNode> Build(Scene? world = null)
    {
        _nodes.Clear();
        _panel.BuildSceneNodes(world ?? EditorSceneManager.EditedWorld, _nodes);
        return _nodes;
    }

    // Every object row carries the id the rest of the panel asks for by object: ping, drag hover and rename match on it.
    private void AssertRowIds(int rows, Scene? world = null)
    {
        List<TreeNode> objects = [.. Build(world).Where(node => node.UserData is GameObject)];
        Assert.Equal(rows, objects.Count);
        foreach (TreeNode node in objects)
            Assert.Equal(HierarchyPanel.NodeId((GameObject)node.UserData!), node.Id);
        Assert.Equal(rows, objects.Select(node => node.Id).Distinct().Count());
    }

    [Fact]
    public void BuildingTheRows_MakesNoGarbagePerObject()
    {
        OpenScene scene = Open(Author("A.scene", "a"));
        Scene world = EditorSceneManager.EditWorld;
        const int Roots = 500;
        for (int i = 0; i < Roots; i++)
            world.Create("child", world.Create(scene.Section!, "root"));

        // With no font the editor theme is built again on every color read, which would drown what the rows cost.
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        using (Stream geist = EditorApplication.GetEmbeddedResource("Geist-Regular.ttf")!)
            EditorTheme.DefaultFont = new Scribe.FontFile(geist);
        long perFrame;
        try
        {
            Build();
            Build();
            const int Frames = 10;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < Frames; i++) Build();
            perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / Frames;
        }
        finally { EditorTheme.DefaultFont = font; }

        Assert.Equal(Roots * 2 + 2, _nodes.Count);
        // An id string is over 100 bytes, so one per object would be over 100,000 here.
        Assert.True(perFrame < 4096, $"Building {_nodes.Count} rows allocated {perFrame} bytes a frame.");
    }

    [Fact]
    public void ARowsId_FollowsItsObjectsIdentifierAndScene()
    {
        OpenScene a = Open(Author("A.scene", "a"));
        OpenScene b = Additive(Author("B.scene", "b"));
        GameObject root = Assert.Single(a.Section!.Roots);
        GameObject child = EditorSceneManager.EditWorld.Create("child", root);
        AssertRowIds(3);
        string first = HierarchyPanel.NodeId(child);

        child.SetIdentifier(Guid.NewGuid());
        AssertRowIds(3);

        root.MoveTo(b.Section!);
        AssertRowIds(3);

        child.SetParent(null);
        child.MoveTo(a.Section!);
        AssertRowIds(3);

        // Saving under a new name gives the scene a new asset, which is the scene's part of every id in it.
        Assert.True(EditorSceneManager.SaveAs(a, "Renamed.scene"));
        AssertRowIds(3);
        Assert.NotEqual(first, HierarchyPanel.NodeId(child));
    }

    [Fact]
    public void ARowsId_FollowsASectionsName_InAWorldWithNoOpenScenes()
    {
        Scene play = Scene.Create("Play");
        try
        {
            SceneSection level = play.CreateSection("Level");
            GameObject kept = play.Create(play.Persistent, "kept");
            GameObject root = play.Create(level, "root");
            play.Create("child", root);
            AssertRowIds(3, play);

            level.Name = "Renamed";
            AssertRowIds(3, play);

            root.MoveTo(play.Persistent);
            kept.MoveTo(level);
            AssertRowIds(3, play);
        }
        finally { play.Dispose(); }
    }

    [Fact]
    public void HiddenObjects_GetNoRow_AndAParentOfOnlyHiddenChildren_ShowsNoArrow()
    {
        OpenScene scene = Open(Author("A.scene", "a"));
        Scene world = EditorSceneManager.EditWorld;
        GameObject root = Assert.Single(scene.Section!.Roots);
        world.Create("hidden", root).HideFlags = HideFlags.Hide;
        world.Create(scene.Section!, "hiddenRoot").HideFlags = HideFlags.HideAndDontSave;

        TreeNode row = Assert.Single(Build(), node => node.UserData is GameObject);
        Assert.Same(root, row.UserData);
        Assert.False(row.HasChildren);

        world.Create("shown", root);
        Assert.True(Build().Single(node => ReferenceEquals(node.UserData, root)).HasChildren);
    }
}
