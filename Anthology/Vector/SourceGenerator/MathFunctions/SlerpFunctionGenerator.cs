namespace SourceGenerator.MathFunctions
{
    [MathFunction("Slerp")]
    public class SlerpFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        public override bool SupportsScalars => false;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var vectorType = $"{typeName}{dimension}";
            var mathClass = GetMathClass(type);
            var epsilon = type == "float" ? "1e-6f" : "1e-15";

            return $@"        /// <summary>Spherically interpolates between two vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {vectorType} Slerp({vectorType} a, {vectorType} b, {type} t)
        {{
            t = Saturate(t);
            return SlerpUnclamped(a, b, t);
        }}";
        }
    }
}