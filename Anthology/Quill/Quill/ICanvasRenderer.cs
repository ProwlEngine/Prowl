using Prowl.Vector;

namespace Prowl.Quill
{
    public interface ICanvasRenderer
    {
        public object CreateTexture(uint width, uint height);
        public Vector2Int GetTextureSize(object texture);
        public void SetTextureData(object texture, IntRect bounds, byte[] data);
        public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls);

        public void Dispose();
    }
}
