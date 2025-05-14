namespace SourceGenerator.MathFunctions;

[MathFunction("Saturate")]
public class SaturateFunctionGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    public override bool SupportsScalars => true;

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var typeName = GetTypeName(type);
        var returnType = dimension > 1 ? $"{typeName}{dimension}" : type;
        var oneValue = GetOneValue(type);
        var zeroValue = GetZeroValue(type);

        if (dimension == 1)
        {
            return $@"        /// <summary>Clamps a value between 0 and 1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} Saturate({type} x) {{ return Clamp(x, {zeroValue}, {oneValue}); }}";
        }
        else
        {
            var componentExpressions = string.Join(", ", components.Select(c => $"Saturate(x.{c})"));
            return $@"        /// <summary>Clamps each component of a vector between 0 and 1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} Saturate({returnType} x) {{ return new {returnType}({componentExpressions}); }}";
        }
    }

    private string GetZeroValue(string type)
    {
        return type == "float" ? "0f" : type == "double" ? "0.0" : "0";
    }

    private string GetOneValue(string type)
    {
        return type == "float" ? "1f" : type == "double" ? "1.0" : "1";
    }
}
