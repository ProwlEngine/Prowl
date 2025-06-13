using System.Text;

namespace SourceGenerator.MathFunctions;

[MathFunction("Add")]
public class AddFunctionGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double", "int", "byte", "ushort", "uint", "ulong" };
    public override int[] SupportedDimensions => new[] { 2, 3, 4 };
    public override bool SupportsScalars => true;

    // Types that have matrix support
    private readonly string[] MatrixSupportedTypes = new[] { "float", "double", "int", "uint" };

    private readonly HashSet<string> _generatedFunctions = new HashSet<string>();

    public override bool SupportsType(string type, int dimension)
    {
        return type != "bool" && SupportedTypes.Contains(type);
    }

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var sb = new StringBuilder();
        var typeName = GetTypeName(type);

        // Generate all addition operations
        GenerateScalarOperations(sb, type, typeName);
        GenerateVectorOperations(sb, type, typeName);

        // Only generate matrix operations for types that have matrices
        if (MatrixSupportedTypes.Contains(type))
        {
            GenerateMatrixOperations(sb, type, typeName);
        }

        return sb.ToString();
    }

    private void GenerateScalarOperations(StringBuilder sb, string type, string typeName)
    {
        var functionKey = $"Add_{type}_{type}";
        if (!_generatedFunctions.Add(functionKey)) return;

        sb.AppendLine($"        /// <summary>Returns the sum of two {type} values.</summary>");
        sb.AppendLine($"        /// <param name=\"a\">First value.</param>");
        sb.AppendLine($"        /// <param name=\"b\">Second value.</param>");
        sb.AppendLine($"        /// <returns>The result of a + b.</returns>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static {type} Add({type} a, {type} b)");
        sb.AppendLine("        {");

        // Handle potential overflow for small integer types
        if (type == "byte" || type == "ushort")
        {
            sb.AppendLine($"            return ({type})(a + b);");
        }
        else
        {
            sb.AppendLine("            return a + b;");
        }

        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private void GenerateVectorOperations(StringBuilder sb, string type, string typeName)
    {
        for (int dim = 2; dim <= 4; dim++)
        {
            var vectorType = $"{typeName}{dim}";
            var components = GetComponents(dim);

            // Vector + Vector
            GenerateVectorVectorAdd(sb, type, typeName, dim, vectorType, components);

            // Vector + Scalar
            GenerateVectorScalarAdd(sb, type, typeName, dim, vectorType, components);

            // Scalar + Vector
            GenerateScalarVectorAdd(sb, type, typeName, dim, vectorType, components);
        }
    }

    private void GenerateVectorVectorAdd(StringBuilder sb, string type, string typeName, int dim,
        string vectorType, string[] components)
    {
        var functionKey = $"Add_{vectorType}_{vectorType}";
        if (!_generatedFunctions.Add(functionKey)) return;

        sb.AppendLine($"        /// <summary>Returns the component-wise sum of two {vectorType} vectors.</summary>");
        sb.AppendLine($"        /// <param name=\"a\">First vector.</param>");
        sb.AppendLine($"        /// <param name=\"b\">Second vector.</param>");
        sb.AppendLine($"        /// <returns>A new {vectorType} where each component is the sum of the corresponding components in a and b.</returns>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static {vectorType} Add({vectorType} a, {vectorType} b)");
        sb.AppendLine("        {");

        var addTerms = new List<string>();
        for (int i = 0; i < dim; i++)
        {
            if (type == "byte" || type == "ushort")
            {
                addTerms.Add($"({type})(a.{components[i]} + b.{components[i]})");
            }
            else
            {
                addTerms.Add($"a.{components[i]} + b.{components[i]}");
            }
        }

        sb.AppendLine($"            return new {vectorType}({string.Join(", ", addTerms)});");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private void GenerateVectorScalarAdd(StringBuilder sb, string type, string typeName, int dim,
        string vectorType, string[] components)
    {
        var functionKey = $"Add_{vectorType}_{type}";
        if (!_generatedFunctions.Add(functionKey)) return;

        sb.AppendLine($"        /// <summary>Adds a scalar value to each component of a {vectorType} vector.</summary>");
        sb.AppendLine($"        /// <param name=\"v\">The vector.</param>");
        sb.AppendLine($"        /// <param name=\"scalar\">The scalar value to add.</param>");
        sb.AppendLine($"        /// <returns>A new {vectorType} where each component is the sum of the vector component and the scalar.</returns>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static {vectorType} Add({vectorType} v, {type} scalar)");
        sb.AppendLine("        {");

        var addTerms = new List<string>();
        for (int i = 0; i < dim; i++)
        {
            if (type == "byte" || type == "ushort")
            {
                addTerms.Add($"({type})(v.{components[i]} + scalar)");
            }
            else
            {
                addTerms.Add($"v.{components[i]} + scalar");
            }
        }

        sb.AppendLine($"            return new {vectorType}({string.Join(", ", addTerms)});");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private void GenerateScalarVectorAdd(StringBuilder sb, string type, string typeName, int dim,
        string vectorType, string[] components)
    {
        var functionKey = $"Add_{type}_{vectorType}";
        if (!_generatedFunctions.Add(functionKey)) return;

        sb.AppendLine($"        /// <summary>Adds a scalar value to each component of a {vectorType} vector.</summary>");
        sb.AppendLine($"        /// <param name=\"scalar\">The scalar value to add.</param>");
        sb.AppendLine($"        /// <param name=\"v\">The vector.</param>");
        sb.AppendLine($"        /// <returns>A new {vectorType} where each component is the sum of the scalar and the vector component.</returns>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static {vectorType} Add({type} scalar, {vectorType} v)");
        sb.AppendLine("        {");

        var addTerms = new List<string>();
        for (int i = 0; i < dim; i++)
        {
            if (type == "byte" || type == "ushort")
            {
                addTerms.Add($"({type})(scalar + v.{components[i]})");
            }
            else
            {
                addTerms.Add($"scalar + v.{components[i]}");
            }
        }

        sb.AppendLine($"            return new {vectorType}({string.Join(", ", addTerms)});");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private void GenerateMatrixOperations(StringBuilder sb, string type, string typeName)
    {
        // Generate square matrix operations
        for (int dim = 2; dim <= 4; dim++)
        {
            GenerateSquareMatrixAdd(sb, type, typeName, dim);
            GenerateSquareMatrixScalarAdd(sb, type, typeName, dim);
            GenerateScalarSquareMatrixAdd(sb, type, typeName, dim);
        }

        // Generate non-square matrix operations
        GenerateNonSquareMatrixAdd(sb, type, typeName);
    }

    private void GenerateSquareMatrixAdd(StringBuilder sb, string type, string typeName, int dim)
    {
        var matrixType = $"{typeName}{dim}x{dim}";
        var functionKey = $"Add_{matrixType}_{matrixType}";
        if (!_generatedFunctions.Add(functionKey)) return;

        sb.AppendLine($"        /// <summary>Returns the component-wise sum of two {matrixType} matrices.</summary>");
        sb.AppendLine($"        /// <param name=\"a\">First matrix.</param>");
        sb.AppendLine($"        /// <param name=\"b\">Second matrix.</param>");
        sb.AppendLine($"        /// <returns>A new {matrixType} where each component is the sum of the corresponding components in a and b.</returns>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static {matrixType} Add({matrixType} a, {matrixType} b)");
        sb.AppendLine("        {");

        sb.AppendLine($"            return new {matrixType}(");
        var columnAdditions = new List<string>();
        for (int col = 0; col < dim; col++)
        {
            columnAdditions.Add($"                a.c{col} + b.c{col}");
        }
        sb.AppendLine(string.Join(",\n", columnAdditions));
        sb.AppendLine("            );");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private void GenerateSquareMatrixScalarAdd(StringBuilder sb, string type, string typeName, int dim)
    {
        var matrixType = $"{typeName}{dim}x{dim}";
        var functionKey = $"Add_{matrixType}_{type}";
        if (!_generatedFunctions.Add(functionKey)) return;

        sb.AppendLine($"        /// <summary>Adds a scalar value to each component of a {matrixType} matrix.</summary>");
        sb.AppendLine($"        /// <param name=\"m\">The matrix.</param>");
        sb.AppendLine($"        /// <param name=\"scalar\">The scalar value to add.</param>");
        sb.AppendLine($"        /// <returns>A new {matrixType} where each component is the sum of the matrix component and the scalar.</returns>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static {matrixType} Add({matrixType} m, {type} scalar)");
        sb.AppendLine("        {");

        sb.AppendLine($"            return new {matrixType}(");
        var columnAdditions = new List<string>();
        for (int col = 0; col < dim; col++)
        {
            columnAdditions.Add($"                m.c{col} + scalar");
        }
        sb.AppendLine(string.Join(",\n", columnAdditions));
        sb.AppendLine("            );");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private void GenerateScalarSquareMatrixAdd(StringBuilder sb, string type, string typeName, int dim)
    {
        var matrixType = $"{typeName}{dim}x{dim}";
        var functionKey = $"Add_{type}_{matrixType}";
        if (!_generatedFunctions.Add(functionKey)) return;

        sb.AppendLine($"        /// <summary>Adds a scalar value to each component of a {matrixType} matrix.</summary>");
        sb.AppendLine($"        /// <param name=\"scalar\">The scalar value to add.</param>");
        sb.AppendLine($"        /// <param name=\"m\">The matrix.</param>");
        sb.AppendLine($"        /// <returns>A new {matrixType} where each component is the sum of the scalar and the matrix component.</returns>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static {matrixType} Add({type} scalar, {matrixType} m)");
        sb.AppendLine("        {");

        sb.AppendLine($"            return new {matrixType}(");
        var columnAdditions = new List<string>();
        for (int col = 0; col < dim; col++)
        {
            columnAdditions.Add($"                scalar + m.c{col}");
        }
        sb.AppendLine(string.Join(",\n", columnAdditions));
        sb.AppendLine("            );");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private void GenerateNonSquareMatrixAdd(StringBuilder sb, string type, string typeName)
    {
        var nonSquareConfigs = new[]
        {
            (2, 3), (2, 4), (3, 2), (3, 4), (4, 2), (4, 3)
        };

        foreach (var (rows, cols) in nonSquareConfigs)
        {
            var matrixType = $"{typeName}{rows}x{cols}";

            // Matrix + Matrix
            GenerateNonSquareMatrixMatrixAdd(sb, type, typeName, rows, cols, matrixType);

            // Matrix + Scalar
            GenerateNonSquareMatrixScalarAdd(sb, type, typeName, rows, cols, matrixType);

            // Scalar + Matrix
            GenerateNonSquareScalarMatrixAdd(sb, type, typeName, rows, cols, matrixType);
        }
    }

    private void GenerateNonSquareMatrixMatrixAdd(StringBuilder sb, string type, string typeName,
        int rows, int cols, string matrixType)
    {
        var functionKey = $"Add_{matrixType}_{matrixType}";
        if (!_generatedFunctions.Add(functionKey)) return;

        sb.AppendLine($"        /// <summary>Returns the component-wise sum of two {matrixType} matrices.</summary>");
        sb.AppendLine($"        /// <param name=\"a\">First matrix.</param>");
        sb.AppendLine($"        /// <param name=\"b\">Second matrix.</param>");
        sb.AppendLine($"        /// <returns>A new {matrixType} where each component is the sum of the corresponding components in a and b.</returns>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static {matrixType} Add({matrixType} a, {matrixType} b)");
        sb.AppendLine("        {");

        sb.AppendLine($"            return new {matrixType}(");
        var columnAdditions = new List<string>();
        for (int col = 0; col < cols; col++)
        {
            columnAdditions.Add($"                a.c{col} + b.c{col}");
        }
        sb.AppendLine(string.Join(",\n", columnAdditions));
        sb.AppendLine("            );");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private void GenerateNonSquareMatrixScalarAdd(StringBuilder sb, string type, string typeName,
        int rows, int cols, string matrixType)
    {
        var functionKey = $"Add_{matrixType}_{type}";
        if (!_generatedFunctions.Add(functionKey)) return;

        sb.AppendLine($"        /// <summary>Adds a scalar value to each component of a {matrixType} matrix.</summary>");
        sb.AppendLine($"        /// <param name=\"m\">The matrix.</param>");
        sb.AppendLine($"        /// <param name=\"scalar\">The scalar value to add.</param>");
        sb.AppendLine($"        /// <returns>A new {matrixType} where each component is the sum of the matrix component and the scalar.</returns>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static {matrixType} Add({matrixType} m, {type} scalar)");
        sb.AppendLine("        {");

        sb.AppendLine($"            return new {matrixType}(");
        var columnAdditions = new List<string>();
        for (int col = 0; col < cols; col++)
        {
            columnAdditions.Add($"                m.c{col} + scalar");
        }
        sb.AppendLine(string.Join(",\n", columnAdditions));
        sb.AppendLine("            );");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private void GenerateNonSquareScalarMatrixAdd(StringBuilder sb, string type, string typeName,
        int rows, int cols, string matrixType)
    {
        var functionKey = $"Add_{type}_{matrixType}";
        if (!_generatedFunctions.Add(functionKey)) return;

        sb.AppendLine($"        /// <summary>Adds a scalar value to each component of a {matrixType} matrix.</summary>");
        sb.AppendLine($"        /// <param name=\"scalar\">The scalar value to add.</param>");
        sb.AppendLine($"        /// <param name=\"m\">The matrix.</param>");
        sb.AppendLine($"        /// <returns>A new {matrixType} where each component is the sum of the scalar and the matrix component.</returns>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static {matrixType} Add({type} scalar, {matrixType} m)");
        sb.AppendLine("        {");

        sb.AppendLine($"            return new {matrixType}(");
        var columnAdditions = new List<string>();
        for (int col = 0; col < cols; col++)
        {
            columnAdditions.Add($"                scalar + m.c{col}");
        }
        sb.AppendLine(string.Join(",\n", columnAdditions));
        sb.AppendLine("            );");
        sb.AppendLine("        }");
        sb.AppendLine();
    }
}
