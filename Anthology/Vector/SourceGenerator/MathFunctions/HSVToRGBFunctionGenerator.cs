namespace SourceGenerator.MathFunctions
{
    [MathFunction("HSVToRGB")]
    public class HSVToRGBFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "byte" };
        public override int[] SupportedDimensions => new[] { 3, 4 };
        public override bool SupportsScalars => false;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var vectorType = $"{typeName}{dimension}";

            string varName = $"hsv{(dimension == 4 ? "a" : "")}";

            // Alpha handling - preserve alpha as-is
            var alphaHandling = dimension == 4 ? ", " + varName + ".W" : "";

            // For byte types, convert input HSV from byte range to float for calculation
            string normalizeH, normalizeS, normalizeV;
            if (type == "byte")
            {
                normalizeH = varName + ".X * 360f / 255f";  // Convert 0-255 to 0-360
                normalizeS = varName + ".Y / 255f";          // Convert 0-255 to 0-1
                normalizeV = varName + ".Z / 255f";          // Convert 0-255 to 0-1
            }
            else
            {
                normalizeH = varName + ".X";  // Already 0-360
                normalizeS = varName + ".Y";  // Already 0-1
                normalizeV = varName + ".Z";  // Already 0-1
            }

            // For byte types, convert RGB back to byte range
            var convertR = type == "byte" ? "(byte)(r * 255f)" : "r";
            var convertG = type == "byte" ? "(byte)(g * 255f)" : "g";
            var convertB = type == "byte" ? "(byte)(b * 255f)" : "b";

            return $@"        /// <summary>Converts HSV to RGB color space.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {vectorType} HSVToRGB({vectorType} {varName})
        {{
            float h = {normalizeH}, s = {normalizeS}, v = {normalizeV};
            
            if (s <= 0f)
            {{
                float gray = v{(type == "byte" ? " * 255f" : "")};
                return new {vectorType}({(type == "byte" ? "(byte)gray, (byte)gray, (byte)gray" : "gray, gray, gray")}{alphaHandling});
            }}

            float c = v * s;
            float x = c * (1f - Abs((h / 60f) % 2f - 1f));
            float m = v - c;

            float r = 0f, g = 0f, b = 0f;

            if (h >= 0f && h < 60f)
                (r, g, b) = (c, x, 0f);
            else if (h >= 60f && h < 120f)
                (r, g, b) = (x, c, 0f);
            else if (h >= 120f && h < 180f)
                (r, g, b) = (0f, c, x);
            else if (h >= 180f && h < 240f)
                (r, g, b) = (0f, x, c);
            else if (h >= 240f && h < 300f)
                (r, g, b) = (x, 0f, c);
            else if (h >= 300f && h < 360f)
                (r, g, b) = (c, 0f, x);

            r += m; g += m; b += m;

            return new {vectorType}({convertR}, {convertG}, {convertB}{alphaHandling});
        }}";
        }
    }
}