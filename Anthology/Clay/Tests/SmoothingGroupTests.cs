// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;

using Prowl.Clay.Importer;
using Prowl.Vector;

using Xunit;

namespace Prowl.Clay.Tests;

/// <summary>
/// The <c>s</c> directive is the only thing an OBJ can say about how it wants to be shaded, and an
/// OBJ with no <c>vn</c> records has nothing else to go on. Dropping it left the whole file to one
/// global smoothing angle, so a model authored with hard and smooth sections came in as all one or
/// all the other.
/// </summary>
public sealed class SmoothingGroupTests
{
    /// <summary>
    /// Two triangles sharing the edge from (0,0,0) to (1,0,0), folded 90 degrees apart, so the angle
    /// alone calls that edge hard. <paramref name="smoothing"/> goes above both faces, so whatever it
    /// says applies to the whole fold.
    /// </summary>
    private static Model LoadFold(string smoothing, float smoothingAngleDeg)
    {
        string obj = $"""
        v 0 0 0
        v 1 0 0
        v 0 1 0
        v 0 0 1
        {smoothing}
        f 1 2 3
        f 1 2 4
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(obj));
        return ModelImporter.Load(ms, "obj", ModelImporterSettings.Raw with
        {
            PostProcess = PostProcessFlags.Triangulate | PostProcessFlags.GenerateSmoothNormals,
            SmoothNormalsAngleDeg = smoothingAngleDeg,
        });
    }

    /// <summary>Vertex count tells us whether the generator split along a hard edge.</summary>
    private static int VertexCount(Model model) => model.Meshes[0].Vertices.Length;

    // Nothing changed for files that say nothing: the angle still decides, as it always did.
    [Fact]
    public void FileWithoutSmoothingDirectives_StillUsesTheAngle()
    {
        Assert.Equal(4, VertexCount(LoadFold("", 179f)));  // smoothed, nothing split
        Assert.Equal(6, VertexCount(LoadFold("", 1f)));    // both shared vertices split
    }

    /// <summary>
    /// One group across the whole fold means the author asked for a smooth surface, and that beats
    /// the angle, which would otherwise split the 90 degree edge.
    /// </summary>
    [Fact]
    public void OneSmoothingGroup_SmoothsAcrossAnAngleThatWouldOtherwiseSplit()
    {
        Assert.Equal(6, VertexCount(LoadFold("", 1f)));      // the angle would split this fold
        Assert.Equal(4, VertexCount(LoadFold("s 1", 1f)));   // the group says otherwise
    }

    // "s off" is the author saying this face shades alone, whatever the angle would have allowed.
    [Fact]
    public void SmoothingOff_KeepsTheEdgeHardAtAnyAngle()
    {
        Assert.Equal(4, VertexCount(LoadFold("s 1", 179f)));   // the angle would smooth this fold
        Assert.Equal(6, VertexCount(LoadFold("s off", 179f))); // the author said not to
    }

    [Fact]
    public void SmoothingZero_MeansTheSameAsOff()
    {
        Assert.Equal(VertexCount(LoadFold("s off", 179f)), VertexCount(LoadFold("s 0", 179f)));
    }

    /// <summary>
    /// Different groups never smooth together, which is the case that makes groups worth having:
    /// a hard crease between two smooth panels.
    /// </summary>
    [Fact]
    public void DifferentSmoothingGroups_NeverSmoothTogether()
    {
        string obj = """
        v 0 0 0
        v 1 0 0
        v 0 1 0
        v 0 0 1
        s 1
        f 1 2 3
        s 2
        f 1 2 4
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(obj));
        var model = ModelImporter.Load(ms, "obj", ModelImporterSettings.Raw with
        {
            // Wide enough that the angle alone would smooth these two together.
            PostProcess = PostProcessFlags.Triangulate | PostProcessFlags.GenerateSmoothNormals,
            SmoothNormalsAngleDeg = 179f,
        });

        var mesh = model.Meshes[0];
        // The two shared vertices each carry a different normal per group, so both had to split.
        Assert.Equal(6, mesh.Vertices.Length);
    }

    // Groups have to survive the triangulation of an n-gon, which runs before normals are generated.
    [Fact]
    public void SmoothingGroupsSurviveTriangulation()
    {
        string obj = """
        v 0 0 0
        v 2 0 0
        v 3 1.5 0
        v 1 2.5 0
        v -1 1.5 0
        v 0 0 1
        s 1
        f 1 2 3 4 5
        s 2
        f 1 2 6
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(obj));
        var model = ModelImporter.Load(ms, "obj", ModelImporterSettings.Raw with
        {
            PostProcess = PostProcessFlags.Triangulate | PostProcessFlags.GenerateSmoothNormals,
            SmoothNormalsAngleDeg = 179f,
        });

        var mesh = model.Meshes[0];
        // The pentagon's own three triangles stay smooth with each other, so its interior normals
        // agree, while the vertices it shares with the second group are split.
        Assert.True(mesh.Vertices.Length > 6, "the two groups should not have been smoothed together");

        foreach (var normal in mesh.Normals!)
            Assert.Equal(1f, Float3.Length(normal), 3);
    }
}
