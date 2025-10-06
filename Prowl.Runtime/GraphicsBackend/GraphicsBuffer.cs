using System;

namespace Prowl.Runtime.GraphicsBackend
{
    public abstract class GraphicsBuffer : IDisposable
    {
        public abstract bool IsDisposed { get; protected set; }
        public abstract void Dispose();
    }
}
