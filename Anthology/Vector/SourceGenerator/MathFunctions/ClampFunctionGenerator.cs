namespace SourceGenerator.MathFunctions
{
    [MathFunction("Clamp")]
    public class ClampFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double", "int", "byte", "ushort", "uint", "ulong" };
        public override bool SupportsScalars => true;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var mathClass = GetMathClass(type);
            var returnType = dimension > 1 ? $"{typeName}{dimension}" : type;

            if (dimension == 1)
            {
                return $@"        /// <summary>Clamps {type} x between min and max values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} Clamp({type} x, {type} min, {type} max) {{ return ({type})Math.Clamp(x, min, max); }}";
            }
            else
            {
                // Vector-to-vector clamp
                var componentExpressions = string.Join(", ", components.Select(c => $"Clamp(x.{c}, min.{c}, max.{c})"));
                var vectorToVectorClamp = $@"        /// <summary>Returns the componentwise clamp of a {returnType} vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} Clamp({returnType} x, {returnType} min, {returnType} max) {{ return new {returnType}({componentExpressions}); }}";

                // Vector-to-scalar clamp
                var scalarComponentExpressions = string.Join(", ", components.Select(c => $"Clamp(x.{c}, min, max)"));
                var vectorToScalarClamp = $@"        /// <summary>Clamps each component of a {returnType} vector between scalar min and max values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} Clamp({returnType} x, {type} min, {type} max) {{ return new {returnType}({scalarComponentExpressions}); }}";

                return vectorToVectorClamp + Environment.NewLine + Environment.NewLine + vectorToScalarClamp;
            }
        }
    }
}