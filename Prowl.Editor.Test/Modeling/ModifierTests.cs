// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Importers;
using Prowl.Editor.Modeling;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Editor.Test.Modeling;

public class ModifierTests : EditorTestHarness
{
    private static GeometryData Quad()
    {
        var mesh = new GeometryData();
        mesh.AddFace(mesh.AddVertex(0, 0, 0), mesh.AddVertex(1, 0, 0), mesh.AddVertex(1, 1, 0), mesh.AddVertex(0, 1, 0));
        return mesh;
    }

    private static int Triangles(Mesh mesh) => mesh.Indices!.Length / 3;

    [Fact]
    public void AStackReadsBackFromTheDocument()
    {
        var item = new PMeshObject { Name = "Body", Mesh = Quad() };
        item.Modifiers.Add(new MirrorModifier { Axis = 2, Offset = 0.5f });
        item.Modifiers.Add(new SubdivideModifier { Levels = 2, Enabled = false });
        var document = new PMeshDocument { Objects = { item } };

        PMeshObject read = PMeshDocument.Read(document.Write()).Objects[0];

        Assert.Equal(2, read.Modifiers.Count);
        MirrorModifier mirror = Assert.IsType<MirrorModifier>(read.Modifiers[0]);
        Assert.Equal(2, mirror.Axis);
        Assert.Equal(0.5f, mirror.Offset);
        SubdivideModifier subdivide = Assert.IsType<SubdivideModifier>(read.Modifiers[1]);
        Assert.Equal(2, subdivide.Levels);
        Assert.False(subdivide.Enabled);
    }

    [Fact]
    public void TheBuiltMeshIsTheEndOfTheStack_AndTheMeshUnderneathIsLeftAlone()
    {
        var item = new PMeshObject { Name = "Body", Mesh = Quad() };
        item.Modifiers.Add(new MirrorModifier { Axis = 0 });
        item.Modifiers.Add(new SolidifyModifier { Thickness = 0.1f, Enabled = false });

        Mesh mesh = PMeshImporter.BuildMesh(item);

        Assert.Equal(4, Triangles(mesh));
        Assert.Single(item.Mesh.Faces);
        Assert.Equal(4, item.Mesh.Vertices.Count);
    }

    [Fact]
    public void SimplifyIsLeftOutWhileDragging()
    {
        var item = new PMeshObject { Name = "Body", Mesh = Quad() };
        item.Modifiers.Add(new SubdivideModifier { Levels = 3 });
        item.Modifiers.Add(new SimplifyModifier { Ratio = 0.25f });

        int dragging = Triangles(PMeshImporter.BuildMesh(item, dragging: true));
        int still = Triangles(PMeshImporter.BuildMesh(item));

        Assert.Equal(128, dragging);
        Assert.True(still < dragging);
    }

    [Fact]
    public void ImportingRunsTheStack()
    {
        var item = new PMeshObject { Name = "Body", Mesh = Quad() };
        item.Modifiers.Add(new SolidifyModifier { Thickness = 0.2f });
        File.WriteAllBytes(AssetAbsolutePath("Slab.pmesh"), new PMeshDocument { Objects = { item } }.Write());
        Assets.Refresh();

        GameObject root = GameObject.InstantiateDetached(Load<PrefabAsset>(Assets.GetEntry("Slab.pmesh")!.Guid)!);

        Mesh mesh = root.Transform.Find("Body")!.GameObject.GetComponent<MeshRenderer>()!.Mesh!;
        Assert.Equal(12, Triangles(mesh));
    }
}
