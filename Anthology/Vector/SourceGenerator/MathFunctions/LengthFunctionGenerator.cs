namespace SourceGenerator.MathFunctions
{
    // Vector-specific functions
    [MathFunction("Length")]
    public class LengthFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        public override bool SupportsScalars => false; // Only makes sense for vectors

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var vectorType = $"{typeName}{dimension}";

            return $@"        /// <summary>Returns the length (magnitude) of a {vectorType} vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} Length({vectorType} x) {{ return x.Length; }}";
        }
    }
}