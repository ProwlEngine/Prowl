// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Scribe;

/// <summary>
/// How one character is to be laid out. It arrives filled in from the layout settings, and a
/// <see cref="GlyphCustomizer"/> may change any of it. Whatever comes back is what Scribe shapes,
/// kerns, measures, wraps and places with, so a character given a different font or size is a
/// first-class part of the layout rather than something stretched afterwards.
///
/// This is the layout-time hook. Its draw-time counterpart is <see cref="GlyphModifier"/>, which
/// moves and recolours the finished quad without disturbing where anything sits.
/// </summary>
public struct GlyphStyle
{
    /// <summary>Index of this character in the string being laid out.</summary>
    public readonly int CharIndex;

    /// <summary>The character to lay out, already combined from a surrogate pair where there was
    /// one. Replace it and Scribe shapes, measures and draws the replacement, which is all a
    /// password field masked with asterisks needs. <see cref="CharIndex"/> still points at the real
    /// character, so hit testing and selection keep working on the text as typed.</summary>
    public int Codepoint;

    /// <summary>Null falls back to the layout's own font, which then searches the fallbacks.</summary>
    public FontFile Font;

    /// <summary>Zero or less falls back to the layout's pixel size.</summary>
    public float PixelSize;

    /// <summary>Extra width after this character. Ignored for spaces, which use WordSpacing.</summary>
    public float LetterSpacing;

    /// <summary>Extra width after this character when it is a space.</summary>
    public float WordSpacing;

    public FontQuality Quality;

    public GlyphStyle(int charIndex, int codepoint, FontFile font, float pixelSize,
                      float letterSpacing, float wordSpacing, FontQuality quality)
    {
        CharIndex = charIndex;
        Codepoint = codepoint;
        Font = font;
        PixelSize = pixelSize;
        LetterSpacing = letterSpacing;
        WordSpacing = wordSpacing;
        Quality = quality;
    }

    /// <summary>Everything that decides whether two characters can be shaped as one run.</summary>
    internal readonly bool ShapesWith(in GlyphStyle other) =>
        ReferenceEquals(Font, other.Font)
        && PixelSize == other.PixelSize
        && LetterSpacing == other.LetterSpacing
        && Quality == other.Quality;
}

/// <summary>
/// Adjusts how a character is laid out. Called once per character per layout, so it must be cheap,
/// must not allocate, and must return the same answer for the same character.
/// </summary>
public delegate void GlyphCustomizer(ref GlyphStyle style);
