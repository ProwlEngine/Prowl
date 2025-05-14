namespace SourceGenerator.MathFunctions;

[MathFunction("LinearToGamma")]
public class LinearToGammaFunctionGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "byte" };
    public override int[] SupportedDimensions => new[] { 3, 4 };
    public override bool SupportsScalars => false;

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var typeName = GetTypeName(type);
        var vectorType = $"{typeName}{dimension}";

        return $@"        /// <summary>Converts a linear color to gamma space (sRGB).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {vectorType} LinearToGamma({vectorType} linear)
        {{
            return Gamma(linear, 2.2f);
        }}";
    }
}
