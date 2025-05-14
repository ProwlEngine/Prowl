namespace SourceGenerator.MathFunctions;

[MathFunction("CeilToInt")]
public class CeilToIntFunctionGenerator : MathFunctionGenerator
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
            return $@"        /// <summary>Ceils a value to the nearest higher integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CeilToInt({type} x) {{ return (int){mathClass}.Ceiling(x); }}";
        }
        else
        {
            var componentExpressions = string.Join(", ", components.Select(c => $"CeilToInt(x.{c})"));
            return $@"        /// <summary>Ceils each component to the nearest higher integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} CeilToInt({typeName}{dimension} x) {{ return new {returnType}({componentExpressions}); }}";
        }
    }
}
