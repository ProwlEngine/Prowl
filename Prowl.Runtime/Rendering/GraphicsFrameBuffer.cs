namespace Prowl.Runtime.Rendering
{
    public abstract unsafe class GraphicsFrameBuffer
    {
        public struct Attachment
        {
            public GraphicsTexture texture;
            public bool isDepth;
        }

        public abstract bool IsDisposed { get; protected set; }

        public abstract void Dispose();
    }
}
