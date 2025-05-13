namespace SourceGenerator.MathFunctions
{
    [MathFunction("Min")]
    public class MinFunctionGenerator : SimpleMathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double", "int", "byte", "ushort", "uint", "ulong" };
        protected override string MathMethodName => "Min";
        protected override bool RequiresTwoParameters => true;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var mathClass = GetMathClass(type);
            var returnType = dimension > 1 ? $"{typeName}{dimension}" : type;

            if (dimension == 1)
            {
                return $@"        /// <summary>Returns the minimum of two {type} values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} Min({type} x, {type} y) {{ return {mathClass}.Min(x, y); }}";
            }
            else
            {
                // Vector-to-vector min
                var vectorComponentExpressions = string.Join(", ", components.Select(c => $"Min(x.{c}, y.{c})"));
                var vectorToVectorMin = $@"        /// <summary>Returns the componentwise minimum of two {returnType} vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} Min({returnType} x, {returnType} y) {{ return new {returnType}({vectorComponentExpressions}); }}";

                // Vector-to-scalar min
                var scalarComponentExpressions = string.Join(", ", components.Select(c => $"Min(x.{c}, scalar)"));
                var vectorToScalarMin = $@"        /// <summary>Returns the componentwise minimum of a {returnType} vector and a scalar value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} Min({returnType} x, {type} scalar) {{ return new {returnType}({scalarComponentExpressions}); }}";

                return vectorToVectorMin + Environment.NewLine + Environment.NewLine + vectorToScalarMin;
            }
        }
    }
}