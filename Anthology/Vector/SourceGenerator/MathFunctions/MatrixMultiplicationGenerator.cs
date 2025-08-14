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

    #region Tests

    private static readonly HashSet<string> _generatedTestMethods = new HashSet<string>();

    public override List<string> GenerateTestMethods(string type, int dimension, string[] components)
    {
        var tests = new List<string>();
        var typeName = GetTypeName(type);
        var assertEqualMethod = GetXUnitAssertEqual(type);

        // Only generate tests once per type (not per dimension)
        var typeKey = $"MatrixMul_{type}";
        if (!_generatedTestMethods.Add(typeKey))
        {
            return tests; // Already generated for this type
        }

        // Generate scalar multiplication tests
        GenerateScalarMultiplicationTests(tests, type, typeName, assertEqualMethod);

        // Generate vector component-wise multiplication tests
        GenerateVectorComponentWiseTests(tests, type, typeName, assertEqualMethod);

        // Generate matrix-vector multiplication tests
        GenerateMatrixVectorTests(tests, type, typeName, assertEqualMethod);

        // Generate matrix-matrix multiplication tests
        GenerateMatrixMatrixTests(tests, type, typeName, assertEqualMethod);

        return tests;
    }

    private void GenerateScalarMultiplicationTests(List<string> tests, string type, string typeName, string assertEqualMethod)
    {
        var a = GetTestValue(type, 0);
        var b = GetTestValue(type, 1);

        tests.Add($@"        [Fact]
        public void Mul_{typeName}ScalarTest()
        {{
            {type} a = {a};
            {type} b = {b};
            {type} result = Maths.Mul(a, b);
            {type} expected = a * b;
            {assertEqualMethod.Replace("{expected}", "expected").Replace("{actual}", "result")}
        }}");
    }

    private void GenerateVectorComponentWiseTests(List<string> tests, string type, string typeName, string assertEqualMethod)
    {
        for (int dim = 2; dim <= 4; dim++)
        {
            var vectorType = $"{typeName}{dim}";
            var components = GetComponents(dim);

            var aParams = string.Join(", ", components.Select((c, i) => GetTestValue(type, i)));
            var bParams = string.Join(", ", components.Select((c, i) => GetTestValue(type, i + dim)));

            tests.Add($@"        [Fact]
        public void Mul_{typeName}{dim}ComponentWiseTest()
        {{
            {vectorType} a = new {vectorType}({aParams});
            {vectorType} b = new {vectorType}({bParams});
            {vectorType} result = Maths.Mul(a, b);
            
            {string.Join("\n            ", components.Select((c, i) =>
                    $"{type} expected{c} = {GetTestValue(type, i)} * {GetTestValue(type, i + dim)};\n            " +
                    assertEqualMethod.Replace("{expected}", $"expected{c}").Replace("{actual}", $"result.{c}")
                ))}
        }}");
        }
    }

    private void GenerateMatrixVectorTests(List<string> tests, string type, string typeName, string assertEqualMethod)
    {
        // Square matrix-vector tests
        for (int dim = 2; dim <= 4; dim++)
        {
            var matrixType = $"{typeName}{dim}x{dim}";
            var vectorType = $"{typeName}{dim}";
            var components = GetComponents(dim);

            // Matrix * Column Vector test
            GenerateMatrixColumnVectorTest(tests, type, typeName, dim, matrixType, vectorType, components, assertEqualMethod);

            // Row Vector * Matrix test
            GenerateRowVectorMatrixTest(tests, type, typeName, dim, matrixType, vectorType, components, assertEqualMethod);
        }

        // Non-square matrix-vector tests
        GenerateNonSquareMatrixVectorTests(tests, type, typeName, assertEqualMethod);
    }

    private void GenerateMatrixColumnVectorTest(List<string> tests, string type, string typeName, int dim,
        string matrixType, string vectorType, string[] components, string assertEqualMethod)
    {
        var vectorParams = string.Join(", ", components.Select((c, i) => GetTestValue(type, i)));

        // Create matrix columns
        var matrixColumns = new List<string>();
        for (int col = 0; col < dim; col++)
        {
            var columnParams = string.Join(", ", components.Select((c, i) => GetTestValue(type, col * dim + i + 10)));
            matrixColumns.Add($"new {vectorType}({columnParams})");
        }

        tests.Add($@"        [Fact]
        public void Mul_Matrix{dim}x{dim}Vector{dim}_{typeName}Test()
        {{
            {matrixType} matrix = new {matrixType}({string.Join(", ", matrixColumns)});
            {vectorType} vector = new {vectorType}({vectorParams});
            {vectorType} result = Maths.Mul(matrix, vector);
            
            // Expected result: matrix * vector
            {string.Join("\n            ", components.Select((c, row) =>
        {
            var dotProductTerms = new List<string>();
            for (int col = 0; col < dim; col++)
            {
                dotProductTerms.Add($"{GetTestValue(type, col * dim + row + 10)} * {GetTestValue(type, col)}");
            }
            return $"{type} expected{c} = {string.Join(" + ", dotProductTerms)};\n            " +
                   assertEqualMethod.Replace("{expected}", $"expected{c}").Replace("{actual}", $"result.{c}");
        }))}
        }}");
    }

    private void GenerateRowVectorMatrixTest(List<string> tests, string type, string typeName, int dim,
        string matrixType, string vectorType, string[] components, string assertEqualMethod)
    {
        var vectorParams = string.Join(", ", components.Select((c, i) => GetTestValue(type, i + 20)));

        // Create matrix columns
        var matrixColumns = new List<string>();
        for (int col = 0; col < dim; col++)
        {
            var columnParams = string.Join(", ", components.Select((c, i) => GetTestValue(type, col * dim + i + 30)));
            matrixColumns.Add($"new {vectorType}({columnParams})");
        }

        tests.Add($@"        [Fact]
        public void Mul_Vector{dim}Matrix{dim}x{dim}_{typeName}Test()
        {{
            {vectorType} rowVector = new {vectorType}({vectorParams});
            {matrixType} matrix = new {matrixType}({string.Join(", ", matrixColumns)});
            {vectorType} result = Maths.Mul(rowVector, matrix);
            
            // Expected result: rowVector * matrix
            {string.Join("\n            ", components.Select((c, col) =>
        {
            var dotProductTerms = new List<string>();
            for (int row = 0; row < dim; row++)
            {
                dotProductTerms.Add($"{GetTestValue(type, row + 20)} * {GetTestValue(type, col * dim + row + 30)}");
            }
            return $"{type} expected{c} = {string.Join(" + ", dotProductTerms)};\n            " +
                   assertEqualMethod.Replace("{expected}", $"expected{c}").Replace("{actual}", $"result.{c}");
        }))}
        }}");
    }

    private void GenerateMatrixMatrixTests(List<string> tests, string type, string typeName, string assertEqualMethod)
    {
        // Square matrix multiplication tests
        for (int dim = 2; dim <= 4; dim++)
        {
            GenerateSquareMatrixTest(tests, type, typeName, dim, assertEqualMethod);
        }

        // Generate a few representative non-square matrix tests (to avoid too many tests)
        GenerateSelectedNonSquareMatrixTests(tests, type, typeName, assertEqualMethod);
    }

    private void GenerateSquareMatrixTest(List<string> tests, string type, string typeName, int dim, string assertEqualMethod)
    {
        var matrixType = $"{typeName}{dim}x{dim}";
        var vectorType = $"{typeName}{dim}";
        var components = GetComponents(dim);

        // Create first matrix
        var matrixAColumns = new List<string>();
        for (int col = 0; col < dim; col++)
        {
            var columnParams = string.Join(", ", components.Select((c, i) => GetTestValue(type, col * dim + i + 40)));
            matrixAColumns.Add($"new {vectorType}({columnParams})");
        }

        // Create second matrix
        var matrixBColumns = new List<string>();
        for (int col = 0; col < dim; col++)
        {
            var columnParams = string.Join(", ", components.Select((c, i) => GetTestValue(type, col * dim + i + 50)));
            matrixBColumns.Add($"new {vectorType}({columnParams})");
        }

        tests.Add($@"        [Fact]
        public void Mul_Matrix{dim}x{dim}Matrix{dim}x{dim}_{typeName}Test()
        {{
            {matrixType} a = new {matrixType}({string.Join(", ", matrixAColumns)});
            {matrixType} b = new {matrixType}({string.Join(", ", matrixBColumns)});
            {matrixType} result = Maths.Mul(a, b);
            
            // Expected result: a * b
            {string.Join("\n            ", Enumerable.Range(0, dim).SelectMany(col =>
                    components.Select((c, row) =>
                    {
                        var sumTerms = new List<string>();
                        for (int k = 0; k < dim; k++)
                        {
                            sumTerms.Add($"{GetTestValue(type, k * dim + row + 40)} * {GetTestValue(type, col * dim + k + 50)}");
                        }
                        return $"{type} expectedC{col}{c} = {string.Join(" + ", sumTerms)};\n            " +
                               assertEqualMethod.Replace("{expected}", $"expectedC{col}{c}").Replace("{actual}", $"result.c{col}.{c}");
                    })
                ))}
        }}");
    }

    private void GenerateNonSquareMatrixVectorTests(List<string> tests, string type, string typeName, string assertEqualMethod)
    {
        var nonSquareConfigs = new[]
        {
        (2, 3), (3, 2), (2, 4), (4, 2) // Just test a few representative cases
    };

        foreach (var (rows, cols) in nonSquareConfigs)
        {
            var matrixType = $"{typeName}{rows}x{cols}";
            var vectorType = $"{typeName}{cols}";
            var resultType = $"{typeName}{rows}";
            var vectorComponents = GetComponents(cols);
            var resultComponents = GetComponents(rows);

            var vectorParams = string.Join(", ", vectorComponents.Select((c, i) => GetTestValue(type, i + 60)));

            // Create matrix columns
            var matrixColumns = new List<string>();
            for (int col = 0; col < cols; col++)
            {
                var columnParams = string.Join(", ", resultComponents.Select((c, i) => GetTestValue(type, col * rows + i + 70)));
                matrixColumns.Add($"new {resultType}({columnParams})");
            }

            tests.Add($@"        [Fact]
        public void Mul_Matrix{rows}x{cols}Vector{cols}_{typeName}Test()
        {{
            {matrixType} matrix = new {matrixType}({string.Join(", ", matrixColumns)});
            {vectorType} vector = new {vectorType}({vectorParams});
            {resultType} result = Maths.Mul(matrix, vector);
            
            // Expected result: matrix * vector
            {string.Join("\n            ", resultComponents.Select((c, row) =>
            {
                var sumTerms = new List<string>();
                for (int col = 0; col < cols; col++)
                {
                    sumTerms.Add($"{GetTestValue(type, col * rows + row + 70)} * {GetTestValue(type, col + 60)}");
                }
                return $"{type} expected{c} = {string.Join(" + ", sumTerms)};\n            " +
                       assertEqualMethod.Replace("{expected}", $"expected{c}").Replace("{actual}", $"result.{c}");
            }))}
        }}");
        }
    }

    private void GenerateSelectedNonSquareMatrixTests(List<string> tests, string type, string typeName, string assertEqualMethod)
    {
        // Just test a few representative non-square matrix multiplications to avoid generating too many tests
        var selectedCombinations = new[]
        {
        (2, 3, 3, 2), // 2x3 * 3x2 = 2x2
        (3, 2, 2, 4), // 3x2 * 2x4 = 3x4
    };

        foreach (var (aRows, aCols, bRows, bCols) in selectedCombinations)
        {
            if (aCols != bRows) continue;

            var matrixAType = $"{typeName}{aRows}x{aCols}";
            var matrixBType = $"{typeName}{bRows}x{bCols}";
            var resultType = $"{typeName}{aRows}x{bCols}";
            var aColType = $"{typeName}{aRows}";
            var bColType = $"{typeName}{bRows}";
            var resultColType = $"{typeName}{aRows}";

            var aComponents = GetComponents(aRows);
            var bComponents = GetComponents(bRows);

            // Create matrix A columns
            var matrixAColumns = new List<string>();
            for (int col = 0; col < aCols; col++)
            {
                var columnParams = string.Join(", ", aComponents.Select((c, i) => GetTestValue(type, col * aRows + i + 80)));
                matrixAColumns.Add($"new {aColType}({columnParams})");
            }

            // Create matrix B columns
            var matrixBColumns = new List<string>();
            for (int col = 0; col < bCols; col++)
            {
                var columnParams = string.Join(", ", bComponents.Select((c, i) => GetTestValue(type, col * bRows + i + 90)));
                matrixBColumns.Add($"new {bColType}({columnParams})");
            }

            tests.Add($@"        [Fact]
        public void Mul_Matrix{aRows}x{aCols}Matrix{bRows}x{bCols}_{typeName}Test()
        {{
            {matrixAType} a = new {matrixAType}({string.Join(", ", matrixAColumns)});
            {matrixBType} b = new {matrixBType}({string.Join(", ", matrixBColumns)});
            {resultType} result = Maths.Mul(a, b);
            
            // Expected result: a * b
            {string.Join("\n            ", Enumerable.Range(0, bCols).SelectMany(col =>
                    aComponents.Select((c, row) =>
                    {
                        var sumTerms = new List<string>();
                        for (int k = 0; k < aCols; k++)
                        {
                            sumTerms.Add($"{GetTestValue(type, k * aRows + row + 80)} * {GetTestValue(type, col * bRows + k + 90)}");
                        }
                        return $"{type} expectedC{col}{c} = {string.Join(" + ", sumTerms)};\n            " +
                               assertEqualMethod.Replace("{expected}", $"expectedC{col}{c}").Replace("{actual}", $"result.c{col}.{c}");
                    })
                ))}
        }}");
        }
    }

    #endregion
}
