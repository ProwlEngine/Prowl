namespace Prowl.Veldrid.OpenGL;

internal unsafe partial class OpenGLPipeline
{
#if !VALIDATE_USAGE
    public ResourceLayout[] ResourceLayouts { get; private set; }
#endif

    private void OpenGLPipeline_StoreResourceLayouts(ResourceLayout[] resourceLayouts)
    {
#if !VALIDATE_USAGE
        ResourceLayouts = Util.ShallowClone(resourceLayouts);
#endif
    }
}
