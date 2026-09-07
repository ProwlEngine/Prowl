// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Scribe;
using Prowl.Vector;

namespace Tests;

public class RichTextDecorationTests
{
    /// <summary>Keeps every quad handed to it, so where things ended up can be asserted.</summary>
    private sealed class CapturingRenderer : IFontRenderer
    {
        public readonly List<(float Y0, float Y1, float X0, float X1)> Quads = [];

        public object CreateTexture(int width, int height) => new byte[width * height];
        public void UpdateTextureRegion(object texture, AtlasRect bounds, byte[] data) { }

        public void DrawQuads(object texture, ReadOnlySpan<IFontRenderer.Vertex> vertices, ReadOnlySpan<int> indices)
        {
            for (int q = 0; q + 3 < vertices.Length; q += 4)
                Quads.Add((vertices[q].Position.Y, vertices[q + 3].Position.Y,
                           vertices[q].Position.X, vertices[q + 3].Position.X));
        }
    }

    private static List<(float Y0, float Y1, float X0, float X1)> Draw(float lineHeight)
    {
        var renderer = new CapturingRenderer();
        var fs = new FontSystem(renderer);
        foreach (var f in fs.EnumerateSystemFonts())
        {
            if (f.Style != FontStyle.Regular) continue;
            fs.AddFallbackFont(f);
            break;
        }

        var settings = RichTextLayoutSettings.Default;
        settings.RegularFont = fs.FallbackFonts.First();
        settings.PixelSize = 32f;
        settings.LineHeight = lineHeight;

        var rt = new RichTextLayout("<u>Underlined</u>", settings);
        rt.Update(fs);
        rt.Draw(fs, renderer, Float2.Zero, 0.0);

        return renderer.Quads;
    }

    /// <summary>
    /// Half of a line's leading sits above the text, so raising the line height moves the glyphs
    /// down. An underline is drawn from the same baseline and has to move with them; computing it
    /// from the ascent alone leaves it floating above the text it belongs to.
    /// </summary>
    [Fact]
    public void AnUnderlineMovesWithTheTextWhenTheLineHeightGrows()
    {
        var tight = Draw(1.0f);
        var loose = Draw(2.0f);

        Assert.NotEmpty(tight);
        Assert.Equal(tight.Count, loose.Count);

        // The underline is the widest, flattest quad on the line.
        static int Decoration(List<(float Y0, float Y1, float X0, float X1)> quads)
        {
            int best = 0;
            float bestRatio = 0f;
            for (int i = 0; i < quads.Count; i++)
            {
                float h = quads[i].Y1 - quads[i].Y0, w = quads[i].X1 - quads[i].X0;
                if (h <= 0f) continue;
                float ratio = w / h;
                if (ratio > bestRatio) { bestRatio = ratio; best = i; }
            }
            return best;
        }

        int index = Decoration(tight);
        float decorationShift = loose[index].Y0 - tight[index].Y0;

        // Every glyph shifted by the half-leading; the underline has to have shifted by the same.
        float[] glyphShifts = tight
            .Select((q, i) => loose[i].Y0 - q.Y0)
            .Where((_, i) => i != index)
            .ToArray();

        Assert.NotEmpty(glyphShifts);
        float expected = glyphShifts[0];
        Assert.True(expected > 0.5f, $"the text did not move down at all (shift={expected})");

        foreach (float shift in glyphShifts)
            Assert.Equal(expected, shift, 2);

        Assert.Equal(expected, decorationShift, 2);
    }
}
