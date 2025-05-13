namespace SourceGenerator.MathFunctions
{
    [MathFunction("FMod")]
    public class FModFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        public override bool SupportsScalars => true;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var returnType = dimension > 1 ? $"{typeName}{dimension}" : type;

            if (dimension == 1)
            {
                return $@"        /// <summary>Returns the floating-point remainder of x/y.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} FMod({type} x, {type} y) {{ return x % y; }}";
            }
            else
            {
                var componentExpressions = string.Join(", ", components.Select(c => $"FMod(x.{c}, y.{c})"));
                return $@"        /// <summary>Returns the componentwise floating-point remainder of x/y.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} FMod({returnType} x, {returnType} y) {{ return new {returnType}({componentExpressions}); }}";
            }
        }
    }
}