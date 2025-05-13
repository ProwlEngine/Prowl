namespace SourceGenerator.MathFunctions
{
    [MathFunction("ToRadians")]
    public class ToRadiansFunctionGenerator : MathFunctionGenerator
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
                return $@"        /// <summary>Converts degrees to radians.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} ToRadians({type} degrees) {{ return degrees * {mathClass}.PI / 180{suffix}; }}";
            }
            else
            {
                var componentExpressions = string.Join(", ", components.Select(c => $"ToRadians(degrees.{c})"));
                return $@"        /// <summary>Converts degrees to radians componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} ToRadians({returnType} degrees) {{ return new {returnType}({componentExpressions}); }}";
            }
        }
    }
}