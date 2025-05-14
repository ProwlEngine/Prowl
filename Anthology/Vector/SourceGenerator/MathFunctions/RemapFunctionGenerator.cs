namespace SourceGenerator.MathFunctions;

[MathFunction("Remap")]
public class RemapFunctionGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    public override bool SupportsScalars => true;

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var typeName = GetTypeName(type);
        var returnType = dimension > 1 ? $"{typeName}{dimension}" : type;

        if (dimension == 1)
        {
            return $@"        /// <summary>Remaps a value from one range to another.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} Remap({type} value, {type} inputMin, {type} inputMax, {type} outputMin, {type} outputMax)
        {{
            return outputMin + ((value - inputMin) * (outputMax - outputMin) / (inputMax - inputMin));
        }}";
        }
        else
        {
            var componentExpressions = string.Join(", ", components.Select(c => $"Remap(value.{c}, inputMin.{c}, inputMax.{c}, outputMin.{c}, outputMax.{c})"));
            return $@"        /// <summary>Remaps a vector from one range to another componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} Remap({returnType} value, {returnType} inputMin, {returnType} inputMax, {returnType} outputMin, {returnType} outputMax)
        {{
            return new {returnType}({componentExpressions});
        }}";
        }
    }
}
