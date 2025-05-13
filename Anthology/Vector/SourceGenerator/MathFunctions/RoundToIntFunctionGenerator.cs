namespace SourceGenerator.MathFunctions
{
    [MathFunction("RoundToInt")]
    public class RoundToIntFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        public override bool SupportsScalars => true;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var returnType = dimension > 1 ? $"Int{dimension}" : "int";
            var mathClass = GetMathClass(type);

            if (dimension == 1)
            {
                return $@"        /// <summary>Rounds a value to the nearest integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int RoundToInt({type} x) {{ return (int){mathClass}.Round(x); }}";
            }
            else
            {
                var componentExpressions = string.Join(", ", components.Select(c => $"RoundToInt(x.{c})"));
                return $@"        /// <summary>Rounds each component to the nearest integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} RoundToInt({typeName}{dimension} x) {{ return new {returnType}({componentExpressions}); }}";
            }
        }
    }
}