namespace SourceGenerator.MathFunctions;

[MathFunction("DeltaAngle")]
public class DeltaAngleFunctionGenerator : MathFunctionGenerator
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
            return $@"        /// <summary>Calculates the shortest angle between two angles.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} DeltaAngle({type} current, {type} target)
        {{
            {type} delta = Repeat(target - current, {pi2Value});
            if (delta > {piValue})
                delta -= {pi2Value};
            return delta;
        }}";
        }
        else
        {
            var componentExpressions = string.Join(", ", components.Select(c => $"DeltaAngle(current.{c}, target.{c})"));
            return $@"        /// <summary>Calculates the shortest angle between two angle vectors componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} DeltaAngle({returnType} current, {returnType} target) {{ return new {returnType}({componentExpressions}); }}";
        }
    }
}
