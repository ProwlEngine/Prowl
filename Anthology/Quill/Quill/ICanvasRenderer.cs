using System;
using Prowl.Vector;
using System.Collections.Generic;

namespace Prowl.Quill
{
    public interface ICanvasRenderer : IDisposable
    {
        public object CreateTexture(uint width, uint height);
        public Vector2Int GetTextureSize(object texture);
        public void SetTextureData(object texture, IntRect bounds, byte[] data);
        public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls);
    }
}
