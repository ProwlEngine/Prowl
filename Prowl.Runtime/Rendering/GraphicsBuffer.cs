using System;

namespace Prowl.Runtime.Rendering
{
    public abstract class GraphicsBuffer : IDisposable
    {
        public abstract bool IsDisposed { get; protected set; }
        public abstract void Dispose();
    }
}
