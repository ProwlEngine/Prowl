using System;

using Prowl.Vector;

namespace Prowl.PaperUI.RichText;

/// <summary>Animated effects applied to each glyph. Each is a pure function of glyph index and time, so a
/// frame drawn at time t always looks the same.</summary>
public enum RichEffect : byte
{
    None,
    /// <summary>Jitters each glyph independently.</summary>
    Shake,
    /// <summary>Each glyph slides smoothly back and forth along its own axis.</summary>
    Wiggle,
    /// <summary>A vertical wave travelling along the text.</summary>
    Wave,
    /// <summary>The same travelling wave, driving size instead of height.</summary>
    Swell,
    /// <summary>A wave crosses the text now and then, each glyph hopping and bouncing to rest.</summary>
    Bounce,
    /// <summary>Skews every glyph about its centre: the top leans one way, the bottom the other.</summary>
    Slide,
    /// <summary>Hangs each glyph like a flag. The top edge is pinned, the bottom sways.</summary>
    Dangle,
    /// <summary>Swings each glyph from a pivot at its top centre.</summary>
    Pendulum,
    /// <summary>Rocks every glyph about its centre in step.</summary>
    Swing,
    /// <summary>Spins about the glyph centre.</summary>
    Rotate,
    /// <summary>Uniform scale pulse.</summary>
    Pulse,
    /// <summary>Hue cycling along the text.</summary>
    Rainbow
}

[Flags]
public enum RichStyle : byte
{
    None = 0,
    Bold = 1 << 0,
    Italic = 1 << 1,
    Underline = 1 << 2,
    Strike = 1 << 3,
    Mono = 1 << 4
}

/// <summary>A run of visible characters carrying one effect. Indices are into the visible text,
/// which is the source with every tag removed.</summary>
public readonly struct RichEffectSpan
{
    public readonly RichEffect Kind;
    public readonly int Start;
    public readonly int End;
    public readonly float Strength;
    public readonly float Speed;

    public RichEffectSpan(RichEffect kind, int start, int end, float strength, float speed)
    {
        Kind = kind;
        Start = start;
        End = end;
        Strength = strength;
        Speed = speed;
    }

    public bool Covers(int index) => index >= Start && index < End;
}

public readonly struct RichStyleSpan
{
    public readonly RichStyle Style;
    public readonly int Start;
    public readonly int End;
    public RichStyleSpan(RichStyle style, int start, int end) => (Style, Start, End) = (style, start, end);
    public bool Covers(int index) => index >= Start && index < End;
}

public readonly struct RichColorSpan
{
    public readonly Color Color;
    public readonly int Start;
    public readonly int End;
    public RichColorSpan(Color color, int start, int end) => (Color, Start, End) = (color, start, end);
    public bool Covers(int index) => index >= Start && index < End;
}

/// <summary>A relative font size multiplier over a run.</summary>
public readonly struct RichSizeSpan
{
    public readonly float Scale;
    public readonly int Start;
    public readonly int End;
    public RichSizeSpan(float scale, int start, int end) => (Scale, Start, End) = (scale, start, end);
    public bool Covers(int index) => index >= Start && index < End;
}

/// <summary>A clickable run. Paper hit tests these and raises the href on the element.</summary>
public readonly struct RichLinkSpan
{
    public readonly string Href;
    public readonly int Start;
    public readonly int End;
    public RichLinkSpan(string href, int start, int end) => (Href, Start, End) = (href, start, end);
    public bool Covers(int index) => index >= Start && index < End;
}

/// <summary>What a glyph should look like this frame, after every covering effect is composed.
/// Applied to the glyph quad in the order scale, shear, rotate, translate.</summary>
public struct RichGlyphTransform
{
    public float OffsetX;
    public float OffsetY;
    public float ScaleX;
    public float ScaleY;

    /// <summary>Radians, about <see cref="PivotX"/>,<see cref="PivotY"/>.</summary>
    public float Rotation;
    /// <summary>Rotation pivot in the glyph's own space, where 0,0 is the top left and 0.5,0.5 the centre.</summary>
    public float PivotX;
    public float PivotY;

    /// <summary>Horizontal lean, in pixels, across the full height of the glyph. The row at
    /// <see cref="ShearPivotY"/> stays put and every other row slides in proportion, so a pivot of 0
    /// pins the top edge and 0.5 leans the top and bottom opposite ways.</summary>
    public float ShearX;
    public float ShearPivotY;

    public Color Color;

    public static RichGlyphTransform Identity(Color color) => new()
    {
        ScaleX = 1f,
        ScaleY = 1f,
        PivotX = 0.5f,
        PivotY = 0.5f,
        ShearPivotY = 0.5f,
        Color = color
    };

    /// <summary>True when this glyph needs nothing but its plain upright quad.</summary>
    public readonly bool IsIdentity =>
        OffsetX == 0f && OffsetY == 0f && ScaleX == 1f && ScaleY == 1f
        && Rotation == 0f && ShearX == 0f;

    /// <summary>Where a corner of the glyph lands once every channel is applied.
    /// <paramref name="u"/> and <paramref name="v"/> run 0..1 across the quad.</summary>
    public readonly void Apply(float u, float v, float width, float height, out float x, out float y)
    {
        // Scale about the glyph centre so growing text stays put rather than drifting right.
        float localX = (u - 0.5f) * width * ScaleX;
        float localY = (v - 0.5f) * height * ScaleY;

        localX += ShearX * (v - ShearPivotY);

        if (Rotation != 0f)
        {
            float pivotX = (PivotX - 0.5f) * width;
            float pivotY = (PivotY - 0.5f) * height;
            float dx = localX - pivotX;
            float dy = localY - pivotY;
            float sin = MathF.Sin(Rotation);
            float cos = MathF.Cos(Rotation);
            localX = pivotX + dx * cos - dy * sin;
            localY = pivotY + dx * sin + dy * cos;
        }

        x = localX + OffsetX;
        y = localY + OffsetY;
    }
}
