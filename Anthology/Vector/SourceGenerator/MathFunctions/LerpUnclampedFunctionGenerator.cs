namespace SourceGenerator.MathFunctions
{
    [MathFunction("LerpUnclamped")]
    public class LerpUnclampedFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        public override bool SupportsScalars => true;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var returnType = dimension > 1 ? $"{typeName}{dimension}" : type;

            if (dimension == 1)
            {
                return $@"        /// <summary>Linearly interpolates between two values without clamping t.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} LerpUnclamped({type} a, {type} b, {type} t) {{ return a + (b - a) * t; }}";
            }
            else
            {
                var componentExpressions = string.Join(", ", components.Select(c => $"LerpUnclamped(a.{c}, b.{c}, t)"));
                return $@"        /// <summary>Linearly interpolates between two vectors without clamping t.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} LerpUnclamped({returnType} a, {returnType} b, {type} t) {{ return new {returnType}({componentExpressions}); }}";
            }
        }
    }
}