// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Modeling;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Editor.Test.Modeling;

public class PMeshTests : EditorTestHarness
{
    /// <summary>A five sided flat face and a quad, with corner UVs, a sharp edge and a value on one point only.</summary>
    private static GeometryData Sample()
    {
        var mesh = new GeometryData();
        mesh.AddLoopAttribute(MeshGeometry.UV, GeometryData.AttributeBaseType.Float, 2);
        var a = mesh.AddVertex(0, 0, 0);
        var b = mesh.AddVertex(2, 0, 0);
        var c = mesh.AddVertex(3, 0, 1);
        var d = mesh.AddVertex(1, 0, 2);
        var e = mesh.AddVertex(-1, 0, 1);
        var f = mesh.AddVertex(2, 0, -1);
        GeometryData.Face five = mesh.AddFace(a, e, d, c, b)!;
        mesh.AddFace(a, b, f);
        int corner = 0;
        GeometryData.Loop loop = five.Loop!;
        do
        {
            loop.Attributes[MeshGeometry.UV] = new GeometryData.FloatAttributeValue(corner, corner * 0.5f);
            corner++;
            loop = loop.Next!;
        } while (loop != five.Loop);
        mesh.FindEdge(a, b)!.IsSharp = true;
        c.SetAttribute("pin", new GeometryData.IntAttributeValue(7));
        return mesh;
    }

    private static PMeshDocument Document()
    {
        var root = new PMeshObject { Id = Guid.NewGuid(), Name = "Body", Mesh = Sample(), Position = new Float3(1, 2, 3) };
        root.Materials.Add(Guid.NewGuid());
        var child = new PMeshObject { Id = Guid.NewGuid(), Name = "Lid", Parent = root.Id, Mesh = Sample(), Scale = new Float3(2), SmoothingAngle = 45f };
        return new PMeshDocument { Objects = { root, child } };
    }

    [Fact]
    public void ADocumentReadsBackAsItWasWritten()
    {
        PMeshDocument written = Document();

        PMeshDocument read = PMeshDocument.Read(written.Write());

        Assert.Equal(2, read.Objects.Count);
        for (int i = 0; i < 2; i++)
        {
            PMeshObject before = written.Objects[i], after = read.Objects[i];
            Assert.Equal(before.Id, after.Id);
            Assert.Equal(before.Name, after.Name);
            Assert.Equal(before.Parent, after.Parent);
            Assert.Equal(before.Position, after.Position);
            Assert.Equal(before.Scale, after.Scale);
            Assert.Equal(before.SmoothingAngle, after.SmoothingAngle);
            Assert.Equal(before.Materials, after.Materials);

            GeometryData mesh = after.Mesh;
            Assert.Equal(6, mesh.Vertices.Count);
            Assert.Equal(2, mesh.Faces.Count);
            Assert.Contains(mesh.Faces, f => f.VertCount == 5);
            Assert.Single(mesh.Edges, e => e.IsSharp);
            Assert.Single(mesh.Vertices, v => v.TryGetAttribute("pin", out var pin) && pin is GeometryData.IntAttributeValue n && n.Data[0] == 7);
            GeometryData.Face five = mesh.Faces.First(f => f.VertCount == 5);
            Assert.Equal(new[] { 0f, 1f, 2f, 3f, 4f }, CornerUs(five));
        }
    }

    private static float[] CornerUs(GeometryData.Face face)
    {
        var us = new List<float>();
        GeometryData.Loop loop = face.Loop!;
        do
        {
            us.Add(((GeometryData.FloatAttributeValue)loop.Attributes[MeshGeometry.UV]).Data[0]);
            loop = loop.Next!;
        } while (loop != face.Loop);
        return [.. us];
    }

    [Fact]
    public void ImportingBuildsAPrefabWithAMeshPerObject()
    {
        PMeshDocument document = Document();
        File.WriteAllBytes(AssetAbsolutePath("Crate.pmesh"), document.Write());
        Assets.Refresh();

        Guid guid = Assets.GetEntry("Crate.pmesh")!.Guid;
        PrefabAsset prefab = Load<PrefabAsset>(guid)!;
        GameObject root = GameObject.InstantiateDetached(prefab);

        GameObject body = root.Transform.Find("Body")!.GameObject;
        GameObject lid = body.Transform.Find("Lid")!.GameObject;
        Assert.Equal(new Float3(1, 2, 3), body.Transform.LocalPosition);
        // The first read of a mesh not loaded yet, which once came back empty
        Mesh mesh = body.GetComponent<MeshRenderer>()!.Mesh!;
        Assert.Equal(4 * 3, mesh.Indices!.Length);
        Assert.NotNull(lid.GetComponent<MeshRenderer>()!.Mesh);
    }

    /// <summary>Objects are known by their ids, so renaming one keeps its mesh asset and object identity.</summary>
    [Fact]
    public void RenamingAnObjectKeepsItsIdentity()
    {
        PMeshDocument document = Document();
        string path = AssetAbsolutePath("Crate.pmesh");
        File.WriteAllBytes(path, document.Write());
        Assets.Refresh();
        Guid guid = Assets.GetEntry("Crate.pmesh")!.Guid;
        GameObject before = GameObject.InstantiateDetached(Load<PrefabAsset>(guid)!).Transform.Find("Body")!.GameObject;
        Guid meshBefore = before.GetComponent<MeshRenderer>()!.Mesh!.AssetID;
        Guid objectBefore = before.SourceIdentifier;

        document.Objects[0].Name = "Hull";
        File.WriteAllBytes(path, document.Write());
        Assets.Reimport(guid);

        GameObject after = GameObject.InstantiateDetached(Load<PrefabAsset>(guid)!).Transform.Find("Hull")!.GameObject;
        Assert.Equal(meshBefore, after.GetComponent<MeshRenderer>()!.Mesh!.AssetID);
        Assert.NotEqual(Guid.Empty, objectBefore);
        Assert.Equal(objectBefore, after.SourceIdentifier);
    }

    [Fact]
    public void ImportSettingsAddCollidersAndLightmapUVs()
    {
        File.WriteAllBytes(AssetAbsolutePath("Crate.pmesh"), Document().Write());
        Assets.Refresh();
        Guid guid = Assets.GetEntry("Crate.pmesh")!.Guid;
        string metaPath = AssetAbsolutePath("Crate.pmesh") + ".meta";
        MetaFileData meta = MetaFile.Read(metaPath)!;
        meta.Settings ??= Echo.EchoObject.NewCompound();
        meta.Settings[Prowl.Editor.Importers.PMeshImporter.Keys.Collider] = new Echo.EchoObject((int)Prowl.Editor.Importers.PMeshCollider.Convex);
        meta.Settings[Prowl.Editor.Importers.PMeshImporter.Keys.LightmapUVs] = new Echo.EchoObject(true);
        MetaFile.Write(metaPath, meta);
        Assets.Reimport(guid);

        GameObject body = GameObject.InstantiateDetached(Load<PrefabAsset>(guid)!).Transform.Find("Body")!.GameObject;

        MeshCollider collider = body.GetComponent<MeshCollider>()!;
        Assert.True(collider.Convex);
        Mesh mesh = body.GetComponent<MeshRenderer>()!.Mesh!;
        Assert.Equal(mesh.VertexCount, mesh.UV2.Length);
    }

    private static GameObject Drawn(string name, Float3 position)
    {
        var go = new GameObject(name);
        go.Transform.Position = position;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        GeometryData quad = new();
        var a = quad.AddVertex(0, 0, 0);
        var b = quad.AddVertex(1, 0, 0);
        var c = quad.AddVertex(1, 0, 1);
        var d = quad.AddVertex(0, 0, 1);
        quad.AddFace(a, d, c, b);
        GeometryOperators.Triangulate(quad);
        renderer.Mesh = MeshGeometry.ToMesh(quad);
        return go;
    }

    /// <summary>Objects made into a model keep where they are relative to the active one, each with a copy of its mesh.</summary>
    [Fact]
    public void ObjectsBecomeAModelAroundTheActiveOne()
    {
        GameObject active = Drawn("Active", new Float3(1, 0, 0));
        GameObject other = Drawn("Other", new Float3(3, 0, 0));
        GameObject child = Drawn("Child", new Float3(3, 2, 0));
        child.SetParent(other, true);

        PMeshDocument document = PMeshDocument.FromObjects([active, other, child], active);

        Assert.Equal(3, document.Objects.Count);
        PMeshObject first = document.Objects.Single(o => o.Name == "Active");
        PMeshObject second = document.Objects.Single(o => o.Name == "Other");
        PMeshObject third = document.Objects.Single(o => o.Name == "Child");
        Assert.Equal(Float3.Zero, first.Position);
        Assert.Equal(new Float3(2, 0, 0), second.Position);
        Assert.Equal(second.Id, third.Parent);
        Assert.Equal(new Float3(0, 2, 0), third.Position);
        Assert.Equal(2, second.Mesh.Faces.Count);
        Assert.NotSame(first.Mesh, second.Mesh);
    }
}
