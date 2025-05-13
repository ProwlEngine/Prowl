namespace SourceGenerator.MathFunctions
{
    [MathFunction("Lerp")]
    public class LerpFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        public override bool SupportsScalars => true;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var returnType = dimension > 1 ? $"{typeName}{dimension}" : type;

            if (dimension == 1)
            {
                return $@"        /// <summary>Linearly interpolates between two {type} values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} Lerp({type} a, {type} b, {type} t) {{ return a + (b - a) * Saturate(t); }}";
            }
            else
            {
                var componentExpressions = string.Join(", ", components.Select(c => $"Lerp(a.{c}, b.{c}, Saturate(t))"));
                return $@"        /// <summary>Linearly interpolates between two {returnType} vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} Lerp({returnType} a, {returnType} b, {type} t) {{ return new {returnType}({componentExpressions}); }}";
            }
        }
    }
}