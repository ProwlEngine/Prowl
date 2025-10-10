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
            var functionKey = $"Invert_{matrixType}";

            if (!_generatedFunctions.Add(functionKey)) continue;

            sb.AppendLine(GenerateInvertStaticMethod(type, typeName, size, matrixType));
            sb.AppendLine(GenerateInvertExtensionMethod(type, typeName, size, matrixType));
        }

        return sb.ToString();
    }

    private string GenerateInvertStaticMethod(string primitiveType, string typeName, int size, string matrixType)
    {
        var sb = new StringBuilder();
        var vectorType = $"{typeName}{size}";
        var epsilon = primitiveType == "float" ? "float.Epsilon" : "double.Epsilon";
        var mathClass = primitiveType == "float" ? "MathF" : "Math";
        var nanValue = primitiveType == "float" ? "float.NaN" : "double.NaN";

        sb.AppendLine($"        /// <summary>Attempts to calculate the inverse of the given matrix. If successful, result will contain the inverted matrix.</summary>");
        sb.AppendLine($"        /// <param name=\"matrix\">The source matrix to invert.</param>");
        sb.AppendLine($"        /// <param name=\"result\">If successful, contains the inverted matrix.</param>");
        sb.AppendLine($"        /// <returns>True if the source matrix could be inverted; False otherwise.</returns>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static bool Invert({matrixType} matrix, out {matrixType} result)");
        sb.AppendLine("        {");

        switch (size)
        {
            case 2:
                sb.AppendLine(Generate2x2InvertBody(primitiveType, matrixType, vectorType, epsilon, mathClass, nanValue));
                break;
            case 3:
                sb.AppendLine(Generate3x3InvertBody(primitiveType, matrixType, vectorType, epsilon, mathClass, nanValue));
                break;
            case 4:
                sb.AppendLine(Generate4x4InvertBody(primitiveType, matrixType, vectorType, epsilon, mathClass, nanValue));
                break;
        }

        sb.AppendLine("        }");
        sb.AppendLine();

        return sb.ToString();
    }

    private string GenerateInvertExtensionMethod(string primitiveType, string typeName, int size, string matrixType)
    {
        return $@"        /// <summary>Calculates the inverse of this matrix.</summary>
        /// <returns>The inverted matrix, or a matrix filled with NaN if inversion fails.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {matrixType} Invert(this {matrixType} matrix)
        {{
            Maths.Invert(matrix, out {matrixType} result);
            return result;
        }}

";
    }

    private string Generate2x2InvertBody(string primitiveType, string matrixType, string vectorType, string epsilon, string mathClass, string nanValue)
    {
        var one = GetOneValue(primitiveType);

        return $@"            var a = matrix.c0.X; var b = matrix.c1.X;
            var c = matrix.c0.Y; var d = matrix.c1.Y;
            
            var det = a * d - b * c;
            
            if ({mathClass}.Abs(det) < {epsilon})
            {{
                result = new {matrixType}(
                    new {vectorType}({nanValue}, {nanValue}),
                    new {vectorType}({nanValue}, {nanValue})
                );
                return false;
            }}
            
            var invDet = {one} / det;
            
            result = new {matrixType}(
                new {vectorType}(d * invDet, -c * invDet),
                new {vectorType}(-b * invDet, a * invDet)
            );
            return true;";
    }

    private string Generate3x3InvertBody(string primitiveType, string matrixType, string vectorType, string epsilon, string mathClass, string nanValue)
    {
        var one = GetOneValue(primitiveType);

        return $@"            var m00 = matrix.c0.X; var m01 = matrix.c1.X; var m02 = matrix.c2.X;
            var m10 = matrix.c0.Y; var m11 = matrix.c1.Y; var m12 = matrix.c2.Y;
            var m20 = matrix.c0.Z; var m21 = matrix.c1.Z; var m22 = matrix.c2.Z;
            
            // Calculate determinant
            var det = m00 * (m11 * m22 - m12 * m21) -
                      m01 * (m10 * m22 - m12 * m20) +
                      m02 * (m10 * m21 - m11 * m20);
            
            if ({mathClass}.Abs(det) < {epsilon})
            {{
                result = new {matrixType}(
                    new {vectorType}({nanValue}, {nanValue}, {nanValue}),
                    new {vectorType}({nanValue}, {nanValue}, {nanValue}),
                    new {vectorType}({nanValue}, {nanValue}, {nanValue})
                );
                return false;
            }}
            
            var invDet = {one} / det;
            
            // Calculate cofactors and transpose (adjugate matrix)
            result = new {matrixType}(
                new {vectorType}(
                    (m11 * m22 - m12 * m21) * invDet,
                    -(m10 * m22 - m12 * m20) * invDet,
                    (m10 * m21 - m11 * m20) * invDet
                ),
                new {vectorType}(
                    -(m01 * m22 - m02 * m21) * invDet,
                    (m00 * m22 - m02 * m20) * invDet,
                    -(m00 * m21 - m01 * m20) * invDet
                ),
                new {vectorType}(
                    (m01 * m12 - m02 * m11) * invDet,
                    -(m00 * m12 - m02 * m10) * invDet,
                    (m00 * m11 - m01 * m10) * invDet
                )
            );
            return true;";
    }

    private string Generate4x4InvertBody(string primitiveType, string matrixType, string vectorType, string epsilon, string mathClass, string nanValue)
    {
        var one = GetOneValue(primitiveType);

        return $@"            var a = matrix.c0.X; var b = matrix.c1.X; var c = matrix.c2.X; var d = matrix.c3.X;
            var e = matrix.c0.Y; var f = matrix.c1.Y; var g = matrix.c2.Y; var h = matrix.c3.Y;
            var i = matrix.c0.Z; var j = matrix.c1.Z; var k = matrix.c2.Z; var l = matrix.c3.Z;
            var m = matrix.c0.W; var n = matrix.c1.W; var o = matrix.c2.W; var p = matrix.c3.W;

            var kp_lo = k * p - l * o;
            var jp_ln = j * p - l * n;
            var jo_kn = j * o - k * n;
            var ip_lm = i * p - l * m;
            var io_km = i * o - k * m;
            var in_jm = i * n - j * m;

            var a11 = +(f * kp_lo - g * jp_ln + h * jo_kn);
            var a12 = -(e * kp_lo - g * ip_lm + h * io_km);
            var a13 = +(e * jp_ln - f * ip_lm + h * in_jm);
            var a14 = -(e * jo_kn - f * io_km + g * in_jm);

            var det = a * a11 + b * a12 + c * a13 + d * a14;

            if ({mathClass}.Abs(det) < {epsilon})
            {{
                result = new {matrixType}(
                    new {vectorType}({nanValue}, {nanValue}, {nanValue}, {nanValue}),
                    new {vectorType}({nanValue}, {nanValue}, {nanValue}, {nanValue}),
                    new {vectorType}({nanValue}, {nanValue}, {nanValue}, {nanValue}),
                    new {vectorType}({nanValue}, {nanValue}, {nanValue}, {nanValue})
                );
                return false;
            }}

            var invDet = {one} / det;

            var gp_ho = g * p - h * o;
            var fp_hn = f * p - h * n;
            var fo_gn = f * o - g * n;
            var ep_hm = e * p - h * m;
            var eo_gm = e * o - g * m;
            var en_fm = e * n - f * m;

            var gl_hk = g * l - h * k;
            var fl_hj = f * l - h * j;
            var fk_gj = f * k - g * j;
            var el_hi = e * l - h * i;
            var ek_gi = e * k - g * i;
            var ej_fi = e * j - f * i;

            result = new {matrixType}(
                new {vectorType}(
                    a11 * invDet,
                    a12 * invDet,
                    a13 * invDet,
                    a14 * invDet
                ),
                new {vectorType}(
                    -(b * kp_lo - c * jp_ln + d * jo_kn) * invDet,
                    +(a * kp_lo - c * ip_lm + d * io_km) * invDet,
                    -(a * jp_ln - b * ip_lm + d * in_jm) * invDet,
                    +(a * jo_kn - b * io_km + c * in_jm) * invDet
                ),
                new {vectorType}(
                    +(b * gp_ho - c * fp_hn + d * fo_gn) * invDet,
                    -(a * gp_ho - c * ep_hm + d * eo_gm) * invDet,
                    +(a * fp_hn - b * ep_hm + d * en_fm) * invDet,
                    -(a * fo_gn - b * eo_gm + c * en_fm) * invDet
                ),
                new {vectorType}(
                    -(b * gl_hk - c * fl_hj + d * fk_gj) * invDet,
                    +(a * gl_hk - c * el_hi + d * ek_gi) * invDet,
                    -(a * fl_hj - b * el_hi + d * ej_fi) * invDet,
                    +(a * fk_gj - b * ek_gi + c * ej_fi) * invDet
                )
            );
            return true;";
    }

    private string GetOneValue(string primitiveType)
    {
        return primitiveType == "float" ? "1f" : "1.0";
    }

    private string GetZeroValue(string primitiveType)
    {
        return primitiveType == "float" ? "0f" : "0.0";
    }

}
