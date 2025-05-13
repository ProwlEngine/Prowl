namespace SourceGenerator.MathFunctions
{
    [MathFunction("Dot")]
    public class DotFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double", "int" };
        public override bool SupportsScalars => false;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var vectorType = $"{typeName}{dimension}";
            var dotExpression = string.Join(" + ", components.Select(c => $"x.{c} * y.{c}"));

            return $@"        /// <summary>Returns the dot product of two {vectorType} vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} Dot({vectorType} x, {vectorType} y) {{ return {dotExpression}; }}";
        }
    }
}