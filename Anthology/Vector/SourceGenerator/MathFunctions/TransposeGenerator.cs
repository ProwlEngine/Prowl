namespace SourceGenerator.MathFunctions
{
    [MathFunction("transpose")]
    public class TransposeGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        public override int[] SupportedDimensions => new[] { 2, 3, 4 };
        public override bool SupportsScalars => false;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            return $@"
        /// <summary>Returns the transpose of a {type}{dimension}x{dimension} matrix.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {GetTypeName(type)}{dimension}x{dimension} Transpose({GetTypeName(type)}{dimension}x{dimension} m)
        {{
            return new {GetTypeName(type)}{dimension}x{dimension}({GenerateTransposeArgs(type, dimension)});
        }}";
        }

        private string GenerateTransposeArgs(string type, int dimension)
        {
            var args = new List<string>();
            var typeName = GetTypeName(type);
            var vectorType = $"{typeName}{dimension}";

            for (int row = 0; row < dimension; row++)
            {
                var rowComponents = new List<string>();
                for (int col = 0; col < dimension; col++)
                {
                    rowComponents.Add($"m.c{col}.{GetComponents(dimension)[row]}");
                }
                args.Add($"new {vectorType}({string.Join(", ", rowComponents)})");
            }
            return string.Join(",\n            ", args);
        }
    }
}