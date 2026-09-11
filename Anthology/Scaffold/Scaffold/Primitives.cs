using System;

namespace Prowl.Scaffold;
/// <summary>Pixel + percentage (0-100) + intrinsic content, with flex grow/shrink weights.</summary>
public readonly record struct Length(float Px, float Pct = 0, float Grow = 0, float AutoFactor = 0, float Shrink = 0)
{
    public static Length Auto => new(0, AutoFactor: 1);

    public static Length Pixels(float value) => new(value);
    public static Length Percentage(float value, float offset = 0) => new(offset, value);
    public static Length Stretch(float factor = 1) => new(0, Grow: factor);
    public Length Shrinkable(float factor = 1) => this with
    {
        Shrink = factor
    };
    internal float Resolve(float parent, float content) => Px + parent * Pct / 100f + AutoFactor * content;
    public static implicit operator Length(float value) => Pixels(value);
    public static implicit operator Length(int value) => Pixels(value);
    public static Length operator +(Length a, Length b) => new(a.Px + b.Px, a.Pct + b.Pct, a.Grow + b.Grow, a.AutoFactor + b.AutoFactor, a.Shrink + b.Shrink);
    public static Length operator -(Length a, Length b) => new(a.Px - b.Px, a.Pct - b.Pct, a.Grow - b.Grow, a.AutoFactor - b.AutoFactor, a.Shrink - b.Shrink);
    public static Length operator *(Length a, float b) => new(a.Px * b, a.Pct * b, a.Grow * b, a.AutoFactor * b, a.Shrink * b);
    public static Length Lerp(Length a, Length b, float t) => a + (b - a) * Math.Clamp(t, 0, 1);
}

public readonly record struct Size(float Width, float Height);
public readonly record struct Rect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
}

public readonly record struct Edges(float Left, float Top, float Right, float Bottom)
{
    public Edges(float all) : this(all, all, all, all)
    {
    }

    public Edges(float horizontal, float vertical) : this(horizontal, vertical, horizontal, vertical)
    {
    }

    internal float Horizontal => Left + Right;
    internal float Vertical => Top + Bottom;
}

/// <summary>Box edges that each carry a full <see cref="Length"/>, so pixels, percentages and
/// grow weights travel together instead of in parallel fields.</summary>
public readonly record struct LengthEdges(Length Left, Length Top, Length Right, Length Bottom)
{
    public LengthEdges(Length all) : this(all, all, all, all)
    {
    }

    public LengthEdges(Length horizontal, Length vertical) : this(horizontal, vertical, horizontal, vertical)
    {
    }
}

public enum LayoutMode
{
    Column,
    Row,
    Overlay,
    Grid
}

public enum Align
{
    Start,
    Center,
    End,
    Stretch
}

public enum Justify
{
    Start,
    Center,
    End,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly
}

public enum Position
{
    Flow,
    Absolute
}

/// <summary>Immutable value inputs. Use Style.Default, then with-expressions. A default(Style) is a zero-sized box.</summary>
public readonly record struct Style
{
    public Length Width { get; init; }
    public Length Height { get; init; }
    /// <summary>Size bounds in pixels, percent of the parent, or both. Percentages replace the
    /// default rather than adding to it, so MaxWidth = Length.Percentage(50) means exactly half.</summary>
    public Length MinWidth { get; init; }
    public Length MinHeight { get; init; }
    public Length MaxWidth { get; init; }
    public Length MaxHeight { get; init; }
    public LengthEdges Padding { get; init; }
    /// <summary>Edge spacing. A grow weight here makes the edge a flexible spacer that shares
    /// surplus with the element's own grow dimension.</summary>
    public LengthEdges Margin { get; init; }
    public LayoutMode Layout { get; init; }
    public Align AlignItems { get; init; }
    public Align? AlignSelf { get; init; }
    public Justify Justify { get; init; }
    public float Gap { get; init; }
    public float LineGap { get; init; }
    public bool Wrap { get; init; }
    public bool Reverse { get; init; }
    public bool Hidden { get; init; }
    public Position Position { get; init; }
    public float? Left { get; init; }
    public float? Top { get; init; }
    public float? Right { get; init; }
    public float? Bottom { get; init; }
    /// <summary>Width / height. Derives an auto axis from a definite axis. Zero disables.</summary>
    public float AspectRatio { get; init; }
    /// <summary>Number of equal-width grid columns. Rows size to their tallest item.</summary>
    public int GridColumns { get; init; }
    public static Style Default => new()
    {
        Width = Length.Auto,
        Height = Length.Auto,
        MaxWidth = float.MaxValue,
        MaxHeight = float.MaxValue,
        GridColumns = 1
    };
}

/// <summary>Owner- and generation-checked node identity. Default is an invalid handle.</summary>
public readonly record struct NodeId
{
    internal NodeId(long owner, int index, uint generation) => (Owner, Index, Generation) = (owner, index, generation);
    internal long Owner { get; }
    internal int Index { get; }
    internal uint Generation { get; }
}

/// <summary>Measure intrinsic content in the supplied nonnegative finite available space.
/// Register it once and call MarkDirty when its external content changes. Re-registering
/// through SetMeasure always invalidates. Must not mutate the tree.</summary>
public delegate Size MeasureContent(NodeId node, Size available, object? context);
/// <summary>Work-operation counts from the last Layout call. A node measured under multiple
/// constraints contributes multiple measurements. These are not distinct-node counts.</summary>
public readonly record struct LayoutStatistics(int MeasuredNodes, int ArrangedNodes, int MeasureCacheHits, int ArrangeCacheHits);
