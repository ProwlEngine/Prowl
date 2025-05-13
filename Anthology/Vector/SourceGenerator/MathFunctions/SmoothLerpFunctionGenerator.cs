namespace SourceGenerator.MathFunctions
{
    [MathFunction("SmoothLerp")]
    public class SmoothLerpFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        public override bool SupportsScalars => true;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var returnType = dimension > 1 ? $"{typeName}{dimension}" : type;

            if (dimension == 1)
            {
                return $@"        /// <summary>Smoothly interpolates between two values using cubic Hermite interpolation.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} SmoothLerp({type} a, {type} b, {type} t) 
        {{
            return Lerp(a, b, Smoothstep(0{(type == "float" ? "f" : "")}, 1{(type == "float" ? "f" : "")}, t));
        }}";
            }
            else
            {
                var componentExpressions = string.Join(", ", components.Select(c => $"SmoothLerp(a.{c}, b.{c}, t)"));
                return $@"        /// <summary>Smoothly interpolates between two vectors using cubic Hermite interpolation.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} SmoothLerp({returnType} a, {returnType} b, {type} t) {{ return new {returnType}({componentExpressions}); }}";
            }
        }
    }
}