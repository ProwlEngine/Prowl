namespace SourceGenerator.MathFunctions;

[MathFunction("Brightness")]
public class BrightnessFunctionGenerator : MathFunctionGenerator
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
            return $@"        /// <summary>Adjusts the brightness of a color.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {vectorType} Brightness({vectorType} color, float brightness)
        {{
            return new {vectorType}(
                (byte)Clamp(color.X * brightness, 0f, 255f),
                (byte)Clamp(color.Y * brightness, 0f, 255f),
                (byte)Clamp(color.Z * brightness, 0f, 255f){alphaHandling}
            );
        }}";
        }
        else
        {
            var alphaHandling = dimension == 4 ? ", color.W" : "";
            return $@"        /// <summary>Adjusts the brightness of a color.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {vectorType} Brightness({vectorType} color, float brightness)
        {{
            return new {vectorType}(
                Max(0f, color.X * brightness),
                Max(0f, color.Y * brightness),
                Max(0f, color.Z * brightness){alphaHandling}
            );
        }}";
        }
    }
}
