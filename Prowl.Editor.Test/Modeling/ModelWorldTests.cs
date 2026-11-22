// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Core;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Modeling;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Editor.Test.Modeling;

public class ModelWorldTests : EditorTestHarness
{
    private static GeometryData Quad()
    {
        var mesh = new GeometryData();
        var a = mesh.AddVertex(0, 0, 0);
        var b = mesh.AddVertex(1, 0, 0);
        var c = mesh.AddVertex(1, 0, 1);
        var d = mesh.AddVertex(0, 0, 1);
        mesh.AddFace(a, d, c, b);
        return mesh;
    }

    private Guid WriteModel(out PMeshDocument document)
    {
        var body = new PMeshObject { Name = "Body", Mesh = Quad() };
        var lid = new PMeshObject { Name = "Lid", Parent = body.Id, Mesh = Quad(), Position = new Float3(0, 1, 0) };
        document = new PMeshDocument { Objects = { body, lid } };
        File.WriteAllBytes(AssetAbsolutePath("Crate.pmesh"), document.Write());
        Assets.Refresh();
        return Assets.GetEntry("Crate.pmesh")!.Guid;
    }

    [Fact]
    public void OpeningAModelBuildsItsObjectsInAWorldOfItsOwn()
    {
        Guid guid = WriteModel(out PMeshDocument document);
        Scene edit = EditorSceneManager.EditWorld;

        ModelEditingMode.Open(guid);

        Assert.True(ModelEditingMode.IsEditing);
        Assert.Same(ModelEditingMode.World, EditorSceneManager.EditedWorld);
        Assert.Equal(SceneState.Dormant, edit.State);
        GameObject body = ModelEditingMode.World!.FindObject("Body")!;
        ModelObject model = body.GetComponent<ModelObject>()!;
        Assert.Equal(document.Objects[0].Id, model.Id);
        Assert.NotNull(body.GetComponent<MeshRenderer>()!.Mesh);
        GameObject lid = body.Transform.Find("Lid")!.GameObject;
        Assert.Equal(new Float3(0, 1, 0), lid.Transform.LocalPosition);

        ModelEditingMode.Exit();

        Assert.False(ModelEditingMode.IsEditing);
        Assert.Equal(SceneState.Running, edit.State);
    }

    [Fact]
    public void SavingWritesTheWorldBackKeepingEveryId()
    {
        Guid guid = WriteModel(out PMeshDocument document);
        ModelEditingMode.Open(guid);
        GameObject lid = ModelEditingMode.World!.FindObject("Body")!.Transform.Find("Lid")!.GameObject;
        lid.Transform.LocalPosition = new Float3(2, 3, 4);
        lid.Name = "Top";

        Assert.True(ModelEditingMode.Save());
        ModelEditingMode.Exit();

        PMeshDocument saved = PMeshDocument.Read(File.ReadAllBytes(AssetAbsolutePath("Crate.pmesh")));
        PMeshObject top = Assert.Single(saved.Objects, o => o.Name == "Top");
        Assert.Equal(document.Objects[1].Id, top.Id);
        Assert.Equal(document.Objects[0].Id, top.Parent);
        Assert.Equal(new Float3(2, 3, 4), top.Position);
        Assert.Equal(4, top.Mesh.Vertices.Count);
    }

    [Fact]
    public void OpeningAPrefabClosesTheModelFirst()
    {
        Guid guid = WriteModel(out _);
        ModelEditingMode.Open(guid);
        var authored = new GameObject("Crate");
        Guid prefab = CreatePrefabAsset(authored, "Crate.prefab");

        PrefabEditingMode.Enter(prefab);

        Assert.False(ModelEditingMode.IsEditing);
        Assert.True(PrefabEditingMode.IsEditing);
        PrefabEditingMode.Exit();
    }

    [Fact]
    public void AModelCanBeMadeFromAnotherModelsAsset()
    {
        Guid source = WriteModel(out _);
        Assert.True(ModelEditingMode.CanCreateModelFrom(source));

        Guid made = ModelEditingMode.CreateModelFrom(source, open: false);

        Assert.NotEqual(Guid.Empty, made);
        AssetEntry entry = Assets.GetEntry(made)!;
        PMeshDocument document = PMeshDocument.Read(File.ReadAllBytes(AssetAbsolutePath(entry.Path)));
        Assert.Equal(2, document.Objects.Count);
        PMeshObject lid = document.Objects.Single(o => o.Name == "Lid");
        Assert.Equal(document.Objects.Single(o => o.Name == "Body").Id, lid.Parent);
        Assert.Equal(new Float3(0, 1, 0), lid.Position);
    }

    /// <summary>Objects in the scene become a new model with no file, the scene untouched, until its first save names one.</summary>
    [Fact]
    public void EditingSceneObjectsAsAModelLeavesTheSceneAlone()
    {
        Scene edit = EditorSceneManager.EditWorld;
        GameObject crate = edit.Create("Crate");
        crate.Transform.Position = new Float3(4, 0, 0);
        crate.AddComponent<MeshRenderer>().Mesh = MeshGeometry.ToMesh(Triangulated(Quad()));
        GameObject lamp = edit.Create("Lamp");

        ModelEditingMode.EditAsModel([crate, lamp], crate);

        Assert.True(ModelEditingMode.IsEditing);
        Assert.Equal("Crate", IsolatedWorld.Current!.Name);
        GameObject copy = ModelEditingMode.World!.FindObject("Crate")!;
        Assert.Equal(Float3.Zero, copy.Transform.LocalPosition);
        Assert.Null(ModelEditingMode.World.FindObject("Lamp"));

        Assert.True(ModelEditingMode.SaveAs("Models/Crate.pmesh"));
        Assert.True(File.Exists(AssetAbsolutePath("Models/Crate.pmesh")));
        Assert.NotEqual(Guid.Empty, ModelEditingMode.EditingModelGuid);
        ModelEditingMode.Exit();

        Assert.False(crate.IsDisposed);
        Assert.Equal(new Float3(4, 0, 0), crate.Transform.Position);
        Assert.Null(crate.GetComponent<ModelObject>());
    }

    private static GeometryData Triangulated(GeometryData mesh)
    {
        GeometryOperators.Triangulate(mesh);
        return mesh;
    }

    /// <summary>A model object's mesh is saved with it, so undo puts back the mesh an edit replaced.</summary>
    [Fact]
    public void UndoPutsBackAModelObjectsMesh()
    {
        Guid guid = WriteModel(out _);
        ModelEditingMode.Open(guid);
        ModelObject model = ModelEditingMode.World!.FindObject("Body")!.GetComponent<ModelObject>()!;

        Undo.RecordObject(model, "Grow");
        model.Mesh = Primitives.Make(PrimitiveKind.Cube);
        Undo.FlushUndoRecordObjects();
        Undo.IncrementCurrentGroup();
        Assert.Equal(8, model.Mesh.Vertices.Count);
        Assert.True(IsolatedWorld.IsDirty);

        Undo.PerformUndo();

        Assert.Equal(4, model.Mesh.Vertices.Count);
        ModelEditingMode.Exit();
    }

    [Fact]
    public void JoiningMergesObjectsIntoTheActiveOne()
    {
        Guid guid = WriteModel(out _);
        ModelEditingMode.Open(guid);
        GameObject body = ModelEditingMode.World!.FindObject("Body")!;
        GameObject lid = body.Transform.Find("Lid")!.GameObject;

        ModelEditingMode.Join([body, lid], body);

        Assert.True(lid.IsDisposed);
        ModelObject model = body.GetComponent<ModelObject>()!;
        Assert.Equal(8, model.Mesh.Vertices.Count);
        Assert.Contains(model.Mesh.Vertices, v => MathF.Abs(v.Point.Y - 1f) < 1e-4f);
        ModelEditingMode.Exit();
    }

    [Fact]
    public void ADuplicatedObjectSavesWithAnIdOfItsOwn()
    {
        Guid guid = WriteModel(out PMeshDocument document);
        ModelEditingMode.Open(guid);
        GameObject body = ModelEditingMode.World!.FindObject("Body")!;
        var copy = new GameObject("Copy");
        ModelEditingMode.World.Add(copy);
        ModelObject twin = copy.AddComponent<ModelObject>();
        twin.Id = body.GetComponent<ModelObject>()!.Id;

        Assert.True(ModelEditingMode.Save());
        ModelEditingMode.Exit();

        PMeshDocument saved = PMeshDocument.Read(File.ReadAllBytes(AssetAbsolutePath("Crate.pmesh")));
        Assert.Equal(3, saved.Objects.Select(o => o.Id).Distinct().Count());
        Assert.Equal(document.Objects[0].Id, saved.Objects.Single(o => o.Name == "Body").Id);
    }
}
