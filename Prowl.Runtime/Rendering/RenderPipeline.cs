using Veldrid;


namespace Prowl.Runtime.RenderPipelines
{
    public abstract class RenderPipeline<T> : EngineObject
    {
        public abstract void Render(Framebuffer target, T data);
    }
}
