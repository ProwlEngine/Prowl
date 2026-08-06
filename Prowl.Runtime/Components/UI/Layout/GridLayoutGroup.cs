// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.UI;

/// <summary>Arranges children on a fixed-cell-size grid, filling rows left-to-right, top-to-bottom.</summary>
[AddComponentMenu("UI/Layout/Grid Layout Group")]
[ComponentIcon("")] // Grid
public sealed class GridLayoutGroup : LayoutGroup
{
    public enum Constraint { Flexible, FixedColumnCount, FixedRowCount }

    [SerializeField] private Float2 _cellSize = new(100f, 100f);
    [SerializeField] private Float2 _spacing = Float2.Zero;
    [SerializeField] private Constraint _constraint = Constraint.Flexible;
    [SerializeField] private int _constraintCount = 2;

    public Float2 CellSize { get => _cellSize; set => SetField(ref _cellSize, value, UIDirtyFlags.Layout); }
    public Float2 Spacing { get => _spacing; set => SetField(ref _spacing, value, UIDirtyFlags.Layout); }
    public Constraint GridConstraint { get => _constraint; set => SetField(ref _constraint, value, UIDirtyFlags.Layout); }
    public int ConstraintCount { get => _constraintCount; set => SetField(ref _constraintCount, Maths.Max(1, value), UIDirtyFlags.Layout); }

    public override void Arrange(Rect rect)
    {
        List<GameObject> kids = GetLayoutChildren();
        int n = kids.Count;
        if (n == 0) return;

        Rect content = new Rect(
            rect.Min.X + _paddingLeft, rect.Min.Y + _paddingBottom,
            rect.Max.X - _paddingRight, rect.Max.Y - _paddingTop);

        int cols = ColumnsForWidth(n, content.Size.X);
        int rows = (n + cols - 1) / cols;

        float cellW = _cellSize.X, cellH = _cellSize.Y;
        float spX = _spacing.X, spY = _spacing.Y;
        float gridW = cols * cellW + (cols - 1) * spX;
        float gridH = rows * cellH + (rows - 1) * spY;

        float startX = AlignStart(content.Min.X, content.Size.X, gridW, HorizontalFactor());
        // +Y up: fill from the grid's top edge downward.
        float topY = AlignStart(content.Min.Y, content.Size.Y, gridH, VerticalFactor()) + gridH;

        for (int i = 0; i < n; i++)
        {
            int c = i % cols;
            int r = i / cols;
            float x = startX + c * (cellW + spX);
            float top = topY - r * (cellH + spY);
            SetChildRect(kids[i], new Rect(x, top - cellH, x + cellW, top));
        }
    }

    /// <summary>
    /// The column count the grid actually fills, given the content width available to it. The single
    /// source of truth: <see cref="Arrange"/> and the reported height both go through it, so a grid
    /// inside a <see cref="ContentSizeFitter"/> reports the size it will really lay out to.
    /// </summary>
    private int ColumnsForWidth(int n, float availableWidth)
    {
        switch (_constraint)
        {
            case Constraint.FixedColumnCount:
                return Maths.Max(1, _constraintCount);
            case Constraint.FixedRowCount:
                int rows = Maths.Max(1, _constraintCount);
                return Maths.Max(1, (n + rows - 1) / rows);
            default:
                float step = _cellSize.X + _spacing.X;
                if (step <= 0f) return Maths.Max(1, n);
                int fit = (int)MathF.Floor((availableWidth + _spacing.X + 0.001f) / step);
                return Maths.Clamp(fit, 1, Maths.Max(1, n));
        }
    }

    /// <summary>Content width currently available to the grid, from the last layout pass.</summary>
    private float ContentWidth()
    {
        RectTransform? rt = GameObject.RectTransform;
        float w = rt is null ? 0f : rt.ComputedRect.Size.X;
        return Maths.Max(0f, w - _paddingLeft - _paddingRight);
    }

    // ---- ILayoutElement (grid content size) ----
    // Width is reported without knowing how wide the parent will make us, so it reports the narrowest
    // useful grid (min) and a squarish one (preferred). Height is then derived from the width we
    // actually ended up with, which is what makes it agree with Arrange.

    private int WidthColumns(int n, bool minimum)
    {
        switch (_constraint)
        {
            case Constraint.FixedColumnCount:
                return Maths.Max(1, _constraintCount);
            case Constraint.FixedRowCount:
                int rows = Maths.Max(1, _constraintCount);
                return Maths.Max(1, (n + rows - 1) / rows);
            default:
                return minimum ? 1 : Maths.Max(1, (int)MathF.Ceiling(MathF.Sqrt(Maths.Max(1, n))));
        }
    }

    private float WidthFor(int cols)
        => _paddingLeft + _paddingRight + cols * _cellSize.X + Maths.Max(0, cols - 1) * _spacing.X;

    private float HeightForCurrentWidth()
    {
        int n = GetLayoutChildren().Count;
        if (n == 0) return _paddingTop + _paddingBottom;

        int cols = ColumnsForWidth(n, ContentWidth());
        int rows = _constraint == Constraint.FixedRowCount
            ? Maths.Min(Maths.Max(1, _constraintCount), n)
            : (n + cols - 1) / cols;

        return _paddingTop + _paddingBottom + rows * _cellSize.Y + Maths.Max(0, rows - 1) * _spacing.Y;
    }

    public override float PreferredWidth => WidthFor(WidthColumns(GetLayoutChildren().Count, minimum: false));
    public override float MinWidth => WidthFor(WidthColumns(GetLayoutChildren().Count, minimum: true));
    public override float PreferredHeight => HeightForCurrentWidth();
    public override float MinHeight => HeightForCurrentWidth();
}
