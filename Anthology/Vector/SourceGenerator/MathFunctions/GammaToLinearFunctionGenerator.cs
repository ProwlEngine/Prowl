namespace SourceGenerator.MathFunctions
{
    [MathFunction("GammaToLinear")]
    public class GammaToLinearFunctionGenerator : MathFunctionGenerator
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
                var alphaHandling = dimension == 4 ? ", gamma.W" : "";
                return $@"        /// <summary>Converts a gamma space color to linear space.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {vectorType} GammaToLinear({vectorType} gamma)
        {{
            return new {vectorType}(
                (byte)(Pow(Max(0f, gamma.X / 255f), 2.2f) * 255f),
                (byte)(Pow(Max(0f, gamma.Y / 255f), 2.2f) * 255f),
                (byte)(Pow(Max(0f, gamma.Z / 255f), 2.2f) * 255f){alphaHandling}
            );
        }}";
            }
            else
            {
                var alphaHandling = dimension == 4 ? ", gamma.W" : "";
                return $@"        /// <summary>Converts a gamma space color to linear space.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {vectorType} GammaToLinear({vectorType} gamma)
        {{
            return new {vectorType}(
                Pow(Max(0f, gamma.X), 2.2f),
                Pow(Max(0f, gamma.Y), 2.2f),
                Pow(Max(0f, gamma.Z), 2.2f){alphaHandling}
            );
        }}";
            }
        }



    }
}