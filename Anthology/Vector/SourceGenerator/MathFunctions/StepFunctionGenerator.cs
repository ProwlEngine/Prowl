namespace SourceGenerator.MathFunctions;

[MathFunction("Step")]
public class StepFunctionGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    public override bool SupportsScalars => true;

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var typeName = GetTypeName(type);
        var returnType = dimension > 1 ? $"{typeName}{dimension}" : type;
        var zeroValue = type == "float" ? "0f" : "0.0";
        var oneValue = type == "float" ? "1f" : "1.0";

        if (dimension == 1)
        {
            return $@"        /// <summary>Returns 0 if x < edge, otherwise returns 1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} Step({type} edge, {type} x) {{ return x < edge ? {zeroValue} : {oneValue}; }}";
        }
        else
        {
            var componentExpressions = string.Join(", ", components.Select(c => $"Step(edge.{c}, x.{c})"));
            return $@"        /// <summary>Returns 0 or 1 for each component based on the step function.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} Step({returnType} edge, {returnType} x) {{ return new {returnType}({componentExpressions}); }}";
        }
    }
}
