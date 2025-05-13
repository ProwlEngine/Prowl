namespace SourceGenerator.MathFunctions
{
    [MathFunction("Distance")]
    public class DistanceFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        public override bool SupportsScalars => false;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var vectorType = $"{typeName}{dimension}";

            return $@"        /// <summary>Returns the distance between two {vectorType} points.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} Distance({vectorType} x, {vectorType} y)
        {{
            return Length(x - y);
        }}";
        }
    }
}