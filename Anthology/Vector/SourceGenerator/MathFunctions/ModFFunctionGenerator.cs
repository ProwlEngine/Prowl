namespace SourceGenerator.MathFunctions;

[MathFunction("ModF")]
public class ModFFunctionGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    public override bool SupportsScalars => true;

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var typeName = GetTypeName(type);
        var returnType = dimension > 1 ? $"{typeName}{dimension}" : type;

        if (dimension == 1)
        {
            return $@"        /// <summary>Splits a floating-point value into integer and fractional parts.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} ModF({type} x, out {type} integerPart)
        {{
            integerPart = Floor(x);
            return x - integerPart;
        }}";
        }
        else
        {
            var integerParts = string.Join(", ", components.Select(c => $"out integer.{c}"));
            var componentExpressions = string.Join(", ", components.Select(c => $"ModF(x.{c}, out integer.{c})"));
            return $@"        /// <summary>Splits a vector into integer and fractional parts componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} ModF({returnType} x, out {returnType} integer)
        {{
            integer = new {returnType}();
            return new {returnType}({componentExpressions});
        }}";
        }
    }
}
