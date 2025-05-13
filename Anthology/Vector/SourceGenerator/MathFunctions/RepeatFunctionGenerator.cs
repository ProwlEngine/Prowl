namespace SourceGenerator.MathFunctions
{
    [MathFunction("Repeat")]
    public class RepeatFunctionGenerator : MathFunctionGenerator
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
                return $@"        /// <summary>Wraps the given value between 0 and length.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} Repeat({type} t, {type} length)
        {{
            return Clamp(t - {mathClass}.Floor(t / length) * length, 0{(type == "float" ? "f" : "")}, length);
        }}";
            }
            else
            {
                var componentExpressions = string.Join(", ", components.Select(c => $"Repeat(t.{c}, length.{c})"));
                return $@"        /// <summary>Wraps the given vector between 0 and length componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} Repeat({returnType} t, {returnType} length) {{ return new {returnType}({componentExpressions}); }}";
            }
        }
    }
}