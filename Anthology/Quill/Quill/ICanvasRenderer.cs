using System;
using Prowl.Vector;
using System.Collections.Generic;
using Prowl.Vector.Geometry;

namespace Prowl.Quill
{
    public interface ICanvasRenderer : IDisposable
    {
        public object CreateTexture(uint width, uint height);
        public Int2 GetTextureSize(object texture);
        public void SetTextureData(object texture, IntRect bounds, byte[] data);
        public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls);
    }
}
