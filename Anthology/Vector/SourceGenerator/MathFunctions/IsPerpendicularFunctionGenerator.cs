namespace SourceGenerator.MathFunctions
{
    [MathFunction("IsPerpendicular")]
    public class IsPerpendicularFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        public override bool SupportsScalars => false;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var vectorType = $"{typeName}{dimension}";
            var mathClass = GetMathClass(type);
            var tolerance = type == "float" ? "1e-6f" : "1e-15";

            return $@"        /// <summary>Checks if two vectors are perpendicular within a tolerance.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPerpendicular({vectorType} a, {vectorType} b, {type} tolerance = {tolerance})
        {{
            {type} dot = {mathClass}.Abs(Dot(a, b));
            return dot <= tolerance;
        }}";
        }
    }
}