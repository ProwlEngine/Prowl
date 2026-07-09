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

        int cols = Maths.Max(1, ColumnsFor(n, content.Size.X));
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

    private int ColumnsFor(int n, float availableWidth) => _constraint switch
    {
        Constraint.FixedColumnCount => _constraintCount,
        Constraint.FixedRowCount => (n + _constraintCount - 1) / _constraintCount,
        _ => FlexibleColumns(n, availableWidth),
    };

    private int FlexibleColumns(int n, float availableWidth)
    {
        float step = _cellSize.X + _spacing.X;
        if (step <= 0f) return n;
        int c = (int)((availableWidth + _spacing.X) / step);
        return Maths.Clamp(c, 1, Maths.Max(1, n));
    }

    // ---- ILayoutElement (grid content size) ----
    private void Dimensions(out int cols, out int rows)
    {
        int n = GetLayoutChildren().Count;
        cols = _constraint switch
        {
            Constraint.FixedColumnCount => Maths.Max(1, _constraintCount),
            Constraint.FixedRowCount => Maths.Max(1, (n + _constraintCount - 1) / Maths.Max(1, _constraintCount)),
            _ => Maths.Max(1, (int)MathF.Ceiling(MathF.Sqrt(Maths.Max(1, n)))),
        };
        rows = n == 0 ? 0 : (n + cols - 1) / cols;
    }

    public override float PreferredWidth
    {
        get { Dimensions(out int cols, out _); return _paddingLeft + _paddingRight + cols * _cellSize.X + Maths.Max(0, cols - 1) * _spacing.X; }
    }
    public override float PreferredHeight
    {
        get { Dimensions(out _, out int rows); return _paddingTop + _paddingBottom + rows * _cellSize.Y + Maths.Max(0, rows - 1) * _spacing.Y; }
    }
    public override float MinWidth => PreferredWidth;
    public override float MinHeight => PreferredHeight;
}
