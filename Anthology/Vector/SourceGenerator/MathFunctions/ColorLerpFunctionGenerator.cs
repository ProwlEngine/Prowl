namespace SourceGenerator.MathFunctions
{
    [MathFunction("ColorLerp")]
    public class ColorLerpFunctionGenerator : MathFunctionGenerator
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
                return $@"        /// <summary>Performs color-space aware linear interpolation between two RGB colors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {vectorType} ColorLerp({vectorType} from, {vectorType} to, float t)
        {{
            // Convert to HSV for more natural color interpolation
            var hsvFrom = RGBToHSV(from);
            var hsvTo = RGBToHSV(to);

            // Convert byte HSV values to float for interpolation
            float hFrom = hsvFrom.X * 360f / 255f;
            float sFrom = hsvFrom.Y / 255f;
            float vFrom = hsvFrom.Z / 255f;
            float aFrom = {(dimension == 4 ? "hsvFrom.W / 255f" : "0f")};

            float hTo = hsvTo.X * 360f / 255f;
            float sTo = hsvTo.Y / 255f;
            float vTo = hsvTo.Z / 255f;
            float aTo = {(dimension == 4 ? "hsvTo.W / 255f" : "0f")};

            // Handle hue wrapping for shortest path interpolation
            float deltaHue = hTo - hFrom;
            if (deltaHue > 180f)
                hFrom += 360f;
            else if (deltaHue < -180f)
                hTo += 360f;

            // Interpolate in HSV space
            float hResult = hFrom + (hTo - hFrom) * t;
            float sResult = sFrom + (sTo - sFrom) * t;
            float vResult = vFrom + (vTo - vFrom) * t;
            {(dimension == 4 ? "float aResult = aFrom + (aTo - aFrom) * t;" : "")}

            // Wrap hue back to 0-360 range
            if (hResult < 0f) hResult += 360f;
            if (hResult >= 360f) hResult -= 360f;

            // Convert back to byte HSV format
            var hsvResult = new {(dimension == 3 ? "Byte3" : "Byte4")}(
                (byte)(hResult * 255f / 360f),
                (byte)(sResult * 255f),
                (byte)(vResult * 255f){(dimension == 4 ? ",\n                (byte)(aResult * 255f)" : "")}
            );

            // Convert back to RGB
            return HSVToRGB(hsvResult);
        }}";
            }
            else
            {
                return $@"        /// <summary>Performs color-space aware linear interpolation between two RGB colors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {vectorType} ColorLerp({vectorType} from, {vectorType} to, float t)
        {{
            // Convert to HSV for more natural color interpolation
            var hsvFrom = RGBToHSV(from);
            var hsvTo = RGBToHSV(to);

            // Handle hue wrapping for shortest path interpolation
            float deltaHue = hsvTo.X - hsvFrom.X;
            if (deltaHue > 180f)
                hsvFrom.X += 360f;
            else if (deltaHue < -180f)
                hsvTo.X += 360f;

            // Interpolate in HSV space
            var hsvResult = new {(dimension == 3 ? "Float3" : "Float4")}(
                hsvFrom.X + (hsvTo.X - hsvFrom.X) * t,
                hsvFrom.Y + (hsvTo.Y - hsvFrom.Y) * t,
                hsvFrom.Z + (hsvTo.Z - hsvFrom.Z) * t{(dimension == 4 ? ",\n                hsvFrom.W + (hsvTo.W - hsvFrom.W) * t" : "")}
            );

            // Wrap hue back to 0-360 range
            if (hsvResult.X < 0f) hsvResult.X += 360f;
            if (hsvResult.X >= 360f) hsvResult.X -= 360f;

            // Convert back to RGB
            return HSVToRGB(hsvResult);
        }}";
            }
        }
    }
}