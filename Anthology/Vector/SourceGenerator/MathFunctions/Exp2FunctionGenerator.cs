namespace SourceGenerator.MathFunctions
{
    [MathFunction("Exp2")]
    public class Exp2FunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        public override bool SupportsScalars => true;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var returnType = dimension > 1 ? $"{typeName}{dimension}" : type;
            var mathClass = GetMathClass(type);

            if (dimension == 1)
            {
                return $@"        /// <summary>Returns 2 raised to the power of x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} Exp2({type} x) {{ return {mathClass}.Pow(2{(type == "float" ? "f" : "")}, x); }}";
            }
            else
            {
                var componentExpressions = string.Join(", ", components.Select(c => $"Exp2(x.{c})"));
                return $@"        /// <summary>Returns the componentwise power of 2 raised to x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} Exp2({returnType} x) {{ return new {returnType}({componentExpressions}); }}";
            }
        }
    }
}