using System.Text;

namespace SourceGenerator.MathFunctions;

[MathFunction("Inverse")]
public class MatrixInverseGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    public override int[] SupportedDimensions => new[] { 2, 3, 4 };
    public override bool SupportsScalars => false;

    private readonly HashSet<string> _generatedFunctions = new HashSet<string>();

    public override bool SupportsType(string type, int dimension)
    {
        return (type == "float" || type == "double") && SupportedDimensions.Contains(dimension);
    }

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var sb = new StringBuilder();
        var typeName = GetTypeName(type);

        foreach (int size in SupportedDimensions)
        {
            var matrixType = $"{typeName}{size}x{size}";
            var functionKey = $"Inverse_{matrixType}";

            if (!_generatedFunctions.Add(functionKey)) continue;

            sb.AppendLine(GenerateInverseFunction(type, typeName, size, matrixType));
        }

        return sb.ToString();
    }

    private string GenerateInverseFunction(string primitiveType, string typeName, int size, string matrixType)
    {
        var sb = new StringBuilder();
        var vectorType = $"{typeName}{size}";
        var epsilon = primitiveType == "float" ? "1e-6f" : "1e-14";
        var mathClass = primitiveType == "float" ? "MathF" : "Math";

        sb.AppendLine($"        /// <summary>Returns the inverse of a {matrixType} matrix using analytical formulas.</summary>");
        sb.AppendLine($"        /// <param name=\"matrix\">The matrix to invert.</param>");
        sb.AppendLine($"        /// <returns>The inverse matrix.</returns>");
        sb.AppendLine($"        /// <exception cref=\"ArgumentException\">Thrown when the matrix is singular (non-invertible).</exception>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static {matrixType} Inverse({matrixType} matrix)");
        sb.AppendLine("        {");

        switch (size)
        {
            case 2:
                sb.AppendLine(Generate2x2Inverse(primitiveType, matrixType, vectorType, epsilon, mathClass));
                break;
            case 3:
                sb.AppendLine(Generate3x3Inverse(primitiveType, matrixType, vectorType, epsilon, mathClass));
                break;
            case 4:
                sb.AppendLine(Generate4x4Inverse(primitiveType, matrixType, vectorType, epsilon, mathClass));
                break;
        }

        sb.AppendLine("        }");
        sb.AppendLine();

        return sb.ToString();
    }

    private string Generate2x2Inverse(string primitiveType, string matrixType, string vectorType, string epsilon, string mathClass)
    {
        return $@"            // 2x2 analytical inverse
            var a = matrix.c0.X; var b = matrix.c1.X;
            var c = matrix.c0.Y; var d = matrix.c1.Y;
            
            var det = a * d - b * c;
            if ({mathClass}.Abs(det) <= {epsilon})
                throw new ArgumentException(""Matrix is singular and cannot be inverted."");
            
            var invDet = {GetOneValue(primitiveType)} / det;
            
            return new {matrixType}(
                new {vectorType}(d * invDet, -c * invDet),
                new {vectorType}(-b * invDet, a * invDet)
            );";
    }

    private string Generate3x3Inverse(string primitiveType, string matrixType, string vectorType, string epsilon, string mathClass)
    {
        return $@"            // 3x3 analytical inverse using cofactor expansion
            var m00 = matrix.c0.X; var m01 = matrix.c1.X; var m02 = matrix.c2.X;
            var m10 = matrix.c0.Y; var m11 = matrix.c1.Y; var m12 = matrix.c2.Y;
            var m20 = matrix.c0.Z; var m21 = matrix.c1.Z; var m22 = matrix.c2.Z;
            
            // Calculate determinant
            var det = m00 * (m11 * m22 - m12 * m21) -
                      m01 * (m10 * m22 - m12 * m20) +
                      m02 * (m10 * m21 - m11 * m20);
            
            if ({mathClass}.Abs(det) <= {epsilon})
                throw new ArgumentException(""Matrix is singular and cannot be inverted."");
            
            var invDet = {GetOneValue(primitiveType)} / det;
            
            // Calculate cofactors and transpose (adjugate matrix)
            return new {matrixType}(
                new {vectorType}(
                    (m11 * m22 - m12 * m21) * invDet,  // c00
                    -(m10 * m22 - m12 * m20) * invDet, // c10
                    (m10 * m21 - m11 * m20) * invDet   // c20
                ),
                new {vectorType}(
                    -(m01 * m22 - m02 * m21) * invDet, // c01
                    (m00 * m22 - m02 * m20) * invDet,  // c11
                    -(m00 * m21 - m01 * m20) * invDet  // c21
                ),
                new {vectorType}(
                    (m01 * m12 - m02 * m11) * invDet,  // c02
                    -(m00 * m12 - m02 * m10) * invDet, // c12
                    (m00 * m11 - m01 * m10) * invDet   // c22
                )
            );";
    }

    private string Generate4x4Inverse(string primitiveType, string matrixType, string vectorType, string epsilon, string mathClass)
    {
        string cof_00 = $"s0";
        string cof_01 = $"-s1";
        string cof_02 = $"s2";
        string cof_03 = $"-s3";

        string cof_10 = $"-(m01 * (m22 * m33 - m23 * m32) - m02 * (m21 * m33 - m23 * m31) + m03 * (m21 * m32 - m22 * m31))";
        string cof_11 = $" (m00 * (m22 * m33 - m23 * m32) - m02 * (m20 * m33 - m23 * m30) + m03 * (m20 * m32 - m22 * m30))";
        string cof_12 = $"-(m00 * (m21 * m33 - m23 * m31) - m01 * (m20 * m33 - m23 * m30) + m03 * (m20 * m31 - m21 * m30))";
        string cof_13 = $" (m00 * (m21 * m32 - m22 * m31) - m01 * (m20 * m32 - m22 * m30) + m02 * (m20 * m31 - m21 * m30))";

        string cof_20 = $" (m01 * (m12 * m33 - m13 * m32) - m02 * (m11 * m33 - m13 * m31) + m03 * (m11 * m32 - m12 * m31))";
        string cof_21 = $"-(m00 * (m12 * m33 - m13 * m32) - m02 * (m10 * m33 - m13 * m30) + m03 * (m10 * m32 - m12 * m30))";
        string cof_22 = $" (m00 * (m11 * m33 - m13 * m31) - m01 * (m10 * m33 - m13 * m30) + m03 * (m10 * m31 - m11 * m30))";
        string cof_23 = $"-(m00 * (m11 * m32 - m12 * m31) - m01 * (m10 * m32 - m12 * m30) + m02 * (m10 * m31 - m11 * m30))";

        string cof_30 = $"-(m01 * (m12 * m23 - m13 * m22) - m02 * (m11 * m23 - m13 * m21) + m03 * (m11 * m22 - m12 * m21))";
        string cof_31 = $" (m00 * (m12 * m23 - m13 * m22) - m02 * (m10 * m23 - m13 * m20) + m03 * (m10 * m22 - m12 * m20))";
        string cof_32 = $"-(m00 * (m11 * m23 - m13 * m21) - m01 * (m10 * m23 - m13 * m20) + m03 * (m10 * m21 - m11 * m20))";
        string cof_33 = $" (m00 * (m11 * m22 - m12 * m21) - m01 * (m10 * m22 - m12 * m20) + m02 * (m10 * m21 - m11 * m20))";

        return $@"            // 4x4 analytical inverse using cofactor expansion
            var m00 = matrix.c0.X; var m01 = matrix.c1.X; var m02 = matrix.c2.X; var m03 = matrix.c3.X;
            var m10 = matrix.c0.Y; var m11 = matrix.c1.Y; var m12 = matrix.c2.Y; var m13 = matrix.c3.Y;
            var m20 = matrix.c0.Z; var m21 = matrix.c1.Z; var m22 = matrix.c2.Z; var m23 = matrix.c3.Z;
            var m30 = matrix.c0.W; var m31 = matrix.c1.W; var m32 = matrix.c2.W; var m33 = matrix.c3.W;
            
            // Calculate minors for determinant calculation (cofactors of first row elements)
            // s0 is C00
            var s0 = m11 * (m22 * m33 - m23 * m32) - m12 * (m21 * m33 - m23 * m31) + m13 * (m21 * m32 - m22 * m31);
            // s1 is M01 (minor of m01), so C01 = -s1
            var s1 = m10 * (m22 * m33 - m23 * m32) - m12 * (m20 * m33 - m23 * m30) + m13 * (m20 * m32 - m22 * m30);
            // s2 is C02
            var s2 = m10 * (m21 * m33 - m23 * m31) - m11 * (m20 * m33 - m23 * m30) + m13 * (m20 * m31 - m21 * m30);
            // s3 is M03 (minor of m03), so C03 = -s3
            var s3 = m10 * (m21 * m32 - m22 * m31) - m11 * (m20 * m32 - m22 * m30) + m12 * (m20 * m31 - m21 * m30);
            
            var det = m00 * s0 - m01 * s1 + m02 * s2 - m03 * s3; // = m00*C00 + m01*C01 + m02*C02 + m03*C03
            
            if ({mathClass}.Abs(det) <= {epsilon})
                throw new ArgumentException(""Matrix is singular and cannot be inverted."");
            
            var invDet = {GetOneValue(primitiveType)} / det;
            
            // Construct the inverse matrix (Adjugate / Determinant)
            // Element (i,j) of inverse is Cofactor(j,i) / det
            return new {matrixType}(
                new {vectorType}( // Column 0 of Inverse (M_inv_00, M_inv_10, M_inv_20, M_inv_30)
                    ({cof_00}) * invDet,    // C00 / det
                    ({cof_01}) * invDet,    // C01 / det
                    ({cof_02}) * invDet,    // C02 / det
                    ({cof_03}) * invDet     // C03 / det
                ),
                new {vectorType}( // Column 1 of Inverse (M_inv_01, M_inv_11, M_inv_21, M_inv_31)
                    ({cof_10}) * invDet,    // C10 / det
                    ({cof_11}) * invDet,    // C11 / det
                    ({cof_12}) * invDet,    // C12 / det
                    ({cof_13}) * invDet     // C13 / det
                ),
                new {vectorType}( // Column 2 of Inverse (M_inv_02, M_inv_12, M_inv_22, M_inv_32)
                    ({cof_20}) * invDet,    // C20 / det
                    ({cof_21}) * invDet,    // C21 / det
                    ({cof_22}) * invDet,    // C22 / det
                    ({cof_23}) * invDet     // C23 / det
                ),
                new {vectorType}( // Column 3 of Inverse (M_inv_03, M_inv_13, M_inv_23, M_inv_33)
                    ({cof_30}) * invDet,    // C30 / det
                    ({cof_31}) * invDet,    // C31 / det
                    ({cof_32}) * invDet,    // C32 / det
                    ({cof_33}) * invDet     // C33 / det
                )
            );";
    }

    private string GetOneValue(string primitiveType)
    {
        return primitiveType == "float" ? "1f" : "1.0";
    }

    private string GetZeroValue(string primitiveType)
    {
        return primitiveType == "float" ? "0f" : "0.0";
    }

    #region Tests

    private static readonly HashSet<string> _generatedTestMethods = new HashSet<string>();

    public override List<string> GenerateTestMethods(string type, int dimension, string[] components)
    {
        var tests = new List<string>();
        var typeName = GetTypeName(type);
        var assertEqualMethod = GetXUnitAssertEqual(type);

        // Only generate tests once per type (not per dimension)
        var typeKey = $"Inverse_{type}";
        if (!_generatedTestMethods.Add(typeKey))
        {
            return tests; // Already generated for this type
        }

        // Generate inverse tests for all supported matrix sizes
        foreach (int size in SupportedDimensions)
        {
            GenerateInverseTests(tests, type, typeName, size, assertEqualMethod);
        }

        return tests;
    }

    private void GenerateInverseTests(List<string> tests, string type, string typeName, int size, string assertEqualMethod)
    {
        var matrixType = $"{typeName}{size}x{size}";
        var vectorType = $"{typeName}{size}";
        var suffix = type == "float" ? "f" : "";
        var components = GetComponents(size);

        // Test identity matrix inverse
        GenerateIdentityInverseTest(tests, type, typeName, size, matrixType, vectorType, components, assertEqualMethod, suffix);

        // Test simple diagonal matrix inverse
        GenerateDiagonalInverseTest(tests, type, typeName, size, matrixType, vectorType, components, assertEqualMethod, suffix);

        // Test specific known matrix inverse
        GenerateKnownMatrixInverseTest(tests, type, typeName, size, matrixType, vectorType, components, assertEqualMethod, suffix);

        // Test inverse * original = identity
        GenerateInverseIdentityTest(tests, type, typeName, size, matrixType, vectorType, components, assertEqualMethod, suffix);

        // Test singular matrix throws exception
        GenerateSingularMatrixTest(tests, type, typeName, size, matrixType, vectorType, components, suffix);
    }

    private void GenerateIdentityInverseTest(List<string> tests, string type, string typeName, int size,
        string matrixType, string vectorType, string[] components, string assertEqualMethod, string suffix)
    {
        var identityColumns = new List<string>();
        for (int col = 0; col < size; col++)
        {
            var columnParams = new List<string>();
            for (int row = 0; row < size; row++)
            {
                columnParams.Add(col == row ? $"1.0{suffix}" : $"0.0{suffix}");
            }
            identityColumns.Add($"new {vectorType}({string.Join(", ", columnParams)})");
        }

        tests.Add($@"        [Fact]
        public void Inverse_{typeName}{size}x{size}_Identity_Test()
        {{
            // Test inverse of identity matrix - should return identity
            {matrixType} identity = new {matrixType}({string.Join(", ", identityColumns)});
            
            {matrixType} inverse = Maths.Inverse(identity);
            
            // Inverse of identity should be identity
            {string.Join("\n            ", identityColumns.SelectMany((col, colIndex) =>
                    components.Select((c, rowIndex) =>
                    {
                        var expectedValue = colIndex == rowIndex ? $"1.0{suffix}" : $"0.0{suffix}";
                        return assertEqualMethod.Replace("{expected}", expectedValue).Replace("{actual}", $"inverse.c{colIndex}.{c}");
                    })
                ))}
        }}");
    }

    private void GenerateDiagonalInverseTest(List<string> tests, string type, string typeName, int size,
        string matrixType, string vectorType, string[] components, string assertEqualMethod, string suffix)
    {
        var diagonalValues = new List<string>();
        var expectedInverseValues = new List<string>();
        for (int i = 0; i < size; i++)
        {
            var value = $"{i + 2}.0{suffix}"; // Use 2, 3, 4, 5 to avoid division issues
            diagonalValues.Add(value);
            expectedInverseValues.Add($"1.0{suffix} / {value}"); // 1/value
        }

        var diagonalColumns = new List<string>();
        for (int col = 0; col < size; col++)
        {
            var columnParams = new List<string>();
            for (int row = 0; row < size; row++)
            {
                columnParams.Add(col == row ? diagonalValues[col] : $"0.0{suffix}");
            }
            diagonalColumns.Add($"new {vectorType}({string.Join(", ", columnParams)})");
        }

        tests.Add($@"        [Fact]
        public void Inverse_{typeName}{size}x{size}_Diagonal_Test()
        {{
            // Test inverse of diagonal matrix
            {matrixType} diagonal = new {matrixType}({string.Join(", ", diagonalColumns)});
            
            {matrixType} inverse = Maths.Inverse(diagonal);
            
            // Inverse of diagonal matrix should have reciprocal diagonal elements
            {string.Join("\n            ", diagonalColumns.SelectMany((col, colIndex) =>
                    components.Select((c, rowIndex) =>
                    {
                        var expectedValue = colIndex == rowIndex ? expectedInverseValues[colIndex] : $"0.0{suffix}";
                        return assertEqualMethod.Replace("{expected}", expectedValue).Replace("{actual}", $"inverse.c{colIndex}.{c}");
                    })
                ))}
        }}");
    }

    private void GenerateKnownMatrixInverseTest(List<string> tests, string type, string typeName, int size,
        string matrixType, string vectorType, string[] components, string assertEqualMethod, string suffix)
    {
        switch (size)
        {
            case 2:
                Generate2x2KnownInverseTest(tests, type, typeName, matrixType, vectorType, components, assertEqualMethod, suffix);
                break;
            case 3:
                Generate3x3KnownInverseTest(tests, type, typeName, matrixType, vectorType, components, assertEqualMethod, suffix);
                break;
            case 4:
                Generate4x4KnownInverseTest(tests, type, typeName, matrixType, vectorType, components, assertEqualMethod, suffix);
                break;
        }
    }

    private void Generate2x2KnownInverseTest(List<string> tests, string type, string typeName,
        string matrixType, string vectorType, string[] components, string assertEqualMethod, string suffix)
    {
        tests.Add($@"        [Fact]
        public void Inverse_{typeName}2x2_Known_Test()
        {{
            // Test known 2x2 matrix: [[1, 2], [3, 4]]
            // Determinant = 1*4 - 2*3 = -2
            // Inverse = (1/det) * [[4, -2], [-3, 1]] = [[-2, 1], [1.5, -0.5]]
            {matrixType} matrix = new {matrixType}(
                new {vectorType}(1.0{suffix}, 3.0{suffix}),
                new {vectorType}(2.0{suffix}, 4.0{suffix})
            );
            
            {matrixType} inverse = Maths.Inverse(matrix);
            
            // Check expected inverse values
            {assertEqualMethod.Replace("{expected}", $"-2.0{suffix}").Replace("{actual}", "inverse.c0.X")}
            {assertEqualMethod.Replace("{expected}", $"1.5{suffix}").Replace("{actual}", "inverse.c0.Y")}
            {assertEqualMethod.Replace("{expected}", $"1.0{suffix}").Replace("{actual}", "inverse.c1.X")}
            {assertEqualMethod.Replace("{expected}", $"-0.5{suffix}").Replace("{actual}", "inverse.c1.Y")}
        }}");
    }

    private void Generate3x3KnownInverseTest(List<string> tests, string type, string typeName,
        string matrixType, string vectorType, string[] components, string assertEqualMethod, string suffix)
    {
        tests.Add($@"        [Fact]
        public void Inverse_{typeName}3x3_Known_Test()
        {{
            // Test known 3x3 matrix with simple values
            {matrixType} matrix = new {matrixType}(
                new {vectorType}(2.0{suffix}, 0.0{suffix}, 0.0{suffix}),
                new {vectorType}(0.0{suffix}, 3.0{suffix}, 0.0{suffix}),
                new {vectorType}(0.0{suffix}, 0.0{suffix}, 4.0{suffix})
            );
            
            {matrixType} inverse = Maths.Inverse(matrix);
            
            // This is a diagonal matrix, so inverse should be reciprocals
            {assertEqualMethod.Replace("{expected}", $"0.5{suffix}").Replace("{actual}", "inverse.c0.X")}
            {assertEqualMethod.Replace("{expected}", $"0.0{suffix}").Replace("{actual}", "inverse.c0.Y")}
            {assertEqualMethod.Replace("{expected}", $"0.0{suffix}").Replace("{actual}", "inverse.c0.Z")}
            {assertEqualMethod.Replace("{expected}", $"0.0{suffix}").Replace("{actual}", "inverse.c1.X")}
            {assertEqualMethod.Replace("{expected}", $"1.0{suffix} / 3.0{suffix}").Replace("{actual}", "inverse.c1.Y")}
            {assertEqualMethod.Replace("{expected}", $"0.0{suffix}").Replace("{actual}", "inverse.c1.Z")}
            {assertEqualMethod.Replace("{expected}", $"0.0{suffix}").Replace("{actual}", "inverse.c2.X")}
            {assertEqualMethod.Replace("{expected}", $"0.0{suffix}").Replace("{actual}", "inverse.c2.Y")}
            {assertEqualMethod.Replace("{expected}", $"0.25{suffix}").Replace("{actual}", "inverse.c2.Z")}
        }}");
    }

    private void Generate4x4KnownInverseTest(List<string> tests, string type, string typeName,
        string matrixType, string vectorType, string[] components, string assertEqualMethod, string suffix)
    {
        tests.Add($@"        [Fact]
        public void Inverse_{typeName}4x4_Known_Test()
        {{
            // Test known 4x4 diagonal matrix
            {matrixType} matrix = new {matrixType}(
                new {vectorType}(2.0{suffix}, 0.0{suffix}, 0.0{suffix}, 0.0{suffix}),
                new {vectorType}(0.0{suffix}, 4.0{suffix}, 0.0{suffix}, 0.0{suffix}),
                new {vectorType}(0.0{suffix}, 0.0{suffix}, 8.0{suffix}, 0.0{suffix}),
                new {vectorType}(0.0{suffix}, 0.0{suffix}, 0.0{suffix}, 0.5{suffix})
            );
            
            {matrixType} inverse = Maths.Inverse(matrix);
            
            // Diagonal matrix inverse should be reciprocals
            {assertEqualMethod.Replace("{expected}", $"0.5{suffix}").Replace("{actual}", "inverse.c0.X")}
            {assertEqualMethod.Replace("{expected}", $"0.0{suffix}").Replace("{actual}", "inverse.c0.Y")}
            {assertEqualMethod.Replace("{expected}", $"0.0{suffix}").Replace("{actual}", "inverse.c0.Z")}
            {assertEqualMethod.Replace("{expected}", $"0.0{suffix}").Replace("{actual}", "inverse.c0.W")}
            
            {assertEqualMethod.Replace("{expected}", $"0.0{suffix}").Replace("{actual}", "inverse.c1.X")}
            {assertEqualMethod.Replace("{expected}", $"0.25{suffix}").Replace("{actual}", "inverse.c1.Y")}
            {assertEqualMethod.Replace("{expected}", $"0.0{suffix}").Replace("{actual}", "inverse.c1.Z")}
            {assertEqualMethod.Replace("{expected}", $"0.0{suffix}").Replace("{actual}", "inverse.c1.W")}
            
            {assertEqualMethod.Replace("{expected}", $"0.0{suffix}").Replace("{actual}", "inverse.c2.X")}
            {assertEqualMethod.Replace("{expected}", $"0.0{suffix}").Replace("{actual}", "inverse.c2.Y")}
            {assertEqualMethod.Replace("{expected}", $"0.125{suffix}").Replace("{actual}", "inverse.c2.Z")}
            {assertEqualMethod.Replace("{expected}", $"0.0{suffix}").Replace("{actual}", "inverse.c2.W")}
            
            {assertEqualMethod.Replace("{expected}", $"0.0{suffix}").Replace("{actual}", "inverse.c3.X")}
            {assertEqualMethod.Replace("{expected}", $"0.0{suffix}").Replace("{actual}", "inverse.c3.Y")}
            {assertEqualMethod.Replace("{expected}", $"0.0{suffix}").Replace("{actual}", "inverse.c3.Z")}
            {assertEqualMethod.Replace("{expected}", $"2.0{suffix}").Replace("{actual}", "inverse.c3.W")}
        }}");
    }

    private void GenerateInverseIdentityTest(List<string> tests, string type, string typeName, int size,
        string matrixType, string vectorType, string[] components, string assertEqualMethod, string suffix)
    {
        // Create a test matrix with known values
        var testMatrixColumns = new List<string>();
        for (int col = 0; col < size; col++)
        {
            var columnParams = new List<string>();
            for (int row = 0; row < size; row++)
            {
                // Create a matrix that's likely to be invertible
                var value = col == row ? $"{col + 2}.0{suffix}" : $"{(col + row) % 3 * 0.1}{suffix}";
                columnParams.Add(value);
            }
            testMatrixColumns.Add($"new {vectorType}({string.Join(", ", columnParams)})");
        }

        tests.Add($@"        [Fact]
        public void Inverse_{typeName}{size}x{size}_InverseTimesOriginal_Test()
        {{
            // Test that Inverse(M) * M = Identity
            {matrixType} original = new {matrixType}({string.Join(", ", testMatrixColumns)});
            
            {matrixType} inverse = Maths.Inverse(original);
            {matrixType} product = Maths.Mul(inverse, original);
            
            // Product should be approximately identity matrix
            {string.Join("\n            ", Enumerable.Range(0, size).SelectMany(col =>
                    components.Select((c, row) =>
                    {
                        var expectedValue = col == row ? $"1.0{suffix}" : $"0.0{suffix}";
                        return assertEqualMethod.Replace("{expected}", expectedValue).Replace("{actual}", $"product.c{col}.{c}");
                    })
                ))}
        }}");
    }

    private void GenerateSingularMatrixTest(List<string> tests, string type, string typeName, int size,
        string matrixType, string vectorType, string[] components, string suffix)
    {
        // Create a singular matrix (determinant = 0)
        var singularColumns = new List<string>();

        switch (size)
        {
            case 2:
                // Create a 2x2 matrix where second row is multiple of first
                singularColumns.Add($"new {vectorType}(1.0{suffix}, 2.0{suffix})");
                singularColumns.Add($"new {vectorType}(2.0{suffix}, 4.0{suffix})"); // Second column is 2x first
                break;
            case 3:
                // Create a 3x3 matrix where third row is sum of first two
                singularColumns.Add($"new {vectorType}(1.0{suffix}, 2.0{suffix}, 3.0{suffix})");
                singularColumns.Add($"new {vectorType}(4.0{suffix}, 5.0{suffix}, 6.0{suffix})");
                singularColumns.Add($"new {vectorType}(5.0{suffix}, 7.0{suffix}, 9.0{suffix})"); // Sum of first two columns
                break;
            case 4:
                // Create a 4x4 matrix with one row of zeros
                singularColumns.Add($"new {vectorType}(1.0{suffix}, 0.0{suffix}, 0.0{suffix}, 0.0{suffix})");
                singularColumns.Add($"new {vectorType}(0.0{suffix}, 1.0{suffix}, 0.0{suffix}, 0.0{suffix})");
                singularColumns.Add($"new {vectorType}(0.0{suffix}, 0.0{suffix}, 1.0{suffix}, 0.0{suffix})");
                singularColumns.Add($"new {vectorType}(0.0{suffix}, 0.0{suffix}, 0.0{suffix}, 0.0{suffix})"); // Zero column
                break;
        }

        tests.Add($@"        [Fact]
        public void Inverse_{typeName}{size}x{size}_SingularMatrix_ThrowsException()
        {{
            // Test that singular (non-invertible) matrix throws ArgumentException
            {matrixType} singular = new {matrixType}({string.Join(", ", singularColumns)});
            
            Assert.Throws<ArgumentException>(() => Maths.Inverse(singular));
        }}");
    }

    #endregion
}
