namespace SourceGenerator.MathFunctions;

[MathFunction("Gamma")]
public class GammaFunctionGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "byte" };
    public override int[] SupportedDimensions => new[] { 3, 4 };
    public override bool SupportsScalars => false;

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var typeName = GetTypeName(type);
        var vectorType = $"{typeName}{dimension}";

        if (type == "byte")
        {
            var alphaHandling = dimension == 4 ? ", color.W" : "";
            return $@"        /// <summary>Applies gamma correction to a color.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {vectorType} Gamma({vectorType} color, float gamma = 2.2f)
        {{
            return new {vectorType}(
                (byte)(Pow(Max(0f, color.X / 255f), 1f / gamma) * 255f),
                (byte)(Pow(Max(0f, color.Y / 255f), 1f / gamma) * 255f),
                (byte)(Pow(Max(0f, color.Z / 255f), 1f / gamma) * 255f){alphaHandling}
            );
        }}";
        }
        else
        {
            var alphaHandling = dimension == 4 ? ", color.W" : "";
            return $@"        /// <summary>Applies gamma correction to a color.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {vectorType} Gamma({vectorType} color, float gamma = 2.2f)
        {{
            return new {vectorType}(
                Pow(Max(0f, color.X), 1f / gamma),
                Pow(Max(0f, color.Y), 1f / gamma),
                Pow(Max(0f, color.Z), 1f / gamma){alphaHandling}
            );
        }}";
        }
    }
}
