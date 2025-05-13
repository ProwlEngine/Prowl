namespace SourceGenerator.MathFunctions
{
    [MathFunction("FloorToInt")]
    public class FloorToIntFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        public override bool SupportsScalars => true;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var returnType = dimension > 1 ? $"Int{dimension}" : "int";
            var mathClass = GetMathClass(type);

            if (dimension == 1)
            {
                return $@"        /// <summary>Floors a value to the nearest lower integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FloorToInt({type} x) {{ return (int){mathClass}.Floor(x); }}";
            }
            else
            {
                var componentExpressions = string.Join(", ", components.Select(c => $"FloorToInt(x.{c})"));
                return $@"        /// <summary>Floors each component to the nearest lower integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} FloorToInt({typeName}{dimension} x) {{ return new {returnType}({componentExpressions}); }}";
            }
        }
    }
}