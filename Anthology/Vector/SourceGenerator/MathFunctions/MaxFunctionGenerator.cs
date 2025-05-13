namespace SourceGenerator.MathFunctions
{
    [MathFunction("Max")]
    public class MaxFunctionGenerator : SimpleMathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double", "int", "byte", "ushort", "uint", "ulong" };
        protected override string MathMethodName => "Max";
        protected override bool RequiresTwoParameters => true;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var mathClass = GetMathClass(type);
            var returnType = dimension > 1 ? $"{typeName}{dimension}" : type;

            if (dimension == 1)
            {
                return $@"        /// <summary>Returns the maximum of two {type} values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} Max({type} x, {type} y) {{ return {mathClass}.Max(x, y); }}";
            }
            else
            {
                // Vector-to-vector max
                var vectorComponentExpressions = string.Join(", ", components.Select(c => $"Max(x.{c}, y.{c})"));
                var vectorToVectorMax = $@"        /// <summary>Returns the componentwise maximum of two {returnType} vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} Max({returnType} x, {returnType} y) {{ return new {returnType}({vectorComponentExpressions}); }}";

                // Vector-to-scalar max
                var scalarComponentExpressions = string.Join(", ", components.Select(c => $"Max(x.{c}, scalar)"));
                var vectorToScalarMax = $@"        /// <summary>Returns the componentwise maximum of a {returnType} vector and a scalar value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} Max({returnType} x, {type} scalar) {{ return new {returnType}({scalarComponentExpressions}); }}";

                return vectorToVectorMax + Environment.NewLine + Environment.NewLine + vectorToScalarMax;
            }
        }
    }
}