namespace SourceGenerator.MathFunctions
{
    [MathFunction("ToDegrees")]
    public class ToDegreesFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        public override bool SupportsScalars => true;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var returnType = dimension > 1 ? $"{typeName}{dimension}" : type;
            var mathClass = GetMathClass(type);
            var suffix = type == "float" ? "f" : "";

            if (dimension == 1)
            {
                return $@"        /// <summary>Converts radians to degrees.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} ToDegrees({type} radians) {{ return radians * 180{suffix} / {mathClass}.PI; }}";
            }
            else
            {
                var componentExpressions = string.Join(", ", components.Select(c => $"ToDegrees(radians.{c})"));
                return $@"        /// <summary>Converts radians to degrees componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} ToDegrees({returnType} radians) {{ return new {returnType}({componentExpressions}); }}";
            }
        }
    }
}