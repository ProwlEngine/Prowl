using System.Text;

namespace SourceGenerator.MathFunctions;

[MathFunction("Mul")]
public class MatrixMultiplicationGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double", "int", "uint" };
    public override int[] SupportedDimensions => new[] { 2, 3, 4 };
    public override bool SupportsScalars => true;

    private readonly HashSet<string> _generatedFunctions = new HashSet<string>();

    public override bool SupportsType(string type, int dimension)
    {
        return type != "bool" && SupportedTypes.Contains(type);
    }

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var sb = new StringBuilder();
        var typeName = GetTypeName(type);

        // Generate all multiplication operations
        GenerateScalarOperations(sb, type, typeName);
        GenerateVectorComponentWiseMul(sb, type, typeName);
        GenerateMatrixVectorOperations(sb, type, typeName);
        GenerateMatrixMatrixOperations(sb, type, typeName);

        return sb.ToString();
    }

    private void GenerateScalarOperations(StringBuilder sb, string type, string typeName)
    {
        var functionKey = $"Mul_{type}_{type}";
        if (!_generatedFunctions.Add(functionKey)) return;

        sb.AppendLine($"        /// <summary>Returns the result of scalar multiplication.</summary>");
        sb.AppendLine($"        /// <param name=\"a\">First scalar value.</param>");
        sb.AppendLine($"        /// <param name=\"b\">Second scalar value.</param>");
        sb.AppendLine($"        /// <returns>The result of a * b.</returns>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static {type} Mul({type} a, {type} b)");
        sb.AppendLine("        {");
        sb.AppendLine("            return a * b;");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    /// <summary>
    /// This method generates component-wise multiplication for vectors.
    /// </summary>
    private void GenerateVectorComponentWiseMul(StringBuilder sb, string type, string typeName)
    {
        for (int dim = 2; dim <= 4; dim++)
        {
            var vectorType = $"{typeName}{dim}";
            var functionKey = $"Mul_{vectorType}_{vectorType}";
            if (!_generatedFunctions.Add(functionKey)) continue;

            var components = GetComponents(dim);

            sb.AppendLine($"        /// <summary>Returns the component-wise multiplication of two {vectorType} vectors.</summary>");
            sb.AppendLine($"        /// <param name=\"a\">First vector.</param>");
            sb.AppendLine($"        /// <param name=\"b\">Second vector.</param>");
            sb.AppendLine($"        /// <returns>A new {vectorType} where each component is the product of the corresponding components in a and b.</returns>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static {vectorType} Mul({vectorType} a, {vectorType} b)");
            sb.AppendLine("        {");

            var mulTerms = new List<string>();
            for (int i = 0; i < dim; i++)
            {
                mulTerms.Add($"a.{components[i]} * b.{components[i]}");
            }

            sb.AppendLine($"            return new {vectorType}({string.Join(", ", mulTerms)});");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
    }

    private void GenerateMatrixVectorOperations(StringBuilder sb, string type, string typeName)
    {
        // Generate square matrix-vector operations
        for (int dim = 2; dim <= 4; dim++)
        {
            var matrixType = $"{typeName}{dim}x{dim}";
            var vectorType = $"{typeName}{dim}";
            var components = GetComponents(dim);

            // Matrix * Column Vector
            GenerateMatrixColumnVectorMul(sb, type, typeName, dim, matrixType, vectorType, components);

            // Row Vector * Matrix
            GenerateRowVectorMatrixMul(sb, type, typeName, dim, matrixType, vectorType, components);
        }

        // Generate non-square matrix-vector operations
        GenerateNonSquareMatrixVectorMul(sb, type, typeName);
    }

    private void GenerateMatrixColumnVectorMul(StringBuilder sb, string type, string typeName, int dim,
        string matrixType, string vectorType, string[] components)
    {
        var functionKey = $"Mul_{matrixType}_{vectorType}";
        if (!_generatedFunctions.Add(functionKey)) return;

        sb.AppendLine($"        /// <summary>Returns the result of a matrix-vector multiplication.</summary>");
        sb.AppendLine($"        /// <param name=\"m\">The matrix.</param>");
        sb.AppendLine($"        /// <param name=\"v\">The vector.</param>");
        sb.AppendLine($"        /// <returns>The result of m * v.</returns>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static {vectorType} Mul({matrixType} m, {vectorType} v)");
        sb.AppendLine("        {");

        sb.AppendLine("            return");
        var vectorOps = new List<string>();
        for (int col = 0; col < dim; col++)
        {
            vectorOps.Add($"m.c{col} * v.{components[col]}");
        }
        sb.AppendLine($"                {string.Join(" +\n                ", vectorOps)};");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private void GenerateRowVectorMatrixMul(StringBuilder sb, string type, string typeName, int dim,
        string matrixType, string vectorType, string[] components)
    {
        var functionKey = $"Mul_{vectorType}_{matrixType}";
        if (!_generatedFunctions.Add(functionKey)) return;

        sb.AppendLine($"        /// <summary>Returns the result of a row vector-matrix multiplication.</summary>");
        sb.AppendLine($"        /// <param name=\"rowVector\">The row vector.</param>");
        sb.AppendLine($"        /// <param name=\"matrix\">The matrix.</param>");
        sb.AppendLine($"        /// <returns>The result of rowVector * matrix.</returns>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static {vectorType} Mul({vectorType} rowVector, {matrixType} matrix)");
        sb.AppendLine("        {");

        var resultComponents = new List<string>();
        for (int col = 0; col < dim; col++)
        {
            var dotProduct = new List<string>();
            for (int row = 0; row < dim; row++)
            {
                dotProduct.Add($"rowVector.{components[row]} * matrix.c{col}.{components[row]}");
            }
            resultComponents.Add($"({string.Join(" + ", dotProduct)})");
        }

        sb.AppendLine($"            return new {vectorType}({string.Join(", ", resultComponents)});");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private void GenerateMatrixMatrixOperations(StringBuilder sb, string type, string typeName)
    {
        // Generate square matrix multiplication
        for (int dim = 2; dim <= 4; dim++)
        {
            GenerateSquareMatrixMul(sb, type, typeName, dim);
        }

        // Generate non-square matrix multiplication
        GenerateNonSquareMatrixMul(sb, type, typeName);
    }

    private void GenerateSquareMatrixMul(StringBuilder sb, string type, string typeName, int dim)
    {
        var matrixType = $"{typeName}{dim}x{dim}";
        var functionKey = $"Mul_{matrixType}_{matrixType}";
        if (!_generatedFunctions.Add(functionKey)) return;

        var components = GetComponents(dim);

        sb.AppendLine($"        /// <summary>Returns the result of a matrix-matrix multiplication.</summary>");
        sb.AppendLine($"        /// <param name=\"a\">The first matrix.</param>");
        sb.AppendLine($"        /// <param name=\"b\">The second matrix.</param>");
        sb.AppendLine($"        /// <returns>The result of a * b.</returns>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static {matrixType} Mul({matrixType} a, {matrixType} b)");
        sb.AppendLine("        {");

        sb.AppendLine($"            return new {matrixType}(");
        var columnConstructors = new List<string>();
        for (int col = 0; col < dim; col++)
        {
            var vectorOps = new List<string>();
            for (int k = 0; k < dim; k++)
            {
                vectorOps.Add($"a.c{k} * b.c{col}.{components[k]}");
            }
            columnConstructors.Add($"                {string.Join(" + ", vectorOps)}");
        }
        sb.AppendLine(string.Join(",\n", columnConstructors));
        sb.AppendLine("            );");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private void GenerateNonSquareMatrixMul(StringBuilder sb, string type, string typeName)
    {
        // Generate all valid non-square matrix multiplication combinations
        var allCombinations = new[]
        {
            // A(m×n) * B(n×p) = C(m×p)
            (2, 3, 3, 2), (2, 3, 3, 3), (2, 3, 3, 4),  // 2×3 * 3×? = 2×?
            (2, 4, 4, 2), (2, 4, 4, 3), (2, 4, 4, 4),  // 2×4 * 4×? = 2×?
            (3, 2, 2, 2), (3, 2, 2, 3), (3, 2, 2, 4),  // 3×2 * 2×? = 3×?
            (3, 4, 4, 2), (3, 4, 4, 3), (3, 4, 4, 4),  // 3×4 * 4×? = 3×?
            (4, 2, 2, 2), (4, 2, 2, 3), (4, 2, 2, 4),  // 4×2 * 2×? = 4×?
            (4, 3, 3, 2), (4, 3, 3, 3), (4, 3, 3, 4),  // 4×3 * 3×? = 4×?
        };

        foreach (var (aRows, aCols, bRows, bCols) in allCombinations)
        {
            if (aCols != bRows) continue; // Invalid multiplication

            var matrixAType = $"{typeName}{aRows}x{aCols}";
            var matrixBType = $"{typeName}{bRows}x{bCols}";
            var resultType = $"{typeName}{aRows}x{bCols}";
            var functionKey = $"Mul_{matrixAType}_{matrixBType}";

            if (!_generatedFunctions.Add(functionKey)) continue;

            sb.AppendLine($"        /// <summary>Returns the result of a matrix-matrix multiplication.</summary>");
            sb.AppendLine($"        /// <param name=\"a\">The first matrix ({matrixAType}).</param>");
            sb.AppendLine($"        /// <param name=\"b\">The second matrix ({matrixBType}).</param>");
            sb.AppendLine($"        /// <returns>The result of a * b ({resultType}).</returns>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static {resultType} Mul({matrixAType} a, {matrixBType} b)");
            sb.AppendLine("        {");

            var components = GetComponents(Math.Max(aRows, bRows));

            // Generate result columns
            sb.AppendLine($"            return new {resultType}(");
            var columnConstructors = new List<string>();
            for (int col = 0; col < bCols; col++)
            {
                var vectorOps = new List<string>();
                for (int k = 0; k < aCols; k++)
                {
                    vectorOps.Add($"a.c{k} * b.c{col}.{components[k]}");
                }
                columnConstructors.Add($"                {string.Join(" + ", vectorOps)}");
            }
            sb.AppendLine(string.Join(",\n", columnConstructors));
            sb.AppendLine("            );");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
    }

    private void GenerateNonSquareMatrixVectorMul(StringBuilder sb, string type, string typeName)
    {
        var nonSquareConfigs = new[]
        {
            (2, 3), (2, 4), (3, 2), (3, 4), (4, 2), (4, 3)
        };

        foreach (var (rows, cols) in nonSquareConfigs)
        {
            var matrixType = $"{typeName}{rows}x{cols}";
            var vectorType = $"{typeName}{cols}";
            var resultType = $"{typeName}{rows}";
            var functionKey = $"Mul_{matrixType}_{vectorType}";

            if (!_generatedFunctions.Add(functionKey)) continue;

            var components = GetComponents(Math.Max(rows, cols));

            sb.AppendLine($"        /// <summary>Returns the result of a matrix-vector multiplication.</summary>");
            sb.AppendLine($"        /// <param name=\"m\">The {matrixType} matrix.</param>");
            sb.AppendLine($"        /// <param name=\"v\">The {vectorType} vector.</param>");
            sb.AppendLine($"        /// <returns>The result of m * v ({resultType}).</returns>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static {resultType} Mul({matrixType} m, {vectorType} v)");
            sb.AppendLine("        {");

            sb.AppendLine("            return");
            var vectorOps = new List<string>();
            for (int col = 0; col < cols; col++)
            {
                vectorOps.Add($"m.c{col} * v.{components[col]}");
            }
            sb.AppendLine($"                {string.Join(" +\n                ", vectorOps)};");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
    }
}
