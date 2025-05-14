namespace SourceGenerator.MathFunctions;

[MathFunction("Contrast")]
public class ContrastFunctionGenerator : MathFunctionGenerator
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
            return $@"        /// <summary>Adjusts the contrast of a color.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {vectorType} Contrast({vectorType} color, float contrast)
        {{
            const float midpoint = 127.5f;
            return new {vectorType}(
                (byte)Clamp((color.X - midpoint) * contrast + midpoint, 0f, 255f),
                (byte)Clamp((color.Y - midpoint) * contrast + midpoint, 0f, 255f),
                (byte)Clamp((color.Z - midpoint) * contrast + midpoint, 0f, 255f){alphaHandling}
            );
        }}";
        }
        else
        {
            var alphaHandling = dimension == 4 ? ", color.W" : "";
            return $@"        /// <summary>Adjusts the contrast of a color.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {vectorType} Contrast({vectorType} color, float contrast)
        {{
            const float midpoint = 0.5f;
            return new {vectorType}(
                Clamp((color.X - midpoint) * contrast + midpoint, 0f, 1f),
                Clamp((color.Y - midpoint) * contrast + midpoint, 0f, 1f),
                Clamp((color.Z - midpoint) * contrast + midpoint, 0f, 1f){alphaHandling}
            );
        }}";
        }
    }
}
