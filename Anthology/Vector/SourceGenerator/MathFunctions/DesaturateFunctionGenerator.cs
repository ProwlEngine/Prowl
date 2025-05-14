namespace SourceGenerator.MathFunctions;

[MathFunction("Desaturate")]
public class DesaturateFunctionGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "byte" };
    public override int[] SupportedDimensions => new[] { 3, 4 };
    public override bool SupportsScalars => false;

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var typeName = GetTypeName(type);
        var vectorType = $"{typeName}{dimension}";

        // For byte types, we need different handling
        if (type == "byte")
        {
            var alphaHandling = dimension == 4 ? ", color.W" : "";
            return $@"        /// <summary>Desaturates a color by blending it towards grayscale.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {vectorType} Desaturate({vectorType} color, float amount = 1f)
        {{
            // Standard luminance weights for RGB
            float luminance = 0.299f * color.X + 0.587f * color.Y + 0.114f * color.Z;
            byte grayByte = (byte)Clamp(luminance, 0f, 255f);
            var gray = new {vectorType}(grayByte, grayByte, grayByte{alphaHandling});
            
            amount = Clamp(amount, 0f, 1f);
            return new {vectorType}(
                (byte)(color.X + (gray.X - color.X) * amount),
                (byte)(color.Y + (gray.Y - color.Y) * amount),
                (byte)(color.Z + (gray.Z - color.Z) * amount){alphaHandling}
            );
        }}";
        }
        else
        {
            var alphaHandling = dimension == 4 ? ", color.W" : "";
            return $@"        /// <summary>Desaturates a color by blending it towards grayscale.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {vectorType} Desaturate({vectorType} color, float amount = 1f)
        {{
            // Standard luminance weights for RGB
            float luminance = 0.299f * color.X + 0.587f * color.Y + 0.114f * color.Z;
            var gray = new {vectorType}(luminance, luminance, luminance{alphaHandling});
            amount = Clamp(amount, 0f, 1f);
            return color + (gray - color) * amount;
        }}";
        }
    }
}
