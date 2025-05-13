namespace SourceGenerator.MathFunctions
{
    [MathFunction("PingPong")]
    public class PingPongFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        public override bool SupportsScalars => true;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var returnType = dimension > 1 ? $"{typeName}{dimension}" : type;

            if (dimension == 1)
            {
                return $@"        /// <summary>PingPongs the value t, so that it is never larger than length and never smaller than 0.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} PingPong({type} t, {type} length)
        {{
            t = Repeat(t, length * 2{(type == "float" ? "f" : "")});
            return length - Abs(t - length);
        }}";
            }
            else
            {
                var componentExpressions = string.Join(", ", components.Select(c => $"PingPong(t.{c}, length.{c})"));
                return $@"        /// <summary>PingPongs the vector t, so that each component is never larger than length and never smaller than 0.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} PingPong({returnType} t, {returnType} length) {{ return new {returnType}({componentExpressions}); }}";
            }
        }
    }
}