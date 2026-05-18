using System.Diagnostics;

namespace Prowl.Veldrid;

public abstract partial class Pipeline
{
#if VALIDATE_USAGE
    internal OutputDescription GraphicsOutputDescription { get; private set; }
    internal ResourceLayout[] ResourceLayouts { get; private set; }
#endif

    [Conditional("VALIDATE_USAGE")]
    private void Pipeline_StoreGraphicsOutputDescription(ref GraphicsPipelineDescription graphicsDescription)
    {
#if VALIDATE_USAGE
        GraphicsOutputDescription = graphicsDescription.Outputs;
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private void Pipeline_StoreResourceLayouts(ResourceLayout[] resourceLayouts)
    {
#if VALIDATE_USAGE
        ResourceLayouts = Util.ShallowClone(resourceLayouts);
#endif
    }
}
