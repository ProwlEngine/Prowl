using System.Runtime.CompilerServices;
using System.Text;

namespace SourceGenerator.MathFunctions;

/// <summary>
/// A source generator for creating determinant functions for matrices and sets of vectors.
/// It creates overloads for 2x2, 3x3, and 4x4 matrices and their corresponding vector sets.
/// </summary>
[MathFunction("Determinant")]
public class DeterminantFunctionGenerator : MathFunctionGenerator
{
    /// <summary>
    /// Determinants are supported for floating-point types.
    /// </summary>
    public override string[] SupportedTypes => new[] { "float", "double" };

    /// <summary>
    /// Supported dimensions for square matrices.
    /// </summary>
    public override int[] SupportedDimensions => new[] { 2, 3, 4 };

    /// <summary>
    /// Determinants are not defined for scalars.
    /// </summary>
    public override bool SupportsScalars => false;

    /// <summary>
    /// Tracks generated functions to prevent duplicates since the main generator calls this for each dimension.
    /// </summary>
    private readonly HashSet<string> _generatedFunctions = new HashSet<string>();

    /// <summary>
    /// Generates the C# code for determinant functions based on the dimension.
    /// </summary>
    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var sb = new StringBuilder();
        var typeName = GetTypeName(type);

        switch (dimension)
        {
            case 2:
                Generate2DDeterminants(sb, type, typeName);
                break;
            case 3:
                Generate3DDeterminants(sb, type, typeName);
                break;
            case 4:
                Generate4DDeterminants(sb, type, typeName);
                break;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates determinant functions for 2D vectors and 2x2 matrices.
    /// </summary>
    private void Generate2DDeterminants(StringBuilder sb, string type, string typeName)
    {
        var vectorType = $"{typeName}2";
        var matrixType = $"{typeName}2x2";

        // Determinant of two 2D vectors
        var functionKey_Vec = $"Determinant_{vectorType}_{vectorType}";
        if (_generatedFunctions.Add(functionKey_Vec))
        {
            sb.AppendLine($"        /// <summary>Calculates the determinant of a 2x2 matrix formed by two 2D column vectors.</summary>");
            sb.AppendLine($"        /// <param name=\"a\">The first column vector.</param>");
            sb.AppendLine($"        /// <param name=\"b\">The second column vector.</param>");
            sb.AppendLine($"        /// <returns>The determinant: a.X * b.Y - a.Y * b.X.</returns>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static {type} Determinant({vectorType} a, {vectorType} b)");
            sb.AppendLine("        {");
            sb.AppendLine("            return a.X * b.Y - a.Y * b.X;");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // Determinant of a 2x2 matrix
        var functionKey_Mat = $"Determinant_{matrixType}";
        if (_generatedFunctions.Add(functionKey_Mat))
        {
            sb.AppendLine($"        /// <summary>Calculates the determinant of a {matrixType} matrix.</summary>");
            sb.AppendLine($"        /// <param name=\"m\">The matrix to calculate the determinant of.</param>");
            sb.AppendLine($"        /// <returns>The determinant of the matrix.</returns>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static {type} Determinant({matrixType} m)");
            sb.AppendLine("        {");
            sb.AppendLine("            // The matrix is column-major. For [[a,b],[c,d]], the columns are c0=(a,c) and c1=(b,d).");
            sb.AppendLine("            // The determinant is ad - bc, which corresponds to m.c0.X * m.c1.Y - m.c1.X * m.c0.Y.");
            sb.AppendLine("            return m.c0.X * m.c1.Y - m.c1.X * m.c0.Y;");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
    }

    /// <summary>
    /// Generates determinant functions for 3D vectors and 3x3 matrices.
    /// </summary>
    private void Generate3DDeterminants(StringBuilder sb, string type, string typeName)
    {
        var vectorType = $"{typeName}3";
        var matrixType = $"{typeName}3x3";

        // Determinant of three 3D vectors (Scalar Triple Product)
        var functionKey_Vec = $"Determinant_{vectorType}_{vectorType}_{vectorType}";
        if (_generatedFunctions.Add(functionKey_Vec))
        {
            sb.AppendLine($"        /// <summary>Calculates the determinant of a 3x3 matrix formed by three 3D column vectors (scalar triple product).</summary>");
            sb.AppendLine($"        /// <param name=\"a\">The first column vector.</param>");
            sb.AppendLine($"        /// <param name=\"b\">The second column vector.</param>");
            sb.AppendLine($"        /// <param name=\"c\">The third column vector.</param>");
            sb.AppendLine($"        /// <returns>The determinant, equivalent to Dot(a, Cross(b, c)).</returns>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static {type} Determinant({vectorType} a, {vectorType} b, {vectorType} c)");
            sb.AppendLine("        {");
            sb.AppendLine("            // Using the scalar triple product formula: a · (b x c)");
            sb.AppendLine("            return a.X * (b.Y * c.Z - b.Z * c.Y) -");
            sb.AppendLine("                   a.Y * (b.X * c.Z - b.Z * c.X) +");
            sb.AppendLine("                   a.Z * (b.X * c.Y - b.Y * c.X);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // Determinant of a 3x3 matrix
        var functionKey_Mat = $"Determinant_{matrixType}";
        if (_generatedFunctions.Add(functionKey_Mat))
        {
            sb.AppendLine($"        /// <summary>Calculates the determinant of a {matrixType} matrix.</summary>");
            sb.AppendLine($"        /// <param name=\"m\">The matrix to calculate the determinant of.</param>");
            sb.AppendLine($"        /// <returns>The determinant of the matrix.</returns>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static {type} Determinant({matrixType} m)");
            sb.AppendLine("        {");
            sb.AppendLine("            return Determinant(m.c0, m.c1, m.c2);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
    }

    /// <summary>
    /// Generates the determinant function for 4x4 matrices.
    /// </summary>
    private void Generate4DDeterminants(StringBuilder sb, string type, string typeName)
    {
        var matrixType = $"{typeName}4x4";

        var functionKey_Mat = $"Determinant_{matrixType}";
        if (_generatedFunctions.Add(functionKey_Mat))
        {
            sb.AppendLine($"        /// <summary>Calculates the determinant of a {matrixType} matrix.</summary>");
            sb.AppendLine($"        /// <param name=\"m\">The matrix to calculate the determinant of.</param>");
            sb.AppendLine($"        /// <returns>The determinant of the matrix.</returns>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static {type} Determinant({matrixType} m)");
            sb.AppendLine("        {");
            sb.AppendLine($"            // Components are laid out in column-major order, but the formula is often shown in row-major.");
            sb.AppendLine($"            // We'll use component names a,b,c... for clarity, mapping from the column vectors.");
            sb.AppendLine($"            {type} a = m.c0.X, b = m.c1.X, c = m.c2.X, d = m.c3.X;");
            sb.AppendLine($"            {type} e = m.c0.Y, f = m.c1.Y, g = m.c2.Y, h = m.c3.Y;");
            sb.AppendLine($"            {type} i = m.c0.Z, j = m.c1.Z, k = m.c2.Z, l = m.c3.Z;");
            sb.AppendLine($"            {type} mm = m.c0.W, n = m.c1.W, o = m.c2.W, p = m.c3.W;");
            sb.AppendLine();
            sb.AppendLine($"            // Pre-calculate 2x2 determinants for cofactors");
            sb.AppendLine($"            {type} kp_lo = k * p - l * o;");
            sb.AppendLine($"            {type} jp_ln = j * p - l * n;");
            sb.AppendLine($"            {type} jo_kn = j * o - k * n;");
            sb.AppendLine($"            {type} ip_lm = i * p - l * mm;");
            sb.AppendLine($"            {type} io_km = i * o - k * mm;");
            sb.AppendLine($"            {type} in_jm = i * n - j * mm;");
            sb.AppendLine();
            sb.AppendLine("            // Cofactor expansion across the first row");
            sb.AppendLine($"            return a * (f * kp_lo - g * jp_ln + h * jo_kn) -");
            sb.AppendLine($"                   b * (e * kp_lo - g * ip_lm + h * io_km) +");
            sb.AppendLine($"                   c * (e * jp_ln - f * ip_lm + h * in_jm) -");
            sb.AppendLine($"                   d * (e * jo_kn - f * io_km + g * in_jm);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
    }
}
