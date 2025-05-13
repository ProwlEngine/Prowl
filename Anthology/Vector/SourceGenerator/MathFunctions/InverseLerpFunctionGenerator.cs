namespace SourceGenerator.MathFunctions
{
    [MathFunction("InverseLerp")]
    public class InverseLerpFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        public override bool SupportsScalars => true;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var returnType = dimension > 1 ? $"{typeName}{dimension}" : type;

            if (dimension == 1)
            {
                return $@"        /// <summary>Finds the t value given a, b, and the result of a lerp.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} InverseLerp({type} a, {type} b, {type} value)
        {{
            if (a != b)
                return (value - a) / (b - a);
            else
                return 0{(type == "float" ? "f" : "")};
        }}";
            }
            else
            {
                var componentExpressions = string.Join(", ", components.Select(c => $"InverseLerp(a.{c}, b.{c}, value.{c})"));
                return $@"        /// <summary>Finds the t value for each component given a, b, and the result of a lerp.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} InverseLerp({returnType} a, {returnType} b, {returnType} value)
        {{
            return new {returnType}({componentExpressions});
        }}";
            }
        }
    }
}