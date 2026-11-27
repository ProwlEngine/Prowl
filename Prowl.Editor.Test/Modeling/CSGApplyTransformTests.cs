// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.GUI.SceneView.Editors;
using Prowl.Runtime;
using Prowl.Runtime.CSG;
using Prowl.Vector;

using Xunit;

namespace Prowl.Editor.Test.Modeling;

public class CSGApplyTransformTests
{
    private const ModelEditingMode.TransformParts RotationScale = ModelEditingMode.TransformParts.Rotation | ModelEditingMode.TransformParts.Scale;

    private static (GameObject Root, CSGBrush Brush) Level()
    {
        var root = new GameObject("Root");
        root.Transform.LocalRotation = Quaternion.FromEuler(new Float3(0f, 30f, 0f));
        root.Transform.LocalScale = new Float3(1.5f, 2f, 1f);

        var model = new GameObject("Model");
        model.SetParent(root, worldPositionStays: false);
        model.AddComponent<CSGModel>();

        var go = new GameObject("Brush");
        go.SetParent(model, worldPositionStays: false);
        go.Transform.LocalPosition = new Float3(1f, 2f, 3f);
        go.Transform.LocalRotation = Quaternion.FromEuler(new Float3(0f, 0f, 45f));
        go.Transform.LocalScale = new Float3(1f, 1f, 0.5f);
        CSGBrush brush = go.AddComponent<CSGBrush>();
        brush.ResetToBox(new Float3(2f, 1f, 3f));
        return (root, brush);
    }

    private static Float3[] WorldPoints(CSGBrush brush)
        => brush.ControlMesh.Points.Select(p => Float4x4.TransformPoint(p, brush.Transform.LocalToWorldMatrix)).ToArray();

    [Fact]
    public void ShearedLevelKeepsItsShapeWithUnitTransforms()
    {
        (GameObject root, CSGBrush brush) = Level();
        Float3[] before = WorldPoints(brush);
        Float3 pivot = brush.Transform.Position;

        Assert.True(CSGSelection.ApplyTransform([root], RotationScale));

        Float3[] after = WorldPoints(brush);
        for (int i = 0; i < before.Length; i++) Assert.True(Float3.Distance(before[i], after[i]) < 1e-3f, $"point {i} moved");
        Assert.True(Float3.Distance(pivot, brush.Transform.Position) < 1e-4f);
        foreach (GameObject go in new[] { root, brush.GameObject.Parent!, brush.GameObject })
        {
            Assert.Equal(Float3.One, go.Transform.LocalScale);
            Assert.Equal(Quaternion.Identity, go.Transform.LocalRotation);
        }
        Assert.True(brush.IsShapeValid);
    }

    [Fact]
    public void RefusesWhenAMeshIsUnderneath()
    {
        (GameObject root, CSGBrush brush) = Level();
        var prop = new GameObject("Prop");
        prop.SetParent(root, worldPositionStays: false);
        prop.AddComponent<MeshRenderer>();
        Float3[] before = WorldPoints(brush);

        Assert.False(CSGSelection.ApplyTransform([root], RotationScale));

        Assert.Equal(new Float3(1.5f, 2f, 1f), root.Transform.LocalScale);
        Assert.Equal(before, WorldPoints(brush));
    }

    private static (CSGModel Model, CSGBrush Brush) Tidy()
    {
        var root = new GameObject("Model");
        CSGModel model = root.AddComponent<CSGModel>();
        var go = new GameObject("Brush");
        go.SetParent(root, worldPositionStays: false);
        CSGBrush brush = go.AddComponent<CSGBrush>();
        brush.ResetToBox(new Float3(2f));
        CSGSelection.KeepTidy(model, null);
        return (model, brush);
    }

    [Fact]
    public void AScaleGivenToABrushIsBakedIntoItsPoints()
    {
        (CSGModel model, CSGBrush brush) = Tidy();

        brush.Transform.LocalScale = new Float3(2f, 1f, 3f);
        CSGSelection.KeepTidy(model, null);

        Assert.Equal(Float3.One, brush.Transform.LocalScale);
        Float3 size = brush.Shape!.Bounds().Size;
        Assert.True(Float3.Distance(new Float3(4f, 2f, 6f), size) < 1e-3f, $"{size}");
    }

    [Fact]
    public void AnEditedBrushKeepsItsPivotCentred()
    {
        (CSGModel model, CSGBrush brush) = Tidy();

        CSGControlMesh moved = brush.ControlMesh.Clone();
        moved.TransformPoints(Float4x4.CreateTranslation(new Float3(3f, 0f, 0f)));
        brush.SetShape(moved, brush.Surfaces);
        CSGSelection.KeepTidy(model, null);

        Assert.True(Float3.Distance(new Float3(3f, 0f, 0f), brush.Transform.Position) < 1e-4f, $"{brush.Transform.Position}");
        Assert.True(Float3.Length(CSGBrushGeometry.Centroid(brush.Shape)) < 1e-4f);
    }

    [Fact]
    public void APivotPlacedOnPurposeStaysWhereItIs()
    {
        (CSGModel model, CSGBrush brush) = Tidy();
        brush.CustomPivot = true;

        CSGControlMesh moved = brush.ControlMesh.Clone();
        moved.TransformPoints(Float4x4.CreateTranslation(new Float3(3f, 0f, 0f)));
        brush.SetShape(moved, brush.Surfaces);
        CSGSelection.KeepTidy(model, null);

        Assert.Equal(Float3.Zero, brush.Transform.Position);
    }
}
