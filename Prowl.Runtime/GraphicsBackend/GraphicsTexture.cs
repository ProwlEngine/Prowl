using System;
using Prowl.Runtime.GraphicsBackend.Primitives;

namespace Prowl.Runtime.GraphicsBackend
{
    public abstract class GraphicsTexture : IDisposable
    {
        public abstract TextureType Type { get; protected set; }
        public abstract bool IsDisposed { get; protected set; }
        public abstract void Dispose();
    }
}
