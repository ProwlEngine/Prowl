using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Vector.Tests;

/// <summary>Tests for the weight list attribute: reading, changing, mixing, and surviving a snapshot.</summary>
public class GeometryWeightsTests
{
    private const string Skin = "Skin";

    [Fact]
    public void A_group_that_is_not_listed_weighs_nothing()
    {
        var weights = new GeometryData.WeightsAttributeValue([2, 5], [0.25f, 0.75f]);

        Assert.Equal(0.25f, weights[2]);
        Assert.Equal(0.75f, weights[5]);
        Assert.Equal(0f, weights[3]);
    }

    [Fact]
    public void With_sets_one_group_and_drops_it_at_nothing()
    {
        var weights = new GeometryData.WeightsAttributeValue([2, 5], [0.25f, 0.75f]);

        GeometryData.WeightsAttributeValue changed = weights.With(2, 0.5f).With(7, 0.1f);
        Assert.Equal(0.5f, changed[2]);
        Assert.Equal(0.1f, changed[7]);
        Assert.Equal(3, changed.Groups.Length);

        GeometryData.WeightsAttributeValue dropped = changed.With(5, 0);
        Assert.Equal(0f, dropped[5]);
        Assert.Equal(2, dropped.Groups.Length);
        Assert.Equal(2, weights.Groups.Length);
    }

    [Fact]
    public void Normalized_weights_add_up_to_one()
    {
        var weights = new GeometryData.WeightsAttributeValue([0, 1], [1f, 3f]);

        GeometryData.WeightsAttributeValue normal = weights.Normalized();
        Assert.Equal(0.25f, normal[0], 5);
        Assert.Equal(0.75f, normal[1], 5);
    }

    [Fact]
    public void Blend_mixes_groups_by_share_and_drops_the_tiny()
    {
        var a = new GeometryData.WeightsAttributeValue([0, 1], [1f, 0.00001f]);
        var b = new GeometryData.WeightsAttributeValue([1, 2], [0.00001f, 1f]);

        GeometryData.WeightsAttributeValue mixed = GeometryData.WeightsAttributeValue.Blend([a, b], [0.25f, 0.75f]);
        Assert.Equal(0.25f, mixed[0], 5);
        Assert.Equal(0.75f, mixed[2], 5);
        Assert.Equal(2, mixed.Groups.Length);
    }

    [Fact]
    public void Weights_survive_a_snapshot_and_are_copied_not_shared()
    {
        var mesh = new GeometryData();
        GeometryData.Vertex a = mesh.AddVertex(0, 0, 0), b = mesh.AddVertex(1, 0, 0), c = mesh.AddVertex(0, 1, 0);
        mesh.AddFace(a, b, c);
        a.SetAttribute(Skin, new GeometryData.WeightsAttributeValue([3], [1f]));

        GeometrySnapshot snapshot = mesh.Capture();
        a.SetAttribute(Skin, new GeometryData.WeightsAttributeValue([4], [1f]));
        mesh.Restore(snapshot);

        Assert.True(mesh.Vertices[0].TryGetAttribute(Skin, out GeometryData.AttributeValue value));
        var restored = (GeometryData.WeightsAttributeValue)value;
        Assert.Equal(1f, restored[3]);
        Assert.Equal(0f, restored[4]);
        Assert.False(mesh.Vertices[1].TryGetAttribute(Skin, out _));
    }

    [Fact]
    public void Attribute_names_list_what_was_set_without_a_declaration()
    {
        var mesh = new GeometryData();
        GeometryData.Vertex a = mesh.AddVertex(0, 0, 0), b = mesh.AddVertex(1, 0, 0), c = mesh.AddVertex(0, 1, 0);
        mesh.AddFace(a, b, c);
        Assert.Empty(mesh.VertexAttributeNames());

        a.SetAttribute(Skin, new GeometryData.WeightsAttributeValue([0], [1f]));
        mesh.Edges[0].SetAttribute("Seam", new GeometryData.IntAttributeValue(1));

        Assert.Equal([Skin], mesh.VertexAttributeNames());
        Assert.Equal(["Seam"], mesh.EdgeAttributeNames());
        Assert.Empty(mesh.FaceAttributeNames());
    }
}
