// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Shared UI helpers for the PBR Forge tool. Kept separate from the engine file
/// (<see cref="PBRTextureForge"/>) so the generator is usable without the editor UI.
/// </summary>
internal static class PBRForgeUtils
{
    /// <summary>
    /// Attach a PostLayout draw callback to <paramref name="element"/> that renders
    /// <paramref name="texture"/> centered and aspect-fit inside the element with
    /// correct vertical orientation.
    /// </summary>
    /// <remarks>
    /// Prowl's source textures are stored bottom-up (Texture2D.FromImage calls Flip()
    /// before upload) and so are our generated textures — the negative Y scale in the
    /// brush transform flips them right-side up for display. Without this flip,
    /// <c>canvas.DrawImage</c> would render everything upside-down.
    /// </remarks>
    public static ElementBuilder DrawTextureInto(this ElementBuilder element, Paper paper, Texture2D? texture)
    {
        return element.OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
        {
            if (texture == null || !texture.IsValid()) return;

            float maxW = (float)r.Size.X - 6;
            float maxH = (float)r.Size.Y - 6;
            float aspect = (float)texture.Width / MathF.Max(1, (float)texture.Height);
            float drawW = maxW;
            float drawH = drawW / aspect;
            if (drawH > maxH) { drawH = maxH; drawW = drawH * aspect; }
            float drawX = (float)r.Min.X + ((float)r.Size.X - drawW) * 0.5f;
            float drawY = (float)r.Min.Y + ((float)r.Size.Y - drawH) * 0.5f;

            canvas.SetBrushTexture(texture);
            canvas.SetBrushTextureTransform(
                Transform2D.CreateTranslation(drawX, drawY + drawH) *
                Transform2D.CreateScale(drawW, -drawH));
            canvas.RoundedRectFilled(drawX, drawY, drawW, drawH, 4, 4, 4, 4,
                new Color32(255, 255, 255, 255));
            canvas.ClearBrushTexture();
        }));
    }

    /// <summary>Common slot background colour — normal state.</summary>
    public static readonly System.Drawing.Color SlotBg = System.Drawing.Color.FromArgb(255, 32, 32, 36);
    /// <summary>Common slot background colour — highlighted (e.g. drag-hover).</summary>
    public static readonly System.Drawing.Color SlotBgHover = System.Drawing.Color.FromArgb(255, 46, 52, 70);
    /// <summary>Common slot border colour.</summary>
    public static readonly System.Drawing.Color SlotBorder = System.Drawing.Color.FromArgb(255, 60, 60, 66);
    /// <summary>Warning text colour for missing-input labels.</summary>
    public static readonly System.Drawing.Color MissingInputColor = System.Drawing.Color.FromArgb(255, 230, 95, 95);
}
