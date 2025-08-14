using System.Text;

namespace SourceGenerator.MathFunctions;

[MathFunction("Transpose")]
public class TransposeGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double", "int", "uint" };
    public override int[] SupportedDimensions => new[] { 2, 3, 4 };
    public override bool SupportsScalars => false;

    // Track generated functions to prevent duplicates
    private readonly HashSet<string> _generatedFunctions = new HashSet<string>();

    // Define all supported matrix sizes (rows, columns)
    private readonly (int rows, int cols)[] MatrixSizes = new[]
    {
        // Square matrices
        (2, 2), (3, 3), (4, 4),
        // Non-square matrices  
        (2, 3), (2, 4),
        (3, 2), (3, 4),
        (4, 2), (4, 3)
    };

    public override bool SupportsType(string type, int dimension)
    {
        return type != "bool" && SupportedTypes.Contains(type);
    }

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var sb = new StringBuilder();
        var typeName = GetTypeName(type);

        // Generate transpose functions for all matrix sizes
        foreach (var (rows, cols) in MatrixSizes)
        {
            var sourceType = $"{typeName}{rows}x{cols}";
            var resultType = $"{typeName}{cols}x{rows}";
            var functionKey = $"Transpose_{sourceType}";

            // Skip if already generated
            if (!_generatedFunctions.Add(functionKey)) continue;

            sb.AppendLine(GenerateTransposeFunction(type, typeName, rows, cols, sourceType, resultType));
        }

        return sb.ToString();
    }

    private string GenerateTransposeFunction(string primitiveType, string typeName, int rows, int cols,
        string sourceType, string resultType)
    {
        var sb = new StringBuilder();
        var sourceComponents = GetComponents(rows);
        var resultComponents = GetComponents(cols);

        sb.AppendLine($"        /// <summary>Returns the transpose of a {sourceType} matrix.</summary>");
        sb.AppendLine($"        /// <param name=\"m\">The matrix to transpose.</param>");
        sb.AppendLine($"        /// <returns>The transposed matrix ({resultType}).</returns>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static {resultType} Transpose({sourceType} m)");
        sb.AppendLine("        {");

        // For single column matrices (Nx1), we can return the column as a row vector
        if (cols == 1)
        {
            throw new NotImplementedException("Column vector transpose not implemented.");
        }
        // For single row matrices (1xN), convert row to column
        else if (rows == 1)
        {
            throw new NotImplementedException("Row vector transpose not implemented.");
        }
        // General case for MxN matrices where M,N > 1
        else
        {
            sb.AppendLine($"            return new {resultType}(");
            sb.AppendLine(GenerateTransposeArgs(rows, cols, primitiveType, resultComponents, sourceComponents));
            sb.AppendLine("            );");
        }

        sb.AppendLine("        }");
        sb.AppendLine();

        return sb.ToString();
    }

    private string GenerateTransposeArgs(int sourceRows, int sourceCols, string primitiveType, string[] resultComponents, string[] sourceComponents)
    {
        var args = new List<string>();
        var typeName = GetTypeName(primitiveType); // This will be templated properly by calling code

        // For each column in the result (which corresponds to each row in the source)
        for (int resultCol = 0; resultCol < sourceRows; resultCol++)
        {
            var rowComponents = new List<string>();

            // For each row in the result (which corresponds to each column in the source)
            for (int resultRow = 0; resultRow < sourceCols; resultRow++)
            {
                // Result[resultRow, resultCol] = Source[resultCol, resultRow]
                // In column-major: Result.c[resultCol][resultRow] = Source.c[resultRow][resultCol]
                rowComponents.Add($"m.c{resultRow}.{sourceComponents[resultCol]}");
            }

            // Create the vector for this result column
            var vectorType = $"{typeName}{sourceCols}";
            args.Add($"                new {vectorType}({string.Join(", ", rowComponents)})");
        }

        return string.Join(",\n", args);
    }

    #region Tests

    private static readonly HashSet<string> _generatedTestMethods = new HashSet<string>();

    public override List<string> GenerateTestMethods(string type, int dimension, string[] components)
    {
        var tests = new List<string>();
        var typeName = GetTypeName(type);
        var assertEqualMethod = GetXUnitAssertEqual(type);

        // Only generate tests once per type (not per dimension)
        var typeKey = $"Transpose_{type}";
        if (!_generatedTestMethods.Add(typeKey))
        {
            return tests; // Already generated for this type
        }

        // Generate transpose tests for all matrix sizes
        foreach (var (rows, cols) in MatrixSizes)
        {
            GenerateTransposeTest(tests, type, typeName, rows, cols, assertEqualMethod);
        }

        return tests;
    }

    private void GenerateTransposeTest(List<string> tests, string type, string typeName, int rows, int cols, string assertEqualMethod)
    {
        var sourceType = $"{typeName}{rows}x{cols}";
        var resultType = $"{typeName}{cols}x{rows}";
        var sourceComponents = GetComponents(rows);
        var resultComponents = GetComponents(cols);

        // Generate test matrix values in a predictable pattern
        var sb = new StringBuilder();
        sb.AppendLine($@"        [Fact]
        public void Transpose_{typeName}{rows}x{cols}Test()
        {{");

        // Create source matrix with known values
        sb.AppendLine($"            // Create test matrix with known values");
        var matrixColumns = new List<string>();
        var valueIndex = 0;

        for (int col = 0; col < cols; col++)
        {
            var sourceVectorType = $"{typeName}{rows}";
            var columnValues = new List<string>();
            for (int row = 0; row < rows; row++)
            {
                columnValues.Add(GetTestValue(type, valueIndex++));
            }
            matrixColumns.Add($"new {sourceVectorType}({string.Join(", ", columnValues)})");
        }

        sb.AppendLine($"            {sourceType} source = new {sourceType}({string.Join(", ", matrixColumns)});");
        sb.AppendLine();
        sb.AppendLine($"            // Perform transpose");
        sb.AppendLine($"            {resultType} result = Maths.Transpose(source);");
        sb.AppendLine();

        // Generate expected values and assertions
        sb.AppendLine($"            // Verify transpose: result[i,j] should equal source[j,i]");

        valueIndex = 0;
        for (int sourceCol = 0; sourceCol < cols; sourceCol++)
        {
            for (int sourceRow = 0; sourceRow < rows; sourceRow++)
            {
                var sourceValue = GetTestValue(type, valueIndex++);

                // In the result matrix: result[sourceCol, sourceRow] = source[sourceRow, sourceCol]
                // Which means: result.c{sourceRow}.{resultComponents[sourceCol]} = sourceValue
                if (sourceCol < resultComponents.Length && sourceRow < cols) // Bounds check
                {
                    var assertion = assertEqualMethod
                        .Replace("{expected}", sourceValue)
                        .Replace("{actual}", $"result.c{sourceRow}.{resultComponents[sourceCol]}");
                    sb.AppendLine($"            {assertion}");
                }
            }
        }

        sb.AppendLine("        }");

        tests.Add(sb.ToString());
    }

    #endregion
}
