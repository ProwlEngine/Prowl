// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;

using Prowl.Clay.Importer;
using Prowl.Vector;

using Xunit;

namespace Prowl.Clay.Tests;

/// <summary>
/// The optimizer steps rewrite the scene in place, so what they claim to leave intact is worth
/// pinning: a merged-away mesh has to actually leave, a mirrored merge has to keep its handedness,
/// and a collapse has to not delete the sockets or bake shear the transform cannot express.
/// </summary>
public sealed class OptimizeStepsTests
{
    private static Model Load(string body, PostProcessFlags flags)
    {
        string json = $$"""
        {
          "asset": { "version": "2.0" },
          "scene": 0,
          "scenes": [ { "nodes": [ 0 ] } ],
          {{body}}
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return ModelImporter.Load(stream, "gltf", new ModelImporterSettings { PostProcess = flags });
    }

    /// <summary>
    /// Two identical triangles in the XY plane, wound counter-clockwise so their normals are +Z.
    /// They are separate meshes over one shared material, which is what makes them mergeable.
    /// </summary>
    private const string TriangleGeometry = """
      "meshes": [
        { "name": "TriA", "primitives": [ { "attributes": { "POSITION": 0 }, "indices": 1, "material": 0 } ] },
        { "name": "TriB", "primitives": [ { "attributes": { "POSITION": 0 }, "indices": 1, "material": 0 } ] }
      ],
      "materials": [ { "name": "M" } ],
      "accessors": [
        { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3" },
        { "bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR" }
      ],
      "bufferViews": [
        { "buffer": 0, "byteOffset": 0, "byteLength": 36 },
        { "buffer": 0, "byteOffset": 36, "byteLength": 6 }
      ],
      "buffers": [ { "byteLength": 44, "uri": "data:application/octet-stream;base64,AAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAgD8AAAAAAAABAAIAAAA=" } ]
    """;

    /// <summary>Geometric normal of the triangle starting at <paramref name="tri"/>.</summary>
    private static Float3 FaceNormal(Mesh mesh, int tri)
    {
        Float3 a = mesh.Vertices[(int)mesh.Indices[tri * 3 + 0]];
        Float3 b = mesh.Vertices[(int)mesh.Indices[tri * 3 + 1]];
        Float3 c = mesh.Vertices[(int)mesh.Indices[tri * 3 + 2]];
        return Float3.Cross(b - a, c - a);
    }


    /// <summary>
    /// Clearing the node's mesh reference is not enough: the mesh stayed in the scene, reached
    /// Model.Meshes, and the editor registers every entry there as a sub-asset, so the project
    /// gained a dead mesh asset that nothing referenced.
    /// </summary>
    [Fact]
    public void MergedAwayMesh_LeavesTheModel()
    {
        var model = Load($$"""
          "nodes": [
            { "name": "Root", "children": [ 1, 2 ] },
            { "name": "A", "mesh": 0 },
            { "name": "B", "mesh": 1, "translation": [ 5, 0, 0 ] }
          ],
          {{TriangleGeometry}}
        """, PostProcessFlags.OptimizeMeshes);

        var mesh = Assert.Single(model.Meshes);
        Assert.Equal(6, mesh.Vertices.Length); // both triangles, merged into one mesh
    }

    [Fact]
    public void MergedNodes_StillPointAtTheRightMesh()
    {
        var model = Load($$"""
          "nodes": [
            { "name": "Root", "children": [ 1, 2 ] },
            { "name": "A", "mesh": 0 },
            { "name": "B", "mesh": 1, "translation": [ 5, 0, 0 ] }
          ],
          {{TriangleGeometry}}
        """, PostProcessFlags.OptimizeMeshes);

        foreach (var node in model.Nodes)
            Assert.InRange(node.MeshIndex, -1, model.Meshes.Count - 1);
        Assert.Single(model.Nodes, n => n.MeshIndex == 0);
    }

    /// <summary>
    /// A negative-determinant transform mirrors, and the merged-in triangle then faces the other way
    /// unless its winding is reversed with it. Both halves of the merged mesh have to face the same
    /// direction, or the mirrored one renders backfacing.
    /// </summary>
    [Fact]
    public void MirroredChild_KeepsItsFacing()
    {
        var model = Load($$"""
          "nodes": [
            { "name": "Root", "children": [ 1, 2 ] },
            { "name": "A", "mesh": 0 },
            { "name": "B", "mesh": 1, "scale": [ -1, 1, 1 ] }
          ],
          {{TriangleGeometry}}
        """, PostProcessFlags.OptimizeMeshes);

        var mesh = Assert.Single(model.Meshes);
        Assert.Equal(2, mesh.Indices.Length / 3);
        Assert.True(FaceNormal(mesh, 0).Z * FaceNormal(mesh, 1).Z > 0f,
            "the mirrored triangle should face the same way as the one it merged into");
    }

    [Fact]
    public void UnmirroredChild_IsUnchanged()
    {
        var model = Load($$"""
          "nodes": [
            { "name": "Root", "children": [ 1, 2 ] },
            { "name": "A", "mesh": 0 },
            { "name": "B", "mesh": 1, "translation": [ 5, 0, 0 ] }
          ],
          {{TriangleGeometry}}
        """, PostProcessFlags.OptimizeMeshes);

        var mesh = Assert.Single(model.Meshes);
        Assert.True(FaceNormal(mesh, 0).Z > 0f);
        Assert.True(FaceNormal(mesh, 1).Z > 0f);
    }

    /// <summary>
    /// One mesh instanced by two nodes has nothing to merge. Appending it into itself walked a list
    /// that grew with every append, so the import never returned.
    /// </summary>
    [Fact]
    public void OneMeshInstancedTwice_IsLeftAlone()
    {
        var model = Load($$"""
          "nodes": [
            { "name": "Root", "children": [ 1, 2 ] },
            { "name": "A", "mesh": 0 },
            { "name": "B", "mesh": 0, "translation": [ 5, 0, 0 ] }
          ],
          {{TriangleGeometry}}
        """, PostProcessFlags.OptimizeMeshes);

        Assert.Equal(3, model.Meshes[0].Vertices.Length);
        Assert.Equal(2, model.Nodes.Count(n => n.MeshIndex == 0));
    }


    /// <summary>
    /// An attachment point is a childless empty, which is most of why an artist puts an empty in a
    /// file at all. The step used to read it as a pass-through and delete it.
    /// </summary>
    [Fact]
    public void NamedSocketEmpty_Survives()
    {
        var model = Load($$"""
          "nodes": [
            { "name": "Root", "children": [ 1 ] },
            { "name": "Mesh", "mesh": 0, "children": [ 2 ] },
            { "name": "WeaponSocket_R", "translation": [ 0, 1, 0 ] }
          ],
          {{TriangleGeometry}}
        """, PostProcessFlags.OptimizeGraph);

        Assert.Single(model.Nodes, n => n.Name == "WeaponSocket_R");
    }

    // The grouping nodes the step exists to remove still go.
    [Fact]
    public void PassThroughGroupingNode_IsStillCollapsed()
    {
        var model = Load($$"""
          "nodes": [
            { "name": "Root", "children": [ 1 ] },
            { "name": "Group", "translation": [ 0, 2, 0 ], "children": [ 2 ] },
            { "name": "Mesh", "mesh": 0, "translation": [ 1, 0, 0 ] }
          ],
          {{TriangleGeometry}}
        """, PostProcessFlags.OptimizeGraph);

        Assert.DoesNotContain(model.Nodes, n => n.Name == "Group");

        // The collapsed transform has to survive in the child it was folded into.
        var mesh = Assert.Single(model.Nodes, n => n.Name == "Mesh");
        Assert.Equal(1f, mesh.LocalPosition.X, 4);
        Assert.Equal(2f, mesh.LocalPosition.Y, 4);
    }

    /// <summary>
    /// Folding a non-uniform scale into a rotated child produces shear, which TRS cannot represent
    /// and the decompose silently drops, deforming the subtree with no warning. Uniform scale
    /// commutes with rotation, so only the non-uniform case has to be refused.
    /// </summary>
    [Fact]
    public void NonUniformScaleNode_IsNotCollapsed()
    {
        var model = Load($$"""
          "nodes": [
            { "name": "Root", "children": [ 1 ] },
            { "name": "Squashed", "scale": [ 2, 1, 1 ], "children": [ 2 ] },
            { "name": "Mesh", "mesh": 0, "rotation": [ 0, 0, 0.3826834, 0.9238795 ] }
          ],
          {{TriangleGeometry}}
        """, PostProcessFlags.OptimizeGraph);

        Assert.Single(model.Nodes, n => n.Name == "Squashed");
    }

    [Fact]
    public void UniformScaleNode_IsStillCollapsed()
    {
        var model = Load($$"""
          "nodes": [
            { "name": "Root", "children": [ 1 ] },
            { "name": "Scaled", "scale": [ 2, 2, 2 ], "children": [ 2 ] },
            { "name": "Mesh", "mesh": 0, "rotation": [ 0, 0, 0.3826834, 0.9238795 ] }
          ],
          {{TriangleGeometry}}
        """, PostProcessFlags.OptimizeGraph);

        Assert.DoesNotContain(model.Nodes, n => n.Name == "Scaled");
        var mesh = Assert.Single(model.Nodes, n => n.Name == "Mesh");
        Assert.Equal(2f, mesh.LocalScale.X, 4);
    }

    // ---------------------------------------------------------------- node extras

    /// <summary>
    /// extras is where an exporter puts the pipeline data a game needs, and a node carrying it is
    /// not a pass-through no matter how empty it otherwise looks.
    /// </summary>
    [Fact]
    public void NodeExtras_ReachTheModel()
    {
        var model = Load($$"""
          "nodes": [
            { "name": "Root", "children": [ 1 ] },
            { "name": "Spawn", "extras": { "team": "red", "count": 3, "active": true }, "children": [ 2 ] },
            { "name": "Mesh", "mesh": 0 }
          ],
          {{TriangleGeometry}}
        """, PostProcessFlags.None);

        var node = Assert.Single(model.Nodes, n => n.Name == "Spawn");
        Assert.Equal("red", node.Metadata["team"]);
        Assert.Equal(3L, node.Metadata["count"]);
        Assert.Equal(true, node.Metadata["active"]);
    }

    [Fact]
    public void NodeCarryingExtras_IsNotCollapsed()
    {
        var model = Load($$"""
          "nodes": [
            { "name": "Root", "children": [ 1 ] },
            { "name": "Spawn", "extras": { "team": "red" }, "children": [ 2 ] },
            { "name": "Mesh", "mesh": 0 }
          ],
          {{TriangleGeometry}}
        """, PostProcessFlags.OptimizeGraph);

        Assert.Single(model.Nodes, n => n.Name == "Spawn");
    }
}
