using Prowl.Runtime.GUI.Graphics;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Runtime.GUI
{
    public class GuiDraw2D
    {
        public UIDrawList DrawList => _drawList[currentZIndex];

        internal Dictionary<int, UIDrawList> _drawList = new();

        private int currentZIndex => _gui.CurrentZIndex;

        private Gui _gui;
        List<UIDrawList> drawListsOrdered = new();

        public GuiDraw2D(Gui gui)
        {
            _gui = gui;
            _drawList[0] = new UIDrawList(); // Root Draw List
        }

        public void BeginFrame()
        {
            if (!_drawList.ContainsKey(0))
                _drawList[0] = new UIDrawList(); // Root Draw List

            drawListsOrdered.Clear();
            foreach (var index in _drawList.Keys.OrderBy(x => x))
            {
                _drawList[index].Clear();
                _drawList[index].PushTextureID(UIDrawList.DefaultFont.Texture.Handle);
                drawListsOrdered.Add(_drawList[index]);
            }
        }

        public void EndFrame(Rect screenRect)
        {
            //UIDrawList.Draw(GLDevice.GL, new(screenRect.width, screenRect.height), drawListsOrdered.ToArray());
        }

        /// <summary>
        /// Push a Clipping/Scissoring Rect
        /// This will Intersect with the last active clip rect
        /// All drawing operations applied after this will be cut off by the clip rect and only draw inside it
        /// </summary>
        /// <param name="overwrite">Overwrite all current clip rects instead of using Intersection</param>
        public void PushClip(Rect rect, bool overwrite = false)
            => _drawList[currentZIndex].PushClipRect(new(rect.x, rect.y, rect.x + rect.width, rect.y + rect.height), overwrite);

        /// <summary> Pop the last clip rect </summary>
        public void PopClip() => _drawList[currentZIndex].PopClipRect();

        /// <summary> Peek at the current clip rect </summary>
        public Rect PeekClip()
        {
            var clip = _drawList[currentZIndex]._ClipRectStack.Peek();
            return new(clip.x, clip.y, (clip.z - clip.x), (clip.w - clip.y));
        }

        public void SetZIndex(int index, bool keepClipSpace)
        {
            if (!_drawList.ContainsKey(index))
            {
                _drawList[index] = new UIDrawList();
                _drawList[index].PushTextureID(UIDrawList.DefaultFont.Texture.Handle);
            }

            // Copy over the clip rect from the previous list
            if (keepClipSpace)
            {
                var previousList = _drawList[currentZIndex];
                _drawList[index].PushClipRect(previousList._ClipRectStack.Peek());
            }
        }

        public void DrawVerticalBlackGradient(Vector2 top, Vector2 bottom, float right, float strength)
        {
            uint col = new Color(0, 0, 0, strength).GetUInt();
            _drawList[currentZIndex].AddRectFilledMultiColor(new(top.x, top.y), new(bottom.x + right, bottom.y), col, Color.clear.GetUInt(), Color.clear.GetUInt(), col);
        }

        public void DrawHorizontalBlackGradient(Vector2 left, Vector2 right, float down, float strength)
        {
            uint col = new Color(0, 0, 0, strength).GetUInt();
            _drawList[currentZIndex].AddRectFilledMultiColor(new(left.x, left.y), new(right.x, right.y + down), Color.clear.GetUInt(), Color.clear.GetUInt(), col, col);
        }

        public void DrawRect(Rect screenRect, Color color, float thickness = 1f, float roundness = 0.0f, int rounded_corners = 15)
            => DrawRect(new(screenRect.x, screenRect.y), new(screenRect.width, screenRect.height), color, thickness, roundness, rounded_corners);
        public void DrawRect(Vector2 topleft, Vector2 size, Color color, float thickness = 1f, float roundness = 0.0f, int rounded_corners = 15)
            => _drawList[currentZIndex].AddRect(topleft, topleft + size, color.GetUInt(), roundness, rounded_corners, thickness);
        public void DrawRectFilled(Rect screenRect, Color color, float roundness = 0.0f, int rounded_corners = 15)
            => DrawRectFilled(new(screenRect.x, screenRect.y), new(screenRect.width, screenRect.height), color, roundness, rounded_corners);
        public void DrawRectFilled(Vector2 topleft, Vector2 size, Color color, float roundness = 0.0f, int rounded_corners = 15)
            => _drawList[currentZIndex].AddRectFilled(topleft, topleft + size, color.GetUInt(), roundness, rounded_corners);
        public void DrawLine(Vector2 start, Vector2 end, Color color, float thickness = 1f)
            => _drawList[currentZIndex].AddLine(start, end, color.GetUInt(), thickness);
        public void DrawCircle(Vector2 center, float radius, Color color, int segments = 12, float thickness = 1f)
            => _drawList[currentZIndex].AddCircle(center, radius, color.GetUInt(), segments, thickness);
        public void DrawCircleFilled(Vector2 center, float radius, Color color, int segments = 12)
            => _drawList[currentZIndex].AddCircleFilled(center, radius, color.GetUInt(), segments);
        public void DrawTriangle(Vector2 a, Vector2 b, Vector2 c, Color color, float thickness = 1f)
            => _drawList[currentZIndex].AddTriangle(a, b, c, color.GetUInt(), thickness);
        public void DrawTriangleFilled(Vector2 a, Vector2 b, Vector2 c, Color color)
            => _drawList[currentZIndex].AddTriangleFilled(a, b, c, color.GetUInt());


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
            _drawList[currentZIndex].AddImage(texture.Handle, position, position + size, uv0, uv1, color.GetUInt());
        }

        #endregion


        public void DrawText(string text, Rect rect, Color color, bool dowrap = true, bool doclip = true)
            => DrawText(UIDrawList.DefaultFont, text, 20, rect, color, dowrap, doclip);
        public void DrawText(string text, Rect rect, bool dowrap = true, bool doclip = true)
            => DrawText(UIDrawList.DefaultFont, text, 20, rect, Color.white, dowrap, doclip);

        public void DrawText(string text, double fontSize, Rect rect, bool dowrap = true, bool doclip = true)
            => DrawText(UIDrawList.DefaultFont, text, fontSize, rect, Color.white, dowrap, doclip);

        public void DrawText(string text, double fontSize, Rect rect, Color color, bool dowrap = true, bool doclip = true)
            => DrawText(UIDrawList.DefaultFont, text, fontSize, rect, color, dowrap, doclip);

        public void DrawText(Font font, string text, double fontSize, Rect rect, Color color, bool dowrap = true, bool doclip = true)
        {
            var pos = new Vector2(rect.x, rect.y);
            var wrap = rect.width;
            var textSize = font.CalcTextSize(text, fontSize, 0, (float)(dowrap ? wrap : -1));
            pos.x += Mathf.Max((rect.width - textSize.x) * 0.5f, 0.0);
            pos.y += (rect.height - (textSize.y * 0.75f)) * 0.5f;
            DrawText(font, text, fontSize, pos, color, dowrap ? wrap : 0, doclip ? rect : null);
        }

        public void DrawText(string text, Vector2 position, double wrapwidth = 0.0f)
            => DrawText(UIDrawList.DefaultFont, text, 20, position, Color.white, wrapwidth);

        public void DrawText(string text, Vector2 position, Color color, double wrapwidth = 0.0f)
            => DrawText(UIDrawList.DefaultFont, text, 20, position, color, wrapwidth);

        public void DrawText(string text, double fontSize, Vector2 position, double wrapwidth = 0.0f)
            => DrawText(UIDrawList.DefaultFont, text, fontSize, position, Color.white, wrapwidth);

        public void DrawText(string text, double fontSize, Vector2 position, Color color, double wrapwidth = 0.0f)
            => _drawList[currentZIndex].AddText((float)fontSize, position, color.GetUInt(), text, wrap_width: (float)wrapwidth);

        public void DrawText(Font font, string text, double fontSize, Vector2 position, Color color, double wrapwidth = 0.0f, Rect? clip = null)
        {
            _drawList[currentZIndex].PushTextureID(font.Texture.Handle);
            if (clip != null)
                _drawList[currentZIndex].AddText(font, (float)fontSize, position, color.GetUInt(), text, wrap_width: (float)wrapwidth, cpu_fine_clip_rect: new Vector4(clip.Value.Position, clip.Value.Position + clip.Value.Size));
            else
                _drawList[currentZIndex].AddText(font, (float)fontSize, position, color.GetUInt(), text, wrap_width: (float)wrapwidth);
            _drawList[currentZIndex].PopTextureID();
        }
    }

}