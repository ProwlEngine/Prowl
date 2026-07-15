namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// Extensions that bind a shaderdef pass's active variant to a command buffer.
/// </summary>
public static class CommandBufferExtensions
{
    /// <summary>
    /// Binds the pass's active variant, overlaying the pass render state onto library-default base
    /// states. Compiles the active variant on demand if a compiler is attached.
    /// </summary>
    public static void SetShader(this CommandBuffer commandBuffer, ShaderPass pass)
        => SetShader(
            commandBuffer,
            pass,
            BlendStateDescription.SingleDisabled,
            DepthStencilStateDescription.DepthOnlyLessEqual,
            new RasterizerStateDescription(FaceCullMode.Back, FrontFace.Clockwise, true, false));


    /// <summary>
    /// Binds the pass's active variant, overlaying the pass render state onto the given base states.
    /// Compiles the active variant on demand if a compiler is attached.
    /// </summary>
    public static void SetShader(this CommandBuffer commandBuffer, ShaderPass pass,
        BlendStateDescription baseBlend, DepthStencilStateDescription baseDepth, RasterizerStateDescription baseRaster)
    {
        GraphicsProgram program = pass.ResolveProgram(baseBlend, baseDepth, baseRaster);
        commandBuffer.SetShader(program);
    }
}
