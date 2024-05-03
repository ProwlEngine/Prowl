using Prowl.Runtime.GUI.Graphics;

namespace Prowl.Runtime.GUI
{

    public partial class Gui
    {
        public void PushClip(Rect rect)
        {
            _drawList[CurrentZIndex].PushClipRect(new Vector4(rect.x, rect.y, rect.x + rect.width, rect.y + rect.height));
        }

        public void PopClip()
        {
            _drawList[CurrentZIndex].PopClipRect();
        }

        public void DrawRect(Rect screenRect, Color color, float thickness = 1f, float roundness = 0.0f) => DrawRect(new(screenRect.x, screenRect.y), new(screenRect.width, screenRect.height), color, thickness, roundness);
        public void DrawRect(Vector2 topleft, Vector2 size, Color color, float thickness = 1f, float roundness = 0.0f)
        {
            //if(CurrentNode.DrawList == -1)
            //    CurrentNode.DrawList = CreateDrawList(CurrentNode);

            //var drawlist = drawLists[CurrentNode.DrawList];
            uint col = UIDrawList.ColorConvertFloat4ToU32(color);
            var pos = topleft;
            _drawList[CurrentZIndex].AddRect(pos, pos + size, col, roundness, 15, thickness);
        }

        public void DrawRectFilled(Rect screenRect, Color color, float roundness = 0.0f) => DrawRectFilled(new(screenRect.x, screenRect.y), new(screenRect.width, screenRect.height), color, roundness);
        public void DrawRectFilled(Vector2 topleft, Vector2 size, Color color, float roundness = 0.0f)
        {
            uint col = UIDrawList.ColorConvertFloat4ToU32(color);
            var pos = topleft;
            _drawList[CurrentZIndex].AddRectFilled(pos, pos + size, col, roundness, 15);
        }

        public void DrawVerticalShadow(Vector2 top, Vector2 bottom, float x, float strength)
        {
            uint a = UIDrawList.ColorConvertFloat4ToU32(new Vector4(0,0,0,strength));
            uint b = UIDrawList.ColorConvertFloat4ToU32(new Vector4(0,0,0, strength));
            uint c = UIDrawList.ColorConvertFloat4ToU32(new Vector4(0,0,0,0));
            uint d = UIDrawList.ColorConvertFloat4ToU32(new Vector4(0,0,0,0));
            Vector2 posA = new Vector2(top.x, top.y);
            Vector2 posB = new Vector2(bottom.x + x, bottom.y);
            _drawList[CurrentZIndex].AddRectFilledMultiColor(posA, posB, a, d, c, b);
        }

        public void DrawHorizontalShadow(Vector2 left, Vector2 right, float y, float strength)
        {
            uint a = UIDrawList.ColorConvertFloat4ToU32(new Vector4(0,0,0,strength));
            uint b = UIDrawList.ColorConvertFloat4ToU32(new Vector4(0,0,0, strength));
            uint c = UIDrawList.ColorConvertFloat4ToU32(new Vector4(0,0,0,0));
            uint d = UIDrawList.ColorConvertFloat4ToU32(new Vector4(0,0,0,0));
            Vector2 posA = new Vector2(left.x, left.y);
            Vector2 posB = new Vector2(right.x, right.y + y);
            _drawList[CurrentZIndex].AddRectFilledMultiColor(posA, posB, d, c, b, a);
        }

        public void DrawLine(Vector2 start, Vector2 end, Color color, float thickness = 1f)
        {
            uint col = UIDrawList.ColorConvertFloat4ToU32(color);
            _drawList[CurrentZIndex].AddLine(start, end, col, thickness);
        }

        public void DrawCircle(Vector2 center, float radius, Color color, int segments = 12, float thickness = 1f)
        {
            uint col = UIDrawList.ColorConvertFloat4ToU32(color);
            _drawList[CurrentZIndex].AddCircle(center, radius, col, segments, thickness);
        }

        public void DrawCircleFilled(Vector2 center, float radius, Color color, int segments = 12)
        {
            uint col = UIDrawList.ColorConvertFloat4ToU32(color);
            _drawList[CurrentZIndex].AddCircleFilled(center, radius, col, segments);
        }

        public void DrawTriangle(Vector2 a, Vector2 b, Vector2 c, Color color, float thickness = 1f)
        {
            uint col = UIDrawList.ColorConvertFloat4ToU32(color);
            _drawList[CurrentZIndex].AddTriangle(a, b, c, col, thickness);
        }

        public void DrawTriangleFilled(Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            uint col = UIDrawList.ColorConvertFloat4ToU32(color);
            _drawList[CurrentZIndex].AddTriangleFilled(a, b, c, col);
        }

        public void DrawImage(Texture texture, Vector2 position, Vector2 size, Color color)
        {
            uint col = UIDrawList.ColorConvertFloat4ToU32(color);
            _drawList[CurrentZIndex].AddImage(texture.Handle, position, position + size, new Vector2(0, 0), new Vector2(1, 1), col);
        }

        public void DrawImage(Texture texture, Vector2 position, Vector2 size, Vector2 uv0, Vector2 uv1, Color color)
        {
            uint col = UIDrawList.ColorConvertFloat4ToU32(color);
            _drawList[CurrentZIndex].AddImage(texture.Handle, position, position + size, uv0, uv1, col);
        }

        //public void DrawText(string text, float fontSize, Rect rect, Color color)
        //{
        //    uint col = UIDrawList.ColorConvertFloat4ToU32(color);
        //    _drawList[CurrentZIndex].AddText(UIDrawList._fontAtlas.Fonts[0], fontSize, position, col, text);
        //}

        public void DrawText(string text, double fontSize, Rect rect, Color color)
        {
            var pos = new Vector2(rect.x, rect.y);
            var wrap = rect.width;
            DrawText(text, fontSize, pos, color, wrap);
        }

        public void DrawText(Font font, string text, double fontSize, Rect rect, Color color)
        {
            var pos = new Vector2(rect.x, rect.y);
            var wrap = rect.width;
            DrawText(font, text, fontSize, pos, color, wrap);
        }

        public void DrawText(string text, double fontSize, Vector2 position, Color color, double wrapwidth = 0.0f)
        {
            uint col = UIDrawList.ColorConvertFloat4ToU32(color);
            _drawList[CurrentZIndex].AddText((float)fontSize, position, col, text, wrap_width: (float)wrapwidth);
        }

        public void DrawText(Font font, string text, double fontSize, Vector2 position, Color color, double wrapwidth = 0.0f)
        {
            uint col = UIDrawList.ColorConvertFloat4ToU32(color);
            _drawList[CurrentZIndex].PushTextureID(font.Texture.Handle);
            var test = (Mathf.Sin(Time.time) + 1.0) * 0.5;
            _drawList[CurrentZIndex].AddText(font, (float)fontSize, position, col, text, wrap_width: (float)wrapwidth);
            _drawList[CurrentZIndex].PopTextureID();
        }





    }
}