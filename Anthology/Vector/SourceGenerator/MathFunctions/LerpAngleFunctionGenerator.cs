namespace SourceGenerator.MathFunctions
{
    [MathFunction("LerpAngle")]
    public class LerpAngleFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        public override bool SupportsScalars => true;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var returnType = dimension > 1 ? $"{typeName}{dimension}" : type;
            var piValue = $"{GetMathClass(type)}.PI";
            var pi2Value = $"2{(type == "float" ? "f" : "")} * {piValue}";

            if (dimension == 1)
            {
                return $@"        /// <summary>Linearly interpolates between two angles, taking the shortest path around the circle.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} LerpAngle({type} a, {type} b, {type} t)
        {{
            {type} delta = Repeat(b - a, {pi2Value});
            if (delta > {piValue})
                delta -= {pi2Value};
            return a + delta * Saturate(t);
        }}";
            }
            else
            {
                var componentExpressions = string.Join(", ", components.Select(c => $"LerpAngle(a.{c}, b.{c}, t)"));
                return $@"        /// <summary>Linearly interpolates between two angle vectors, taking the shortest path for each component.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} LerpAngle({returnType} a, {returnType} b, {type} t) {{ return new {returnType}({componentExpressions}); }}";
            }
        }
    }
}