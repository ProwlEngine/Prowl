// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Prowl.Runtime.GUI.Graphics;

namespace Prowl.Runtime.GUI;

public class GuiDraw2D
{
    public UIDrawList DrawList => _drawList[currentZIndex];

    internal readonly Dictionary<int, UIDrawList> _drawList = new();

    private int currentZIndex => _gui.CurrentZIndex;

    private readonly Gui _gui;
    private bool _AntiAliasing;
    readonly List<UIDrawList> drawListsOrdered = new();

    public GuiDraw2D(Gui gui, bool antiAliasing)
    {
        _gui = gui;
        _AntiAliasing = antiAliasing;
        _drawList[0] = new UIDrawList(_AntiAliasing); // Root Draw List
    }

    public void BeginFrame(bool antiAliasing)
    {
        if (!_drawList.ContainsKey(0))
            _drawList[0] = new UIDrawList(_AntiAliasing); // Root Draw List

        _AntiAliasing = antiAliasing;

        drawListsOrdered.Clear();
        foreach (var index in _drawList.Keys.OrderBy(x => x))
        {
            _drawList[index].AntiAliasing(antiAliasing);
            _drawList[index].Clear();
            _drawList[index].PushTexture(Font.DefaultFont.Texture);

            drawListsOrdered.Add(_drawList[index]);
        }
    }

    public void EndFrame(Veldrid.CommandList commandList, Rect screenRect)
    {
        UIDrawListRenderer.Draw(commandList, drawListsOrdered.ToArray(), new(screenRect.width, screenRect.height), _gui.UIScale);
    }

    /// <summary>
    /// Push a Clipping/Scissoring Rect
    /// This will Intersect with the last active clip rect
    /// All drawing operations applied after this will be cut off by the clip rect and only draw inside it
    /// </summary>
    /// <param name="overwrite">Overwrite all current clip rects instead of using Intersection</param>
    public void PushClip(Rect rect, bool overwrite = false) =>
        _drawList[currentZIndex].PushClipRect(new(rect.x, rect.y, rect.x + rect.width, rect.y + rect.height), overwrite);

    /// <summary> Pop the last clip rect </summary>
    public void PopClip() =>
        _drawList[currentZIndex].PopClipRect();

    /// <summary> Peek at the current clip rect </summary>
    public Rect PeekClip()
    {
        Vector4 clip = _drawList[currentZIndex].PeekClipRect();
        return new(clip.x, clip.y, clip.z - clip.x, clip.w - clip.y);
    }

    public void SetZIndex(int index, bool keepClipSpace)
    {
        if (!_drawList.ContainsKey(index))
        {
            _drawList[index] = new UIDrawList(_AntiAliasing);
        }

        // Copy over the clip rect from the previous list
        if (keepClipSpace)
        {
            var previousList = _drawList[currentZIndex];
            _drawList[index].PushClipRect(previousList.PeekClipRect());
            _drawList[index].PushTexture(Font.DefaultFont.Texture);
        }
    }

    public void DrawVerticalGradient(Vector2 top, Vector2 bottom, float right, Color a, Color b)
    {
        _drawList[currentZIndex].AddRectFilledMultiColor(new(top.x, top.y), new(bottom.x + right, bottom.y), a, b, b, a);
    }

    public void DrawHorizontalGradient(Vector2 left, Vector2 right, float down, Color a, Color b)
    {
        _drawList[currentZIndex].AddRectFilledMultiColor(new(left.x, left.y), new(right.x, right.y + down), b, b, a, a);
    }

    public void DrawVerticalBlackGradient(Vector2 top, Vector2 bottom, float right, float strength)
    {
        Color col = new Color(0, 0, 0, strength);
        _drawList[currentZIndex].AddRectFilledMultiColor(new(top.x, top.y), new(bottom.x + right, bottom.y), col, Color.clear, Color.clear, col);
    }

    public void DrawHorizontalBlackGradient(Vector2 left, Vector2 right, float down, float strength)
    {
        Color col = new Color(0, 0, 0, strength);
        _drawList[currentZIndex].AddRectFilledMultiColor(new(left.x, left.y), new(right.x, right.y + down), Color.clear, Color.clear, col, col);
    }

    public void DrawRect(Rect screenRect, Color color, float thickness = 1f, float roundness = 0.0f, int corners = CornerRounding.All)
        => DrawRect(new(screenRect.x, screenRect.y), new(screenRect.width, screenRect.height), color, thickness, roundness, corners);
    public void DrawRect(Vector2 topleft, Vector2 size, Color color, float thickness = 1f, float roundness = 0.0f, int corners = CornerRounding.All)
        => _drawList[currentZIndex].AddRect(topleft, topleft + size, color, roundness, corners, thickness);
    public void DrawRectFilled(Rect screenRect, Color color, float roundness = 0.0f, int corners = CornerRounding.All)
        => DrawRectFilled(new(screenRect.x, screenRect.y), new(screenRect.width, screenRect.height), color, roundness, corners);
    public void DrawRectFilled(Vector2 topleft, Vector2 size, Color color, float roundness = 0.0f, int corners = CornerRounding.All)
        => _drawList[currentZIndex].AddRectFilled(topleft, topleft + size, color, roundness, corners);
    public void DrawLine(Vector2 start, Vector2 end, Color color, float thickness = 1f)
        => _drawList[currentZIndex].AddLine(start, end, color, thickness);
    public void DrawBezierLine(Vector2 start, Vector2 startControl, Vector2 end, Vector2 endControl, Color color, float thickness = 1f, int segments = 0)
        => _drawList[currentZIndex].AddBezierCurve(start, startControl, endControl, end, color, thickness, segments);
    public void DrawCircle(Vector2 center, float radius, Color color, int segments = 12, float thickness = 1f)
        => _drawList[currentZIndex].AddCircle(center, radius, color, segments, thickness);
    public void DrawCircleFilled(Vector2 center, float radius, Color color, int segments = 12)
        => _drawList[currentZIndex].AddCircleFilled(center, radius, color, segments);
    public void DrawTriangle(Vector2 a, Vector2 b, Vector2 c, Color color, float thickness = 1f)
        => _drawList[currentZIndex].AddTriangle(a, b, c, color, thickness);
    public void DrawTriangleFilled(Vector2 a, Vector2 b, Vector2 c, Color color)
        => _drawList[currentZIndex].AddTriangleFilled(a, b, c, color);
    public void DrawTriangle(Vector2 center, Vector2 dir, float width, Color color, float thickness = 1f)
    {
        Vector2 offset = -dir * width;
        width *= 1.5f;
        Vector2 normalizedDir = Vector2.Normalize(dir);
        Vector2 right = new Vector2(-normalizedDir.y, normalizedDir.x);
        Vector2 a = center + normalizedDir * width * 1.75f;
        Vector2 b = center + right * width;
        Vector2 c = center - right * width;
        _drawList[currentZIndex].AddTriangle(a + offset, b + offset, c + offset, color, thickness);
    }
    public void DrawTriangleFilled(Vector2 center, Vector2 dir, float width, Color color)
    {
        Vector2 offset = -dir * width;
        width *= 1.5f;
        Vector2 normalizedDir = Vector2.Normalize(dir);
        Vector2 right = new Vector2(-normalizedDir.y, normalizedDir.x);
        Vector2 a = center + normalizedDir * width * 1.75f;
        Vector2 b = center + right * width;
        Vector2 c = center - right * width;
        _drawList[currentZIndex].AddTriangleFilled(a + offset, b + offset, c + offset, color);
    }


    #region DrawImage

    public void DrawImage(Texture2D texture, Rect src, Rect dest, bool keepAspect = false)
        => DrawImage(texture, dest.Position, dest.Size, src.Position, src.Size, Color.white, keepAspect);
    public void DrawImage(Texture2D texture, Rect rect, bool keepAspect = false)
        => DrawImage(texture, rect.Position, rect.Size, new(0, 1), new(1, 0), Color.white, keepAspect);
    public void DrawImage(Texture2D texture, Rect rect, Color color, bool keepAspect = false)
        => DrawImage(texture, rect.Position, rect.Size, new(0, 1), new(1, 0), color, keepAspect);
    public void DrawImage(Texture2D texture, Vector2 position, Vector2 size, bool keepAspect = false)
        => DrawImage(texture, position, size, new(0, 1), new(1, 0), Color.white, keepAspect);
    public void DrawImage(Texture2D texture, Vector2 position, Vector2 size, Color color, bool keepAspect = false)
        => DrawImage(texture, position, size, new(0, 1), new(1, 0), color, keepAspect);

    public void DrawImage(Texture2D texture, Vector2 position, Vector2 size, Vector2 uv0, Vector2 uv1, Color color, bool keepAspect = false)
    {
        if (keepAspect)
        {
            double aspectRatio = (double)texture.Width / texture.Height;
            double rectAspectRatio = size.x / size.y;

            if (aspectRatio < rectAspectRatio)
            {
                // Fit height, adjust width
                double adjustedWidth = size.y * aspectRatio;
                double widthDiff = (size.x - adjustedWidth) / 2.0;
                position.x += widthDiff;
                size.x = adjustedWidth;
            }
            else
            {
                // Fit width, adjust height
                double adjustedHeight = size.x / aspectRatio;
                double heightDiff = (size.y - adjustedHeight) / 2.0;
                position.y += heightDiff;
                size.y = adjustedHeight;
            }
        }

        _drawList[currentZIndex].AddImage(texture, position, position + size, uv0, uv1, color);
    }

    #endregion


    public void DrawText(string text, Rect rect, Color color, bool dowrap = true, bool doclip = true)
        => DrawText(Font.DefaultFont, text, 20, rect, color, dowrap, doclip);
    public void DrawText(string text, Rect rect, bool dowrap = true, bool doclip = true)
        => DrawText(Font.DefaultFont, text, 20, rect, Color.white, dowrap, doclip);

    public void DrawText(string text, double fontSize, Rect rect, bool dowrap = true, bool doclip = true)
        => DrawText(Font.DefaultFont, text, fontSize, rect, Color.white, dowrap, doclip);

    public void DrawText(string text, double fontSize, Rect rect, Color color, bool dowrap = true, bool doclip = true)
        => DrawText(Font.DefaultFont, text, fontSize, rect, color, dowrap, doclip);

    public void DrawText(Font font, string text, double fontSize, Rect rect, Color color, bool dowrap = true, bool doclip = true)
    {
        var pos = new Vector2(rect.x, rect.y);
        var wrap = rect.width;
        var textSize = font.CalcTextSize(text, fontSize, 0, (float)(dowrap ? wrap : -1));
        pos.x += MathD.Max((rect.width - textSize.x) * 0.5f, 0.0);
        pos.y += (rect.height - (textSize.y * 0.75f)) * 0.5f;
        DrawText(font, text, fontSize, pos, color, dowrap ? wrap : 0, doclip ? rect : null);
    }

    public void DrawText(string text, Vector2 position, double wrapwidth = 0.0f)
        => DrawText(Font.DefaultFont, text, 20, position, Color.white, wrapwidth);

    public void DrawText(string text, Vector2 position, Color color, double wrapwidth = 0.0f)
        => DrawText(Font.DefaultFont, text, 20, position, color, wrapwidth);

    public void DrawText(string text, double fontSize, Vector2 position, double wrapwidth = 0.0f)
        => DrawText(Font.DefaultFont, text, fontSize, position, Color.white, wrapwidth);

    public void DrawText(string text, double fontSize, Vector2 position, Color color, double wrapwidth = 0.0f)
        => _drawList[currentZIndex].AddText((float)fontSize, position, color, text, wrap_width: (float)wrapwidth);

    public void DrawText(Font font, string text, double fontSize, Vector2 position, Color color, double wrapwidth = 0.0f, Rect? clip = null)
    {
        _drawList[currentZIndex].PushTexture(font.Texture);

        if (clip != null)
            _drawList[currentZIndex].AddText(font, (float)fontSize, position, color, text, wrap_width: (float)wrapwidth, cpu_fine_clip_rect: new Vector4(clip.Value.Position, clip.Value.Position + clip.Value.Size));
        else
            _drawList[currentZIndex].AddText(font, (float)fontSize, position, color, text, wrap_width: (float)wrapwidth);
        _drawList[currentZIndex].PopTexture();
    }

    public void LoadingIndicatorCircle(Vector2 center, double radius, Vector4 mainColor, Vector4 backdropColor, int circleCount, float speed)
    {
        double circleRadius = radius / 15.0f;
        double updatedIndicatorRadius = radius - 4.0f * circleRadius;

        double t = Time.time;
        double degreeOffset = 2.0f * MathD.PI / circleCount;

        for (int i = 0; i < circleCount; ++i)
        {
            double x = updatedIndicatorRadius * MathD.Sin(degreeOffset * i);
            double y = updatedIndicatorRadius * MathD.Cos(degreeOffset * i);
            double growth = MathD.Max(0.0f, MathD.Sin(t * speed * 8 - i * degreeOffset));

            Vector4 color = new Vector4
            {
                x = mainColor.x * growth + backdropColor.x * (1.0f - growth),
                y = mainColor.y * growth + backdropColor.y * (1.0f - growth),
                z = mainColor.z * growth + backdropColor.z * (1.0f - growth),
                w = 1.0f
            };

            _drawList[currentZIndex].AddCircleFilled(
                new Vector2(center.x + x, center.y - y),
                (float)(circleRadius + growth * circleRadius),
                (Color)color
            );
        }
    }

}
