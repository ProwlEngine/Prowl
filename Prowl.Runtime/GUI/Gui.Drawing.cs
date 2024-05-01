using Prowl.Runtime.GUI.Graphics;

namespace Prowl.Runtime.GUI
{

    public partial class Gui
    {
        public int CurrentZIndex => CurrentState.ZIndex;

        public void PushClip(Rect rect)
        {
            if (CurrentPass != Pass.AfterLayout) return;
            _drawList[CurrentZIndex].PushClipRect(new Vector4(rect.x, rect.y, rect.x + rect.width, rect.y + rect.height));
        }

        public void PopClip()
        {
            if (CurrentPass != Pass.AfterLayout) return;
            _drawList[CurrentZIndex].PopClipRect();
        }

        public void DrawRect(Rect screenRect, Color color, float thickness = 1f, float roundness = 0.0f) => DrawRect(new(screenRect.x, screenRect.y), new(screenRect.width, screenRect.height), color, thickness, roundness);
        public void DrawRect(Vector2 topleft, Vector2 size, Color color, float thickness = 1f, float roundness = 0.0f)
        {
            if (CurrentPass != Pass.AfterLayout) return;

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
            if (CurrentPass != Pass.AfterLayout) return;

            uint col = UIDrawList.ColorConvertFloat4ToU32(color);
            var pos = topleft;
            _drawList[CurrentZIndex].AddRectFilled(pos, pos + size, col, roundness, 15);
        }

        public void DrawLine(Vector2 start, Vector2 end, Color color, float thickness = 1f)
        {
            if (CurrentPass != Pass.AfterLayout) return;

            uint col = UIDrawList.ColorConvertFloat4ToU32(color);
            _drawList[CurrentZIndex].AddLine(start, end, col, thickness);
        }

        public void DrawCircle(Vector2 center, float radius, Color color, int segments = 12, float thickness = 1f)
        {
            if (CurrentPass != Pass.AfterLayout) return;

            uint col = UIDrawList.ColorConvertFloat4ToU32(color);
            _drawList[CurrentZIndex].AddCircle(center, radius, col, segments, thickness);
        }

        public void DrawCircleFilled(Vector2 center, float radius, Color color, int segments = 12)
        {
            if (CurrentPass != Pass.AfterLayout) return;

            uint col = UIDrawList.ColorConvertFloat4ToU32(color);
            _drawList[CurrentZIndex].AddCircleFilled(center, radius, col, segments);
        }

        public void DrawTriangle(Vector2 a, Vector2 b, Vector2 c, Color color, float thickness = 1f)
        {
            if (CurrentPass != Pass.AfterLayout) return;

            uint col = UIDrawList.ColorConvertFloat4ToU32(color);
            _drawList[CurrentZIndex].AddTriangle(a, b, c, col, thickness);
        }

        public void DrawTriangleFilled(Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            if (CurrentPass != Pass.AfterLayout) return;

            uint col = UIDrawList.ColorConvertFloat4ToU32(color);
            _drawList[CurrentZIndex].AddTriangleFilled(a, b, c, col);
        }

        public void DrawImage(Texture texture, Vector2 position, Vector2 size, Color color)
        {
            if (CurrentPass != Pass.AfterLayout) return;

            uint col = UIDrawList.ColorConvertFloat4ToU32(color);
            _drawList[CurrentZIndex].AddImage(texture.Handle, position, position + size, new Vector2(0, 0), new Vector2(1, 1), col);
        }

        public void DrawImage(Texture texture, Vector2 position, Vector2 size, Vector2 uv0, Vector2 uv1, Color color)
        {
            if (CurrentPass != Pass.AfterLayout) return;

            uint col = UIDrawList.ColorConvertFloat4ToU32(color);
            _drawList[CurrentZIndex].AddImage(texture.Handle, position, position + size, uv0, uv1, col);
        }

        //public void DrawText(string text, float fontSize, Rect rect, Color color)
        //{
        //    if (CurrentPass != Pass.Draw) return;
        //
        //    uint col = UIDrawList.ColorConvertFloat4ToU32(color);
        //    _drawList[CurrentZIndex].AddText(UIDrawList._fontAtlas.Fonts[0], fontSize, position, col, text);
        //}

        public void DrawText(string text, float fontSize, Vector2 position, Color color)
        {
            if (CurrentPass != Pass.AfterLayout) return;

            uint col = UIDrawList.ColorConvertFloat4ToU32(color);
            _drawList[CurrentZIndex].AddText(UIDrawList._fontAtlas.Fonts[0], fontSize, position, col, text);
        }





    }
}