using System;

namespace Prowl.Runtime.Rendering
{
    public abstract class GraphicsTexture : IDisposable
    {
        public abstract TextureType Type { get; protected set; }
        public abstract bool IsDisposed { get; protected set; }
        public abstract void Dispose();
    }
}
