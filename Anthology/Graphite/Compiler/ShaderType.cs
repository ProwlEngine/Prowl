using Prowl.Slang;


namespace Prowl.Graphite.Compiler;


public enum ShaderType
{
    Rasterization,
    Compute,
    Raytracing,
}


public static class ShaderTypeExtensions
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
