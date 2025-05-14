namespace SourceGenerator.MathFunctions;

[MathFunction("SlerpUnclamped")]
public class SlerpUnclampedFunctionGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    public override bool SupportsScalars => false;

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var typeName = GetTypeName(type);
        var vectorType = $"{typeName}{dimension}";
        var mathClass = GetMathClass(type);
        var epsilon = type == "float" ? "1e-6f" : "1e-15";

        return $@"        /// <summary>Spherically interpolates between two vectors without clamping t.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {vectorType} SlerpUnclamped({vectorType} a, {vectorType} b, {type} t)
        {{
            {vectorType} result;

            // Normalize the vectors
            {vectorType} normalizedA = Normalize(a);
            {vectorType} normalizedB = Normalize(b);
            
            // Calculate the cosine of the angle between them
            {type} dot = Dot(normalizedA, normalizedB);
            
            // If the dot product is negative, slerp won't take the shorter path.
            // So negate one vector to get the shorter path.
            if (dot < 0{(type == "float" ? "f" : "")})
            {{
                normalizedB = -normalizedB;
                dot = -dot;
            }}
            
            // If the vectors are close to identical, just use linear interpolation
            if (dot > 1{(type == "float" ? "f" : "")} - {epsilon})
            {{
                result = normalizedA + t * (normalizedB - normalizedA);
                return Normalize(result) * Lerp(Length(a), Length(b), t);
            }}
            
            // Calculate angle and sin
            {type} angle = {mathClass}.Acos(Abs(dot));
            {type} sinAngle = {mathClass}.Sin(angle);
            
            // Calculate the scale factors
            {type} scale1 = {mathClass}.Sin((1{(type == "float" ? "f" : "")} - t) * angle) / sinAngle;
            {type} scale2 = {mathClass}.Sin(t * angle) / sinAngle;
            
            // Interpolate
            result = scale1 * normalizedA + scale2 * normalizedB;
            return result * Lerp(Length(a), Length(b), t);
        }}";
    }
}
