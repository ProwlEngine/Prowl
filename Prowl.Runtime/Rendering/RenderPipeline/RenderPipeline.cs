using Prowl.Runtime.Utils;


namespace Prowl.Runtime.RenderPipelines
{
    public abstract class RenderPipeline : ScriptableObject   
    {

        public virtual void InitializeResources() { }

        public abstract void Render(RenderingContext context, Camera[] cameras);

        public virtual void ReleaseResources() { }
    }
}