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
    internal static readonly SysColor XColor = SysColor.FromArgb(255, 200, 80, 80);
    internal static readonly SysColor YColor = SysColor.FromArgb(255, 80, 200, 80);
    internal static readonly SysColor ZColor = SysColor.FromArgb(255, 80, 80, 200);
    internal static readonly SysColor WColor = SysColor.FromArgb(255, 180, 180, 180);

    internal static void Draw2<T>(Paper paper, string id, OrigamiTheme theme,
        string l0, T v0, SysColor c0, Action<T> s0,
        string l1, T v1, SysColor c1, Action<T> s1)
        where T : struct, INumber<T>
    {
        using (paper.Row(id).Height(UnitValue.Auto).RowBetween(4).Enter())
        {
            Origami.NumericField<T>(paper, $"{id}_0", v0, s0).Prefix(l0, c0).Width(UnitValue.Stretch()).Show();
            Origami.NumericField<T>(paper, $"{id}_1", v1, s1).Prefix(l1, c1).Width(UnitValue.Stretch()).Show();
        }
    }

    internal static void Draw3<T>(Paper paper, string id, OrigamiTheme theme,
        string l0, T v0, SysColor c0, Action<T> s0,
        string l1, T v1, SysColor c1, Action<T> s1,
        string l2, T v2, SysColor c2, Action<T> s2)
        where T : struct, INumber<T>
    {
        using (paper.Row(id).Height(UnitValue.Auto).RowBetween(4).Enter())
        {
            Origami.NumericField<T>(paper, $"{id}_0", v0, s0).Prefix(l0, c0).Width(UnitValue.Stretch()).Show();
            Origami.NumericField<T>(paper, $"{id}_1", v1, s1).Prefix(l1, c1).Width(UnitValue.Stretch()).Show();
            Origami.NumericField<T>(paper, $"{id}_2", v2, s2).Prefix(l2, c2).Width(UnitValue.Stretch()).Show();
        }
    }

    internal static void Draw4<T>(Paper paper, string id, OrigamiTheme theme,
        string l0, T v0, SysColor c0, Action<T> s0,
        string l1, T v1, SysColor c1, Action<T> s1,
        string l2, T v2, SysColor c2, Action<T> s2,
        string l3, T v3, SysColor c3, Action<T> s3)
        where T : struct, INumber<T>
    {
        using (paper.Row(id).Height(UnitValue.Auto).RowBetween(4).Enter())
        {
            Origami.NumericField<T>(paper, $"{id}_0", v0, s0).Prefix(l0, c0).Width(UnitValue.Stretch()).Show();
            Origami.NumericField<T>(paper, $"{id}_1", v1, s1).Prefix(l1, c1).Width(UnitValue.Stretch()).Show();
            Origami.NumericField<T>(paper, $"{id}_2", v2, s2).Prefix(l2, c2).Width(UnitValue.Stretch()).Show();
            Origami.NumericField<T>(paper, $"{id}_3", v3, s3).Prefix(l3, c3).Width(UnitValue.Stretch()).Show();
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

    internal VectorField2Builder(Paper paper, string id, OrigamiTheme theme,
        T x, T y, Action<T> setX, Action<T> setY)
    {
        _paper = paper; _id = id; _theme = theme;
        _x = x; _y = y; _setX = setX; _setY = setY;
    }

    public void Show() => VectorFieldInternal.Draw2(_paper, _id, _theme,
        "X", _x, VectorFieldInternal.XColor, _setX,
        "Y", _y, VectorFieldInternal.YColor, _setY);
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

    internal VectorField3Builder(Paper paper, string id, OrigamiTheme theme,
        T x, T y, T z, Action<T> setX, Action<T> setY, Action<T> setZ)
    {
        _paper = paper; _id = id; _theme = theme;
        _x = x; _y = y; _z = z; _setX = setX; _setY = setY; _setZ = setZ;
    }

    public void Show() => VectorFieldInternal.Draw3(_paper, _id, _theme,
        "X", _x, VectorFieldInternal.XColor, _setX,
        "Y", _y, VectorFieldInternal.YColor, _setY,
        "Z", _z, VectorFieldInternal.ZColor, _setZ);
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

    internal VectorField4Builder(Paper paper, string id, OrigamiTheme theme,
        T x, T y, T z, T w, Action<T> setX, Action<T> setY, Action<T> setZ, Action<T> setW)
    {
        _paper = paper; _id = id; _theme = theme;
        _x = x; _y = y; _z = z; _w = w;
        _setX = setX; _setY = setY; _setZ = setZ; _setW = setW;
    }

    public void Show() => VectorFieldInternal.Draw4(_paper, _id, _theme,
        "X", _x, VectorFieldInternal.XColor, _setX,
        "Y", _y, VectorFieldInternal.YColor, _setY,
        "Z", _z, VectorFieldInternal.ZColor, _setZ,
        "W", _w, VectorFieldInternal.WColor, _setW);
}
