using Prowl.Slang;


namespace Prowl.Graphite.ShaderDef.Compiler;


/// <summary>
/// The type of shader to compile.
/// </summary>
public enum ShaderType
{
    /// <summary>
    /// A rasterization shader. Applies to both regular vertex and mesh shaders. 
    /// </summary>
    Rasterization,

    /// <summary>
    /// A compute shader. Only applies to compute kernels.
    /// </summary>
    Compute,

    /// <summary>
    /// A raytracing shader. Only applies to raytracing payload shaders.
    /// </summary>
    Raytracing,
}


internal static class ShaderTypeExtensions
{
    public static ShaderType FromStage(ShaderStage stage) =>
        stage switch
        {
            ShaderStage.Vertex => ShaderType.Rasterization,
            ShaderStage.Mesh => ShaderType.Rasterization,
            ShaderStage.Amplification => ShaderType.Rasterization,
            ShaderStage.Hull => ShaderType.Rasterization,
            ShaderStage.Domain => ShaderType.Rasterization,
            ShaderStage.Geometry => ShaderType.Rasterization,
            ShaderStage.Fragment => ShaderType.Rasterization,
            ShaderStage.RayGeneration => ShaderType.Raytracing,
            ShaderStage.Intersection => ShaderType.Raytracing,
            ShaderStage.AnyHit => ShaderType.Raytracing,
            ShaderStage.ClosestHit => ShaderType.Raytracing,
            ShaderStage.Miss => ShaderType.Raytracing,
            ShaderStage.Callable => ShaderType.Raytracing,
            ShaderStage.Compute => ShaderType.Compute,
            _ => throw new System.Exception($"Invalid shader stage: {stage}")
        };
}
