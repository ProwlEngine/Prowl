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
        => SetShader(commandBuffer, pass, DefaultBlend, DefaultDepth, DefaultRaster);


    /// <summary>
    /// Binds pass's active variant over library-default base states, with overrideState applied on top of the pass state. Unset override fields defer to the pass.
    /// </summary>
    public static void SetShader(this CommandBuffer commandBuffer, ShaderPass pass, PassState overrideState)
    {
        GraphicsProgram program = pass.ResolveProgram(overrideState.Apply(pass.State), DefaultBlend, DefaultDepth, DefaultRaster);
        commandBuffer.SetShader(program);
    }


    /// <summary>
    /// Binds pass's active variant over given base states. Compiles on demand if a compiler is attached.
    /// </summary>
    public static void SetShader(this CommandBuffer commandBuffer, ShaderPass pass,
        BlendStateDescription baseBlend, DepthStencilStateDescription baseDepth, RasterizerStateDescription baseRaster)
    {
        GraphicsProgram program = pass.ResolveProgram(baseBlend, baseDepth, baseRaster);
        commandBuffer.SetShader(program);
    }


    private static BlendStateDescription DefaultBlend => BlendStateDescription.SingleDisabled;
    private static DepthStencilStateDescription DefaultDepth => DepthStencilStateDescription.DepthOnlyLessEqual;
    private static RasterizerStateDescription DefaultRaster => new(FaceCullMode.Back, FrontFace.Clockwise, true, false);
}
