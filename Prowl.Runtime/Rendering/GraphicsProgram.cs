using System;

namespace Prowl.Runtime.Rendering
{
    public abstract class GraphicsProgram : IDisposable
    {
        public abstract bool IsDisposed { get; protected set; }
        public abstract void Dispose();
    }
}
