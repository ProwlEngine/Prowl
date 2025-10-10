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
}
