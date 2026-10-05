using System;


namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// Binds a shaderdef pass variant to a command buffer.
/// </summary>
public static class CommandBufferExtensions
{
    /// <summary>
    /// Binds the pass variant for keywords over library-default base states, without mutating the pass. Compiles on demand if a compiler is attached.
    /// </summary>
    public static void SetShader(this CommandBuffer commandBuffer, ShaderPass pass, ReadOnlySpan<Keyword> keywords)
        => SetShader(commandBuffer, pass, pass.GetKey(keywords));


    /// <summary>
    /// Binds the pass variant for a key from ShaderPass.GetKey over library-default base states.
    /// </summary>
    public static void SetShader(this CommandBuffer commandBuffer, ShaderPass pass, int key)
        => commandBuffer.SetShader(pass.ResolveDefaultProgram(key));


    /// <summary>
    /// Binds the default variant (key 0) over library-default base states. Compiles on demand if a compiler is attached.
    /// </summary>
    public static void SetShader(this CommandBuffer commandBuffer, ShaderPass pass)
        => SetShader(commandBuffer, pass, 0);


    /// <summary>
    /// Binds the default variant over library-default base states, with overrideState applied on top of the pass state. Unset override fields defer to the pass.
    /// </summary>
    public static void SetShader(this CommandBuffer commandBuffer, ShaderPass pass, PassState overrideState)
        => commandBuffer.SetShader(pass.ResolveDefaultProgram(0, overrideState));


    /// <summary>
    /// Binds the pass variant for a key over given base states. Compiles on demand if a compiler is attached.
    /// </summary>
    public static void SetShader(this CommandBuffer commandBuffer, ShaderPass pass, int key,
        BlendStateDescription baseBlend, DepthStencilStateDescription baseDepth, RasterizerStateDescription baseRaster)
    {
        GraphicsProgram program = pass.ResolveProgram(key, baseBlend, baseDepth, baseRaster);
        commandBuffer.SetShader(program);
    }


    internal static BlendStateDescription DefaultBlend => BlendStateDescription.SingleDisabled;
    internal static DepthStencilStateDescription DefaultDepth => DepthStencilStateDescription.DepthOnlyLessEqual;
    internal static RasterizerStateDescription DefaultRaster => RasterizerStateDescription.Default;
}
