using System.Text;
using System.Runtime.CompilerServices;

namespace SourceGenerator.MathFunctions;

[MathFunction("TransformDirection")]
public class TransformDirectionGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    public override int[] SupportedDimensions => new[] { 2, 3 };
    public override bool SupportsScalars => false;

    private readonly HashSet<string> _generatedFunctions = new HashSet<string>();

    public override bool SupportsType(string type, int dimension)
    {
        return SupportedTypes.Contains(type) && SupportedDimensions.Contains(dimension);
    }

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var sb = new StringBuilder();
        var typeName = GetTypeName(type);

        // Generate 2D direction transformations
        Generate2DDirectionTransform(sb, type, typeName);

        // Generate 3D direction transformations
        Generate3DDirectionTransform(sb, type, typeName);

        return sb.ToString();
    }

    private void Generate2DDirectionTransform(StringBuilder sb, string type, string typeName)
    {
        // 2x2 matrix transforming 2D direction
        var functionKey_2x2_2 = $"TransformDirection_{typeName}2x2_{typeName}2";
        if (_generatedFunctions.Add(functionKey_2x2_2)) // Ensure unique generation
        {
            sb.AppendLine($"        /// <summary>Transforms a 2D direction vector using a 2x2 matrix (ignores translation).</summary>");
            sb.AppendLine($"        /// <param name=\"direction\">The 2D direction vector to transform.</param>");
            sb.AppendLine($"        /// <param name=\"matrix\">The 2x2 transformation matrix.</param>");
            sb.AppendLine($"        /// <returns>The transformed direction vector.</returns>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static {typeName}2 TransformDirection({typeName}2 direction, {typeName}2x2 matrix)");
            sb.AppendLine("        {");
            sb.AppendLine($"            return Mul(matrix, direction);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // 3x3 matrix transforming 2D direction (extracting 2x2 portion)
        var functionKey_3x3_2 = $"TransformDirection_{typeName}3x3_{typeName}2";
        if (_generatedFunctions.Add(functionKey_3x3_2)) // Ensure unique generation
        {
            sb.AppendLine($"        /// <summary>Transforms a 2D direction vector using the upper-left 2x2 portion of a 3x3 matrix.</summary>");
            sb.AppendLine($"        /// <param name=\"direction\">The 2D direction vector to transform.</param>");
            sb.AppendLine($"        /// <param name=\"matrix\">The 3x3 transformation matrix.</param>");
            sb.AppendLine($"        /// <returns>The transformed direction vector.</returns>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static {typeName}2 TransformDirection({typeName}2 direction, {typeName}3x3 matrix)");
            sb.AppendLine("        {");
            sb.AppendLine($"            // Extract the upper-left 2x2 portion for rotation/scale (no translation)");
            sb.AppendLine($"            {typeName}2x2 upperLeft = new {typeName}2x2(");
            sb.AppendLine($"                matrix.c0.X, matrix.c0.Y,"); // Note: c0.X and c0.Y are first column elements
            sb.AppendLine($"                matrix.c1.X, matrix.c1.Y"); // Note: c1.X and c1.Y are second column elements
            sb.AppendLine($"            );");
            sb.AppendLine($"            return Mul(upperLeft, direction);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
    }

    private void Generate3DDirectionTransform(StringBuilder sb, string type, string typeName)
    {
        // 3x3 matrix transforming 3D direction
        var functionKey_3x3_3 = $"TransformDirection_{typeName}3x3_{typeName}3";
        if (_generatedFunctions.Add(functionKey_3x3_3)) // Ensure unique generation
        {
            sb.AppendLine($"        /// <summary>Transforms a 3D direction vector using a 3x3 matrix (ignores translation).</summary>");
            sb.AppendLine($"        /// <param name=\"direction\">The 3D direction vector to transform.</param>");
            sb.AppendLine($"        /// <param name=\"matrix\">The 3x3 transformation matrix.</param>");
            sb.AppendLine($"        /// <returns>The transformed direction vector.</returns>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static {typeName}3 TransformDirection({typeName}3 direction, {typeName}3x3 matrix)");
            sb.AppendLine("        {");
            sb.AppendLine($"            return Mul(matrix, direction);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // 4x4 matrix transforming 3D direction (extracting 3x3 portion)
        var functionKey_4x4_3 = $"TransformDirection_{typeName}4x4_{typeName}3";
        if (_generatedFunctions.Add(functionKey_4x4_3)) // Ensure unique generation
        {
            sb.AppendLine($"        /// <summary>Transforms a 3D direction vector using the upper-left 3x3 portion of a 4x4 matrix.</summary>");
            sb.AppendLine($"        /// <param name=\"direction\">The 3D direction vector to transform.</param>");
            sb.AppendLine($"        /// <param name=\"matrix\">The 4x4 transformation matrix.</param>");
            sb.AppendLine($"        /// <returns>The transformed direction vector.</returns>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static {typeName}3 TransformDirection({typeName}3 direction, {typeName}4x4 matrix)");
            sb.AppendLine("        {");
            sb.AppendLine($"            // Extract the upper-left 3x3 portion for rotation/scale (no translation)");
            sb.AppendLine($"            {typeName}3x3 upperLeft = new {typeName}3x3(");
            // Assuming Double3x3 constructor takes columns (c0, c1, c2)
            // And each DoubleN is (X, Y, Z, W) where X is first element, Y second, etc.
            // So matrix.c0 is the first column, matrix.c0.X is first element of first column (M11)
            sb.AppendLine($"                matrix.c0.X, matrix.c0.Y, matrix.c0.Z,");
            sb.AppendLine($"                matrix.c1.X, matrix.c1.Y, matrix.c1.Z,");
            sb.AppendLine($"                matrix.c2.X, matrix.c2.Y, matrix.c2.Z");
            sb.AppendLine($"            );");
            sb.AppendLine($"            return Mul(upperLeft, direction);");
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
        // This is crucial because Generate2DDirectionTransformTests and Generate3DDirectionTransformTests
        // will add multiple tests for a given type, regardless of dimension parameter.
        var typeKey = $"TransformDirection_Tests_{type}"; // Unique key for type
        if (!_generatedTestMethods.Add(typeKey))
        {
            return tests; // Already generated tests for this type
        }

        // Generate tests for 2D direction transformations
        Generate2DDirectionTransformTests(tests, type, typeName, assertEqualMethod);

        // Generate tests for 3D direction transformations
        Generate3DDirectionTransformTests(tests, type, typeName, assertEqualMethod);

        return tests;
    }

    private void Generate2DDirectionTransformTests(List<string> tests, string type, string typeName, string assertEqualMethod)
    {
        var suffix = type == "float" ? "f" : "";
        var sqrt2 = type == "float" ? "0.7071068f" : "0.7071067811865475"; // 1/sqrt(2)

        // Test 2x2 matrix direction transformation
        tests.Add($@"        [Fact]
        public void TransformDirection_{typeName}2_2x2Identity_Test()
        {{
            // Test with identity matrix - direction should remain unchanged
            {typeName}2 direction = new {typeName}2(1.0{suffix}, 0.0{suffix}); // Right vector
            {typeName}2x2 identity = new {typeName}2x2(
                1.0{suffix}, 0.0{suffix},
                0.0{suffix}, 1.0{suffix}
            );
            
            {typeName}2 result = Maths.TransformDirection(direction, identity);
            
            {assertEqualMethod.Replace("{expected}", "1.0" + suffix).Replace("{actual}", "result.X")}
            {assertEqualMethod.Replace("{expected}", "0.0" + suffix).Replace("{actual}", "result.Y")}
        }}");

        tests.Add($@"        [Fact]
        public void TransformDirection_{typeName}2_2x2Scale_Test()
        {{
            // Test with scale matrix
            {typeName}2 direction = new {typeName}2(1.0{suffix}, 1.0{suffix}); // Diagonal vector
            {typeName}2x2 scale = new {typeName}2x2(
                2.0{suffix}, 0.0{suffix},  // Scale X by 2
                0.0{suffix}, 3.0{suffix}   // Scale Y by 3
            );
            
            {typeName}2 result = Maths.TransformDirection(direction, scale);
            
            // Direction should be scaled: (1,1) -> (2,3)
            {assertEqualMethod.Replace("{expected}", "2.0" + suffix).Replace("{actual}", "result.X")}
            {assertEqualMethod.Replace("{expected}", "3.0" + suffix).Replace("{actual}", "result.Y")}
        }}");

        tests.Add($@"        [Fact]
        public void TransformDirection_{typeName}2_2x2Rotation_Test()
        {{
            // Test with 90-degree counter-clockwise rotation
            {typeName}2 direction = new {typeName}2(1.0{suffix}, 0.0{suffix}); // Right vector
            {typeName}2x2 rotation90 = new {typeName}2x2(
                0.0{suffix}, -1.0{suffix},  // 90-degree counter-clockwise rotation
                1.0{suffix}, 0.0{suffix}
            );
            
            {typeName}2 result = Maths.TransformDirection(direction, rotation90);
            
            // 90-degree rotation of (1,0) should give (0,1)
            {assertEqualMethod.Replace("{expected}", "0.0" + suffix).Replace("{actual}", "result.X")}
            {assertEqualMethod.Replace("{expected}", "1.0" + suffix).Replace("{actual}", "result.Y")}
        }}");

        tests.Add($@"        [Fact]
        public void TransformDirection_{typeName}2_2x2Rotation45_Test()
        {{
            // Test with 45-degree counter-clockwise rotation
            {typeName}2 direction = new {typeName}2(1.0{suffix}, 0.0{suffix}); // Right vector
            {typeName}2x2 rotation45 = new {typeName}2x2(
                {sqrt2}, -{sqrt2},  // 45-degree counter-clockwise rotation
                {sqrt2}, {sqrt2}
            );
            
            {typeName}2 result = Maths.TransformDirection(direction, rotation45);
            
            // 45-degree rotation of (1,0) should give (sqrt(2)/2, sqrt(2)/2)
            {assertEqualMethod.Replace("{expected}", sqrt2).Replace("{actual}", "result.X")}
            {assertEqualMethod.Replace("{expected}", sqrt2).Replace("{actual}", "result.Y")}
        }}");

        // Test 3x3 matrix with 2D direction (extracting 2x2 portion)
        tests.Add($@"        [Fact]
        public void TransformDirection_{typeName}2_3x3Matrix_Test()
        {{
            // Test 2D direction with 3x3 transformation matrix (should ignore translation)
            {typeName}2 direction = new {typeName}2(2.0{suffix}, 3.0{suffix});
            {typeName}3x3 transform = new {typeName}3x3(
                new {typeName}3(2.0{suffix}, 0.0{suffix}, 10.0{suffix}), // Scale X by 2, translate by 10
                new {typeName}3(0.0{suffix}, 3.0{suffix}, 20.0{suffix}), // Scale Y by 3, translate by 20
                new {typeName}3(0.0{suffix}, 0.0{suffix}, 1.0{suffix})   // Homogeneous row
            );
            
            {typeName}2 result = Maths.TransformDirection(direction, transform);
            
            // Should extract upper-left 2x2 and ignore translation
            // (2,3) scaled by (2,3) = (4,9)
            {assertEqualMethod.Replace("{expected}", "4.0" + suffix).Replace("{actual}", "result.X")}
            {assertEqualMethod.Replace("{expected}", "9.0" + suffix).Replace("{actual}", "result.Y")}
        }}");

        tests.Add($@"        [Fact]
        public void TransformDirection_{typeName}2_3x3WithRotation_Test()
        {{
            // Test 2D direction with 3x3 matrix containing rotation and translation
            {typeName}2 direction = new {typeName}2(1.0{suffix}, 0.0{suffix}); // Right vector
            {typeName}3x3 rotateAndTranslate = new {typeName}3x3(
                new {typeName}3(0.0{suffix}, -1.0{suffix}, 5.0{suffix}), // 90° rotation + translation
                new {typeName}3(1.0{suffix}, 0.0{suffix}, 7.0{suffix}),
                new {typeName}3(0.0{suffix}, 0.0{suffix}, 1.0{suffix})
            );
            
            {typeName}2 result = Maths.TransformDirection(direction, rotateAndTranslate);
            
            // Should rotate (1,0) to (0,1) and ignore translation
            {assertEqualMethod.Replace("{expected}", "0.0" + suffix).Replace("{actual}", "result.X")}
            {assertEqualMethod.Replace("{expected}", "1.0" + suffix).Replace("{actual}", "result.Y")}
        }}");
    }

    private void Generate3DDirectionTransformTests(List<string> tests, string type, string typeName, string assertEqualMethod)
    {
        var suffix = type == "float" ? "f" : "";
        var sqrt2 = type == "float" ? "0.7071068f" : "0.7071067811865475"; // 1/sqrt(2)

        // Test 3x3 matrix direction transformation
        tests.Add($@"        [Fact]
        public void TransformDirection_{typeName}3_3x3Identity_Test()
        {{
            // Test with identity matrix - direction should remain unchanged
            {typeName}3 direction = new {typeName}3(1.0{suffix}, 0.0{suffix}, 0.0{suffix}); // Right vector
            {typeName}3x3 identity = new {typeName}3x3(
                new {typeName}3(1.0{suffix}, 0.0{suffix}, 0.0{suffix}),
                new {typeName}3(0.0{suffix}, 1.0{suffix}, 0.0{suffix}),
                new {typeName}3(0.0{suffix}, 0.0{suffix}, 1.0{suffix})
            );
            
            {typeName}3 result = Maths.TransformDirection(direction, identity);
            
            {assertEqualMethod.Replace("{expected}", "1.0" + suffix).Replace("{actual}", "result.X")}
            {assertEqualMethod.Replace("{expected}", "0.0" + suffix).Replace("{actual}", "result.Y")}
            {assertEqualMethod.Replace("{expected}", "0.0" + suffix).Replace("{actual}", "result.Z")}
        }}");

        tests.Add($@"        [Fact]
        public void TransformDirection_{typeName}3_3x3Scale_Test()
        {{
            // Test with scale matrix
            {typeName}3 direction = new {typeName}3(2.0{suffix}, 3.0{suffix}, 4.0{suffix});
            {typeName}3x3 scale = new {typeName}3x3(
                new {typeName}3(2.0{suffix}, 0.0{suffix}, 0.0{suffix}), // Scale X by 2
                new {typeName}3(0.0{suffix}, 3.0{suffix}, 0.0{suffix}), // Scale Y by 3
                new {typeName}3(0.0{suffix}, 0.0{suffix}, 0.5{suffix})  // Scale Z by 0.5
            );
            
            {typeName}3 result = Maths.TransformDirection(direction, scale);
            
            // Direction should be scaled: (2,3,4) -> (4,9,2)
            {assertEqualMethod.Replace("{expected}", "4.0" + suffix).Replace("{actual}", "result.X")}
            {assertEqualMethod.Replace("{expected}", "9.0" + suffix).Replace("{actual}", "result.Y")}
            {assertEqualMethod.Replace("{expected}", "2.0" + suffix).Replace("{actual}", "result.Z")}
        }}");

        tests.Add($@"        [Fact]
        public void TransformDirection_{typeName}3_3x3RotationX_Test()
        {{
            // Test with 90-degree rotation around X-axis
            {typeName}3 direction = new {typeName}3(0.0{suffix}, 1.0{suffix}, 0.0{suffix}); // Up vector
            {typeName}3x3 rotationX90 = new {typeName}3x3(
                new {typeName}3(1.0{suffix}, 0.0{suffix}, 0.0{suffix}),  // X unchanged
                new {typeName}3(0.0{suffix}, 0.0{suffix}, 1.0{suffix}),  // Y -> Z   <-- FIX: Changed -1.0 to 1.0
                new {typeName}3(0.0{suffix}, -1.0{suffix}, 0.0{suffix})   // Z -> -Y  <-- FIX: Changed 1.0 to -1.0
            );
            
            {typeName}3 result = Maths.TransformDirection(direction, rotationX90);
            
            // 90-degree rotation around X: (0,1,0) -> (0,0,1)
            {assertEqualMethod.Replace("{expected}", "0.0" + suffix).Replace("{actual}", "result.X")}
            {assertEqualMethod.Replace("{expected}", "0.0" + suffix).Replace("{actual}", "result.Y")}
            {assertEqualMethod.Replace("{expected}", "1.0" + suffix).Replace("{actual}", "result.Z")}
        }}");

        tests.Add($@"        [Fact]
        public void TransformDirection_{typeName}3_3x3RotationY_Test()
        {{
            // Test with 90-degree rotation around Y-axis
            {typeName}3 direction = new {typeName}3(1.0{suffix}, 0.0{suffix}, 0.0{suffix}); // Right vector
            {typeName}3x3 rotationY90 = new {typeName}3x3(
                new {typeName}3(0.0{suffix}, 0.0{suffix}, -1.0{suffix}),  // X -> Z
                new {typeName}3(0.0{suffix}, 1.0{suffix}, 0.0{suffix}),  // Y unchanged
                new {typeName}3(-1.0{suffix}, 0.0{suffix}, 0.0{suffix}) // Z -> -X
            );
            
            {typeName}3 result = Maths.TransformDirection(direction, rotationY90);
            
            // 90-degree rotation around Y: (1,0,0) -> (0,0,-1)
            {assertEqualMethod.Replace("{expected}", "0.0" + suffix).Replace("{actual}", "result.X")}
            {assertEqualMethod.Replace("{expected}", "0.0" + suffix).Replace("{actual}", "result.Y")}
            {assertEqualMethod.Replace("{expected}", "-1.0" + suffix).Replace("{actual}", "result.Z")}
        }}");

        // Test 4x4 matrix with 3D direction (extracting 3x3 portion)
        tests.Add($@"        [Fact]
        public void TransformDirection_{typeName}3_4x4Matrix_Test()
        {{
            // Test 3D direction with 4x4 transformation matrix (should ignore translation)
            {typeName}3 direction = new {typeName}3(1.0{suffix}, 2.0{suffix}, 3.0{suffix});
            {typeName}4x4 transform = new {typeName}4x4(
                new {typeName}4(2.0{suffix}, 0.0{suffix}, 0.0{suffix}, 10.0{suffix}), // Scale X by 2, translate
                new {typeName}4(0.0{suffix}, 3.0{suffix}, 0.0{suffix}, 20.0{suffix}), // Scale Y by 3, translate
                new {typeName}4(0.0{suffix}, 0.0{suffix}, 4.0{suffix}, 30.0{suffix}), // Scale Z by 4, translate
                new {typeName}4(0.0{suffix}, 0.0{suffix}, 0.0{suffix}, 1.0{suffix})   // Homogeneous row
            );
            
            {typeName}3 result = Maths.TransformDirection(direction, transform);
            
            // Should extract upper-left 3x3 and ignore translation
            // (1,2,3) scaled by (2,3,4) = (2,6,12)
            {assertEqualMethod.Replace("{expected}", "2.0" + suffix).Replace("{actual}", "result.X")}
            {assertEqualMethod.Replace("{expected}", "6.0" + suffix).Replace("{actual}", "result.Y")}
            {assertEqualMethod.Replace("{expected}", "12.0" + suffix).Replace("{actual}", "result.Z")}
        }}");

        tests.Add($@"        [Fact]
        public void TransformDirection_{typeName}3_4x4WithRotationAndTranslation_Test()
        {{
            // Test 3D direction with 4x4 matrix containing rotation and translation
            {typeName}3 direction = new {typeName}3(0.0{suffix}, 1.0{suffix}, 0.0{suffix}); // Up vector
            {typeName}4x4 rotateAndTranslate = new {typeName}4x4(
                new {typeName}4(1.0{suffix}, 0.0{suffix}, 0.0{suffix}, 100.0{suffix}), // No X rotation, translate
                new {typeName}4(0.0{suffix}, 0.0{suffix}, -1.0{suffix}, 200.0{suffix}), // Y -> -Z, translate
                new {typeName}4(0.0{suffix}, 1.0{suffix}, 0.0{suffix}, 300.0{suffix}), // Z -> Y, translate
                new {typeName}4(0.0{suffix}, 0.0{suffix}, 0.0{suffix}, 1.0{suffix})    // Homogeneous row
            );
            
            {typeName}3 result = Maths.TransformDirection(direction, rotateAndTranslate);
            
            // Should rotate (0,1,0) to (0,0,1) and ignore translation
            {assertEqualMethod.Replace("{expected}", "0.0" + suffix).Replace("{actual}", "result.X")}
            {assertEqualMethod.Replace("{expected}", "0.0" + suffix).Replace("{actual}", "result.Y")}
            {assertEqualMethod.Replace("{expected}", "1.0" + suffix).Replace("{actual}", "result.Z")}
        }}");

        tests.Add($@"        [Fact]
        public void TransformDirection_{typeName}3_4x4UniformScale_Test()
        {{
            // Test with uniform scale (should preserve direction ratios)
            {typeName}3 direction = new {typeName}3(1.0{suffix}, 2.0{suffix}, 3.0{suffix});
            {typeName}4x4 uniformScale = new {typeName}4x4(
                new {typeName}4(2.0{suffix}, 0.0{suffix}, 0.0{suffix}, 0.0{suffix}), // Uniform scale by 2
                new {typeName}4(0.0{suffix}, 2.0{suffix}, 0.0{suffix}, 0.0{suffix}),
                new {typeName}4(0.0{suffix}, 0.0{suffix}, 2.0{suffix}, 0.0{suffix}),
                new {typeName}4(0.0{suffix}, 0.0{suffix}, 0.0{suffix}, 1.0{suffix})
            );
            
            {typeName}3 result = Maths.TransformDirection(direction, uniformScale);
            
            // Uniform scale should preserve ratios: (1,2,3) -> (2,4,6)
            {assertEqualMethod.Replace("{expected}", "2.0" + suffix).Replace("{actual}", "result.X")}
            {assertEqualMethod.Replace("{expected}", "4.0" + suffix).Replace("{actual}", "result.Y")}
            {assertEqualMethod.Replace("{expected}", "6.0" + suffix).Replace("{actual}", "result.Z")}
        }}");
    }

    #endregion
}
