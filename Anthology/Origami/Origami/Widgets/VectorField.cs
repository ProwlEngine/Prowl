// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Numerics;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using SysColor = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// Shared rendering for vector field components. Draws N numeric fields in a horizontal
/// row with colored prefix badges (X=red, Y=green, Z=blue, W=gray).
/// </summary>
internal static class VectorFieldInternal
{
    // Nebula axis colors (.w2vaxis): X #fb7185, Y #4ade80, Z #60a5fa, W #d96bd8.
    internal static readonly SysColor XColor = SysColor.FromArgb(255, 251, 113, 133);
    internal static readonly SysColor YColor = SysColor.FromArgb(255, 74, 222, 128);
    internal static readonly SysColor ZColor = SysColor.FromArgb(255, 96, 165, 250);
    internal static readonly SysColor WColor = SysColor.FromArgb(255, 217, 107, 216);

    private static void Cell<T>(Paper paper, string id, string label, T v, SysColor c, Action<T> set, float h)
        where T : struct, INumber<T>
        => Origami.NumericField<T>(paper, id, v, set)
            .DraggableLabel(label, c, compact: true).Height(h).Width(UnitValue.Stretch()).Show();

    internal static void Draw2<T>(Paper paper, string id, OrigamiTheme theme,
        string l0, T v0, SysColor c0, Action<T> s0,
        string l1, T v1, SysColor c1, Action<T> s1, float h = 24f)
        where T : struct, INumber<T>
    {
        using (paper.Row(id).Height(UnitValue.Auto).RowBetween(6).Enter())
        {
            Cell(paper, $"{id}_0", l0, v0, c0, s0, h);
            Cell(paper, $"{id}_1", l1, v1, c1, s1, h);
        }
    }

    internal static void Draw3<T>(Paper paper, string id, OrigamiTheme theme,
        string l0, T v0, SysColor c0, Action<T> s0,
        string l1, T v1, SysColor c1, Action<T> s1,
        string l2, T v2, SysColor c2, Action<T> s2, float h = 24f)
        where T : struct, INumber<T>
    {
        using (paper.Row(id).Height(UnitValue.Auto).RowBetween(6).Enter())
        {
            Cell(paper, $"{id}_0", l0, v0, c0, s0, h);
            Cell(paper, $"{id}_1", l1, v1, c1, s1, h);
            Cell(paper, $"{id}_2", l2, v2, c2, s2, h);
        }
    }

    internal static void Draw4<T>(Paper paper, string id, OrigamiTheme theme,
        string l0, T v0, SysColor c0, Action<T> s0,
        string l1, T v1, SysColor c1, Action<T> s1,
        string l2, T v2, SysColor c2, Action<T> s2,
        string l3, T v3, SysColor c3, Action<T> s3, float h = 24f)
        where T : struct, INumber<T>
    {
        using (paper.Row(id).Height(UnitValue.Auto).RowBetween(6).Enter())
        {
            Cell(paper, $"{id}_0", l0, v0, c0, s0, h);
            Cell(paper, $"{id}_1", l1, v1, c1, s1, h);
            Cell(paper, $"{id}_2", l2, v2, c2, s2, h);
            Cell(paper, $"{id}_3", l3, v3, c3, s3, h);
        }
    }
}

// ════════════════════════════════════════════════════════════════
//  2-component builders
// ════════════════════════════════════════════════════════════════

public sealed class VectorField2Builder<T> where T : struct, INumber<T>
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly T _x, _y;
    private readonly Action<T> _setX, _setY;

    private float _h = 24f;

    internal VectorField2Builder(Paper paper, string id, OrigamiTheme theme,
        T x, T y, Action<T> setX, Action<T> setY)
    {
        _paper = paper; _id = id; _theme = theme; _h = _theme.Metrics.RowHeight;
        _x = x; _y = y; _setX = setX; _setY = setY;
    }

    /// <summary>Height of each component cell (default 30).</summary>
    public VectorField2Builder<T> Height(float h) { _h = MathF.Max(18f, h); return this; }

    public void Show() => VectorFieldInternal.Draw2(_paper, _id, _theme,
        "X", _x, VectorFieldInternal.XColor, _setX,
        "Y", _y, VectorFieldInternal.YColor, _setY, _h);
}

// ════════════════════════════════════════════════════════════════
//  3-component builders
// ════════════════════════════════════════════════════════════════

public sealed class VectorField3Builder<T> where T : struct, INumber<T>
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly T _x, _y, _z;
    private readonly Action<T> _setX, _setY, _setZ;

    private float _h = 24f;

    internal VectorField3Builder(Paper paper, string id, OrigamiTheme theme,
        T x, T y, T z, Action<T> setX, Action<T> setY, Action<T> setZ)
    {
        _paper = paper; _id = id; _theme = theme; _h = _theme.Metrics.RowHeight;
        _x = x; _y = y; _z = z; _setX = setX; _setY = setY; _setZ = setZ;
    }

    /// <summary>Height of each component cell (default 30).</summary>
    public VectorField3Builder<T> Height(float h) { _h = MathF.Max(18f, h); return this; }

    public void Show() => VectorFieldInternal.Draw3(_paper, _id, _theme,
        "X", _x, VectorFieldInternal.XColor, _setX,
        "Y", _y, VectorFieldInternal.YColor, _setY,
        "Z", _z, VectorFieldInternal.ZColor, _setZ, _h);
}

// ════════════════════════════════════════════════════════════════
//  4-component builders
// ════════════════════════════════════════════════════════════════

public sealed class VectorField4Builder<T> where T : struct, INumber<T>
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly T _x, _y, _z, _w;
    private readonly Action<T> _setX, _setY, _setZ, _setW;

    private float _h = 24f;

    internal VectorField4Builder(Paper paper, string id, OrigamiTheme theme,
        T x, T y, T z, T w, Action<T> setX, Action<T> setY, Action<T> setZ, Action<T> setW)
    {
        _paper = paper; _id = id; _theme = theme; _h = _theme.Metrics.RowHeight;
        _x = x; _y = y; _z = z; _w = w;
        _setX = setX; _setY = setY; _setZ = setZ; _setW = setW;
    }

    /// <summary>Height of each component cell (default 30).</summary>
    public VectorField4Builder<T> Height(float h) { _h = MathF.Max(18f, h); return this; }

    public void Show() => VectorFieldInternal.Draw4(_paper, _id, _theme,
        "X", _x, VectorFieldInternal.XColor, _setX,
        "Y", _y, VectorFieldInternal.YColor, _setY,
        "Z", _z, VectorFieldInternal.ZColor, _setZ,
        "W", _w, VectorFieldInternal.WColor, _setW, _h);
}
