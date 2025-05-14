namespace SourceGenerator.MathFunctions
{
    [MathFunction("RGBToHSL")]
    public class RGBToHSLFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "byte" };
        public override int[] SupportedDimensions => new[] { 3, 4 };
        public override bool SupportsScalars => false;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var vectorType = $"{typeName}{dimension}";

            // For byte types, we need to normalize to 0-1 range for calculation
            var normalizeR = type == "byte" ? "X / 255f" : "X";
            var normalizeG = type == "byte" ? "Y / 255f" : "Y";
            var normalizeB = type == "byte" ? "Z / 255f" : "Z";

            string varName = $"rgb{(dimension == 4 ? "a" : "")}";

            // Alpha handling - preserve alpha as-is
            var alphaHandling = dimension == 4 ? ", " + varName + ".W" : "";

            // For byte types, we need to convert HSL back to byte range
            string formatH, formatS, formatL;
            if (type == "byte")
            {
                // HSL values for byte types: H (0-255 mapped from 0-360), S (0-255), L (0-255)
                formatH = "(byte)(h * 255f / 360f)";
                formatS = "(byte)(s * 255f)";
                formatL = "(byte)(l * 255f)";
            }
            else
            {
                // HSL values for float types: H (0-360), S (0-1), L (0-1)
                formatH = "h";
                formatS = "s";
                formatL = "l";
            }

            return $@"        /// <summary>Converts RGB to HSL color space.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {vectorType} RGBToHSL({vectorType} {varName})
        {{
            float r = {varName}.{normalizeR}, g = {varName}.{normalizeG}, b = {varName}.{normalizeB};
            float max = Max(r, Max(g, b));
            float min = Min(r, Min(g, b));
            float delta = max - min;

            float h = 0f, s = 0f, l = (max + min) / 2f;

            if (delta > 0f)
            {{
                s = l > 0.5f ? delta / (2f - max - min) : delta / (max + min);

                if (max == r)
                    h = 60f * (((g - b) / delta) % 6f);
                else if (max == g)
                    h = 60f * ((b - r) / delta + 2f);
                else if (max == b)
                    h = 60f * ((r - g) / delta + 4f);

                if (h < 0f) h += 360f;
            }}

            return new {vectorType}({formatH}, {formatS}, {formatL}{alphaHandling});
        }}";
        }
    }
}