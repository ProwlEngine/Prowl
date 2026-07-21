namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// Binds a shaderdef pass's active variant to a command buffer.
/// </summary>
public static class CommandBufferExtensions
{
    /// <summary>
    /// Binds pass's active variant over library-default base states. Compiles on demand if a compiler is attached.
    /// </summary>
    public static void SetShader(this CommandBuffer commandBuffer, ShaderPass pass)
        => SetShader(
            commandBuffer,
            pass,
            BlendStateDescription.SingleDisabled,
            DepthStencilStateDescription.DepthOnlyLessEqual,
            new RasterizerStateDescription(FaceCullMode.Back, FrontFace.Clockwise, true, false));


    /// <summary>
    /// Binds pass's active variant over given base states. Compiles on demand if a compiler is attached.
    /// </summary>
    public static void SetShader(this CommandBuffer commandBuffer, ShaderPass pass,
        BlendStateDescription baseBlend, DepthStencilStateDescription baseDepth, RasterizerStateDescription baseRaster)
    {
        GraphicsProgram program = pass.ResolveProgram(baseBlend, baseDepth, baseRaster);
        commandBuffer.SetShader(program);
    }
}
