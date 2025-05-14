using System.Reflection;
using System.Text;

namespace SourceGenerator
{
    class Program
    {
        public static string[] types = new[] { "float", "double", "int", "byte", "ushort", "uint", "ulong", "bool" };
        public static int[] dimensions = new[] { 2, 3, 4 };

        internal class MatrixConfig
        {
            public string StructName { get; }
            public string ComponentTypeName { get; } // e.g., "float"
            public int Rows { get; }
            public int Columns { get; }
            public string VectorTypeNamePrefix { get; } // e.g., "Float" for Float2, Float3, Float4 columns
            public string BoolVectorTypeNamePrefix { get; } // e.g., "Bool" for Bool2, Bool3, Bool4 results of comparisons

            public MatrixConfig(string structName, string componentTypeName, int rows, int columns, string vectorTypeNamePrefix, string boolVectorTypeNamePrefix = "Bool")
            {
                StructName = structName;
                ComponentTypeName = componentTypeName;
                Rows = rows;
                Columns = columns;
                VectorTypeNamePrefix = vectorTypeNamePrefix;
                BoolVectorTypeNamePrefix = boolVectorTypeNamePrefix;
            }

            public string GetColumnVectorType() => $"{VectorTypeNamePrefix}{Rows}"; // e.g., Float4
            public string GetFullBoolMatrixType() => $"{BoolVectorTypeNamePrefix}{Rows}x{Columns}"; // e.g. Bool4x4
            public string GetFullBoolColumnVectorType() => $"{BoolVectorTypeNamePrefix}{Rows}"; // e.g. Bool4

        }

        internal class VectorSwizzleConfig
        {
            public string StructName { get; }
            public string ComponentTypeName { get; }
            public string[] SourceFieldNames { get; }
            public string OutputStructPrefix { get; }

            public VectorSwizzleConfig(string structName, string componentTypeName, string[] sourceFieldNames, string outputStructPrefix)
            {
                StructName = structName;
                ComponentTypeName = componentTypeName;
                SourceFieldNames = sourceFieldNames;
                OutputStructPrefix = outputStructPrefix;
            }
        }

        internal class SwizzleCharSetDefinition
        {
            public char[] Chars { get; }

            public SwizzleCharSetDefinition(char[] chars)
            {
                Chars = chars;
            }
        }

        // Swizzle configuration
        private static readonly VectorSwizzleConfig[] s_vectorConfigs =
        {
            new VectorSwizzleConfig("Float2", "float", new[] { "X", "Y" }, "Float"),
            new VectorSwizzleConfig("Float3", "float", new[] { "X", "Y", "Z" }, "Float"),
            new VectorSwizzleConfig("Float4", "float", new[] { "X", "Y", "Z", "W" }, "Float"),

            new VectorSwizzleConfig("Double2", "double", new[] { "X", "Y" }, "Double"),
            new VectorSwizzleConfig("Double3", "double", new[] { "X", "Y", "Z" }, "Double"),
            new VectorSwizzleConfig("Double4", "double", new[] { "X", "Y", "Z", "W" }, "Double"),

            new VectorSwizzleConfig("Int2", "int", new[] { "X", "Y" }, "Int"),
            new VectorSwizzleConfig("Int3", "int", new[] { "X", "Y", "Z" }, "Int"),
            new VectorSwizzleConfig("Int4", "int", new[] { "X", "Y", "Z", "W" }, "Int"),

            new VectorSwizzleConfig("Bool2", "bool", new[] { "X", "Y" }, "Bool"),
            new VectorSwizzleConfig("Bool3", "bool", new[] { "X", "Y", "Z" }, "Bool"),
            new VectorSwizzleConfig("Bool4", "bool", new[] { "X", "Y", "Z", "W" }, "Bool"),

            new VectorSwizzleConfig("Byte2", "byte", new[] { "X", "Y" }, "Byte"),
            new VectorSwizzleConfig("Byte3", "byte", new[] { "X", "Y", "Z" }, "Byte"),
            new VectorSwizzleConfig("Byte4", "byte", new[] { "X", "Y", "Z", "W" }, "Byte"),

            new VectorSwizzleConfig("UShort2", "ushort", new[] { "X", "Y" }, "UShort"),
            new VectorSwizzleConfig("UShort3", "ushort", new[] { "X", "Y", "Z" }, "UShort"),
            new VectorSwizzleConfig("UShort4", "ushort", new[] { "X", "Y", "Z", "W" }, "UShort"),

            new VectorSwizzleConfig("UInt2", "uint", new[] { "X", "Y" }, "UInt"),
            new VectorSwizzleConfig("UInt3", "uint", new[] { "X", "Y", "Z" }, "UInt"),
            new VectorSwizzleConfig("UInt4", "uint", new[] { "X", "Y", "Z", "W" }, "UInt"),

            new VectorSwizzleConfig("ULong2", "ulong", new[] { "X", "Y" }, "ULong"),
            new VectorSwizzleConfig("ULong3", "ulong", new[] { "X", "Y", "Z" }, "ULong"),
            new VectorSwizzleConfig("ULong4", "ulong", new[] { "X", "Y", "Z", "W" }, "ULong")
        };

        // Could probably not be a static array... should probably do that cause this is a whole lot of duplicate code
        private static readonly MatrixConfig[] s_matrixConfigs =
        {
            // Square Matrices - Float
            new MatrixConfig("Float2x2", "float", 2, 2, "Float"),
            new MatrixConfig("Float3x3", "float", 3, 3, "Float"),
            new MatrixConfig("Float4x4", "float", 4, 4, "Float"),
            
            // Non-Square Matrices - Float
            new MatrixConfig("Float2x3", "float", 2, 3, "Float"),
            new MatrixConfig("Float2x4", "float", 2, 4, "Float"),
            new MatrixConfig("Float3x2", "float", 3, 2, "Float"),
            new MatrixConfig("Float3x4", "float", 3, 4, "Float"),
            new MatrixConfig("Float4x2", "float", 4, 2, "Float"),
            new MatrixConfig("Float4x3", "float", 4, 3, "Float"),

            // Square Matrices - Double
            new MatrixConfig("Double2x2", "double", 2, 2, "Double"),
            new MatrixConfig("Double3x3", "double", 3, 3, "Double"),
            new MatrixConfig("Double4x4", "double", 4, 4, "Double"),
            
            // Non-Square Matrices - Double
            new MatrixConfig("Double2x3", "double", 2, 3, "Double"),
            new MatrixConfig("Double2x4", "double", 2, 4, "Double"),
            new MatrixConfig("Double3x2", "double", 3, 2, "Double"),
            new MatrixConfig("Double3x4", "double", 3, 4, "Double"),
            new MatrixConfig("Double4x2", "double", 4, 2, "Double"),
            new MatrixConfig("Double4x3", "double", 4, 3, "Double"),

            // Square Matrices - Int
            new MatrixConfig("Int2x2", "int", 2, 2, "Int"),
            new MatrixConfig("Int3x3", "int", 3, 3, "Int"),
            new MatrixConfig("Int4x4", "int", 4, 4, "Int"),
            
            // Non-Square Matrices - Int
            new MatrixConfig("Int2x3", "int", 2, 3, "Int"),
            new MatrixConfig("Int2x4", "int", 2, 4, "Int"),
            new MatrixConfig("Int3x2", "int", 3, 2, "Int"),
            new MatrixConfig("Int3x4", "int", 3, 4, "Int"),
            new MatrixConfig("Int4x2", "int", 4, 2, "Int"),
            new MatrixConfig("Int4x3", "int", 4, 3, "Int"),

            // Square Matrices - UInt
            new MatrixConfig("UInt2x2", "uint", 2, 2, "UInt"),
            new MatrixConfig("UInt3x3", "uint", 3, 3, "UInt"),
            new MatrixConfig("UInt4x4", "uint", 4, 4, "UInt"),
            
            // Non-Square Matrices - UInt
            new MatrixConfig("UInt2x3", "uint", 2, 3, "UInt"),
            new MatrixConfig("UInt2x4", "uint", 2, 4, "UInt"),
            new MatrixConfig("UInt3x2", "uint", 3, 2, "UInt"),
            new MatrixConfig("UInt3x4", "uint", 3, 4, "UInt"),
            new MatrixConfig("UInt4x2", "uint", 4, 2, "UInt"),
            new MatrixConfig("UInt4x3", "uint", 4, 3, "UInt"),
            
            // Square Bool Matrices (for results of comparisons)
            new MatrixConfig("Bool2x2", "bool", 2, 2, "Bool"),
            new MatrixConfig("Bool3x3", "bool", 3, 3, "Bool"),
            new MatrixConfig("Bool4x4", "bool", 4, 4, "Bool"),
            
            // Non-Square Bool Matrices (for results of comparisons)
            new MatrixConfig("Bool2x3", "bool", 2, 3, "Bool"),
            new MatrixConfig("Bool2x4", "bool", 2, 4, "Bool"),
            new MatrixConfig("Bool3x2", "bool", 3, 2, "Bool"),
            new MatrixConfig("Bool3x4", "bool", 3, 4, "Bool"),
            new MatrixConfig("Bool4x2", "bool", 4, 2, "Bool"),
            new MatrixConfig("Bool4x3", "bool", 4, 3, "Bool"),
        };

        private static readonly SwizzleCharSetDefinition[] s_swizzleCharSets =
        {
            new SwizzleCharSetDefinition(new[] { 'X', 'Y', 'Z', 'W' }), // Primary accessors
            new SwizzleCharSetDefinition(new[] { 'R', 'G', 'B', 'A' }), // Color accessors
        };

        private const int MAX_OUTPUT_SWIZZLE_DIMENSION = 4;

        static void Main(string[] args)
        {
            string outputDirectory = args.Length > 0 ? args[0] : "Generated";
            bool generateSwizzles = args.Length > 1 ? bool.Parse(args[1]) : true;

            // Create output directory if it doesn't exist
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            Console.WriteLine($"Generating vector types to directory: {outputDirectory}");

            // Generate basic vector structs
            foreach (var type in types)
            {
                foreach (var dimension in dimensions)
                {
                    var source = GenerateVectorStruct(type, dimension);
                    var fileName = $"{GetTypeName(type)}{dimension}.cs";
                    var filePath = Path.Combine(outputDirectory, "Vectors", fileName);
                    Directory.CreateDirectory(new FileInfo(filePath).Directory.FullName);
                    File.WriteAllText(filePath, source, Encoding.UTF8);
                    Console.WriteLine($"Generated: {fileName}");
                }
            }

            // Generate swizzles if enabled
            if (generateSwizzles)
            {
                Console.WriteLine("\nGenerating swizzles...");
                foreach (var config in s_vectorConfigs)
                {
                    var swizzleSource = GenerateSwizzlesForStruct(config);
                    var fileName = $"{config.StructName}.Swizzles.cs";
                    var filePath = Path.Combine(outputDirectory, "Vectors", fileName);
                    Directory.CreateDirectory(new FileInfo(filePath).Directory.FullName);
                    File.WriteAllText(filePath, swizzleSource, Encoding.UTF8);
                    Console.WriteLine($"Generated: {fileName}");
                }
            }

            {
                Console.WriteLine("\nGenerating matrix types...");
                foreach (var config in s_matrixConfigs)
                {
                    var source = GenerateMatrixStruct(config);
                    var fileName = $"{config.StructName}.cs";
                    var filePath = Path.Combine(outputDirectory, "Matrix", fileName);
                    Directory.CreateDirectory(new FileInfo(filePath).Directory.FullName);
                    File.WriteAllText(filePath, source, Encoding.UTF8);
                    Console.WriteLine($"Generated: {fileName} in {outputDirectory}");
                }
            }

            // Generate math class
            {
                var source = GenerateMathSource(DiscoverMathFunctions());
                var fileName = $"Maths.cs";
                var filePath = Path.Combine(outputDirectory, fileName);

                File.WriteAllText(filePath, source, Encoding.UTF8);
                Console.WriteLine($"Generated: {fileName}");
            }


            int totalFiles = types.Length * dimensions.Length + (generateSwizzles ? s_vectorConfigs.Length : 0);
            Console.WriteLine($"\nGeneration complete! {totalFiles} files created.");
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }

        private static void AddHeader(StringBuilder source)
        {
            source.AppendLine("//");
            source.AppendLine("// THIS FILE IS AUTO-GENERATED");
            source.AppendLine("//");
            source.AppendLine("// Do not modify this file directly. All changes will be lost when the code is regenerated.");
            source.AppendLine("// To make changes to this vector type, modify the SourceGenerator project and regenerate the code.");
            source.AppendLine("//");
            source.AppendLine("// Generated by: Prowl.Vector's SourceGenerator Console Application");
            source.AppendLine("// Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            source.AppendLine("//");
            source.AppendLine();
        }

        private static string GetTypeName(string type)
        {
            if (type == "float") return "Float";
            if (type == "double") return "Double";
            if (type == "int") return "Int";
            if (type == "byte") return "Byte";
            if (type == "ushort") return "UShort";
            if (type == "uint") return "UInt";
            if (type == "ulong") return "ULong";
            if (type == "bool") return "Bool";
            return type;
        }

        private static bool IsFloatingPoint(string type)
        {
            return type == "float" || type == "double";
        }

        private static bool IsSignedType(string type)
        {
            return type == "float" || type == "double" || type == "int" || type == "bool";
        }

        private static bool IsNumericType(string type)
        {
            return type != "bool";
        }

        #region Vector Generation Methods

        private static bool IsSystemNumericsCompatible(string type, int dimension)
        {
            // System.Numerics supports Vector2, Vector3, Vector4 for float only
            return type == "float" && (dimension >= 2 && dimension <= 4);
        }

        private static string GenerateVectorStruct(string primitiveType, int dimension)
        {
            var typeName = GetTypeName(primitiveType);
            var structName = $"{typeName}{dimension}";
            var components = GetComponents(dimension);
            var componentDeclarations = string.Join(", ", components);

            var isFloatingPoint = IsFloatingPoint(primitiveType);
            var isSignedType = IsSignedType(primitiveType);

            var mathClass = primitiveType == "float" ? "MathF" : "Math";

            var source = new StringBuilder();

            // Add auto-generated header comment
            AddHeader(source);

            source.AppendLine("using System;");
            source.AppendLine("using System.Globalization;");
            source.AppendLine("using System.Runtime.CompilerServices;");
            source.AppendLine("using System.Collections.Generic;");
            source.AppendLine("using System.Linq;");
            if (IsSystemNumericsCompatible(primitiveType, dimension))
                source.AppendLine("using System.Numerics;");

            source.AppendLine();
            source.AppendLine("namespace Prowl.Vector");
            source.AppendLine("{");
            source.AppendLine($"    /// <summary>");
            source.AppendLine($"    /// Represents a {dimension}-component vector using {primitiveType} precision.");
            source.AppendLine($"    /// </summary>");
            source.AppendLine($"    [System.Serializable]");
            source.AppendLine($"    public partial struct {structName} : IEquatable<{structName}>, IFormattable");
            source.AppendLine("    {");

            // Fields
            source.AppendLine($"        public {primitiveType} {componentDeclarations};");
            source.AppendLine();

            // Constructors
            GenerateConstructors(source, structName, primitiveType, dimension, components);

            // Static Properties
            GenerateStaticProperties(source, structName, primitiveType, dimension, components);

            // Properties (only for floating-point types)
            if (isFloatingPoint)
            {
                GenerateProperties(source, primitiveType, dimension, components, mathClass, structName);
            }

            // Indexer
            GenerateIndexer(source, primitiveType, dimension, components);

            // Operators
            GenerateOperators(source, structName, primitiveType, dimension, components, isSignedType);

            if (IsNumericType(primitiveType))
                GenerateConversions(source, structName, primitiveType, dimension, components);

            // Methods
            GenerateMethods(source, structName, primitiveType, dimension, components, isFloatingPoint, mathClass);

            source.AppendLine("    }");
            source.AppendLine("}");

            return source.ToString();
        }


        private static void GenerateConstructors(StringBuilder source, string structName, string primitiveType, int dimension, string[] components)
        {
            source.AppendLine("        // --- Constructors ---");

            // Scalar constructor
            source.AppendLine($"        /// <summary>Initializes all components to a single scalar value.</summary>");
            source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            var componentList = string.Join(", ", components.Select(c => "scalar").ToArray());
            source.AppendLine($"        public {structName}({primitiveType} scalar) : this({componentList}) {{ }}");
            source.AppendLine();

            // Individual components constructor
            source.AppendLine($"        /// <summary>Initializes with specified component values.</summary>");
            source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            var parameters = string.Join(", ", components.Select((c, i) => $"{primitiveType} {c.ToLower()}").ToArray());
            source.AppendLine($"        public {structName}({parameters})");
            source.AppendLine("        {");
            foreach (var component in components)
            {
                source.AppendLine($"            {component} = {component.ToLower()};");
            }
            source.AppendLine("        }");
            source.AppendLine();

            // Copy constructor
            source.AppendLine($"        /// <summary>Initializes by copying components from another {structName}.</summary>");
            source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            var copyParameters = string.Join(", ", components.Select(c => $"v.{c}").ToArray());
            source.AppendLine($"        public {structName}({structName} v) : this({copyParameters}) {{ }}");
            source.AppendLine();

            // Array constructor
            source.AppendLine($"        /// <summary>Initializes from an array.</summary>");
            source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            source.AppendLine($"        public {structName}({primitiveType}[] array)");
            source.AppendLine("        {");
            source.AppendLine($"            if (array == null) throw new ArgumentNullException(nameof(array));");
            source.AppendLine($"            if (array.Length < {dimension}) throw new ArgumentException(\"Array must contain at least {dimension} elements.\", nameof(array));");
            for (int i = 0; i < dimension; i++)
            {
                source.AppendLine($"            {components[i]} = array[{i}];");
            }
            source.AppendLine("        }");
            source.AppendLine();

            // Mixed constructors for higher dimensions
            var typeName = GetTypeName(primitiveType);

            if (dimension == 3)
            {
                // Float3(Float2, float) - XY + Z
                source.AppendLine($"        /// <summary>Initializes from a {typeName}2 and Z component.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                source.AppendLine($"        public {structName}({typeName}2 xy, {primitiveType} z) : this(xy.X, xy.Y, z) {{ }}");
                source.AppendLine();

                // Float3(float, Float2) - X + YZ
                source.AppendLine($"        /// <summary>Initializes from X component and a {typeName}2.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                source.AppendLine($"        public {structName}({primitiveType} x, {typeName}2 yz) : this(x, yz.X, yz.Y) {{ }}");
                source.AppendLine();
            }
            else if (dimension == 4)
            {
                // Float4(Float2, float, float) - XY + Z + W
                source.AppendLine($"        /// <summary>Initializes from a {typeName}2 and Z, W components.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                source.AppendLine($"        public {structName}({typeName}2 xy, {primitiveType} z, {primitiveType} w) : this(xy.X, xy.Y, z, w) {{ }}");
                source.AppendLine();

                // Float4(float, Float2, float) - X + YZ + W
                source.AppendLine($"        /// <summary>Initializes from X component, a {typeName}2, and W component.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                source.AppendLine($"        public {structName}({primitiveType} x, {typeName}2 yz, {primitiveType} w) : this(x, yz.X, yz.Y, w) {{ }}");
                source.AppendLine();

                // Float4(float, float, Float2) - X + Y + ZW
                source.AppendLine($"        /// <summary>Initializes from X, Y components and a {typeName}2.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                source.AppendLine($"        public {structName}({primitiveType} x, {primitiveType} y, {typeName}2 zw) : this(x, y, zw.X, zw.Y) {{ }}");
                source.AppendLine();

                // Float4(Float2, Float2) - XY + ZW
                source.AppendLine($"        /// <summary>Initializes from two {typeName}2 vectors.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                source.AppendLine($"        public {structName}({typeName}2 xy, {typeName}2 zw) : this(xy.X, xy.Y, zw.X, zw.Y) {{ }}");
                source.AppendLine();

                // Float4(Float3, float) - XYZ + W
                source.AppendLine($"        /// <summary>Initializes from a {typeName}3 and W component.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                source.AppendLine($"        public {structName}({typeName}3 xyz, {primitiveType} w) : this(xyz.X, xyz.Y, xyz.Z, w) {{ }}");
                source.AppendLine();

                // Float4(float, Float3) - X + YZW
                source.AppendLine($"        /// <summary>Initializes from X component and a {typeName}3.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                source.AppendLine($"        public {structName}({primitiveType} x, {typeName}3 yzw) : this(x, yzw.X, yzw.Y, yzw.Z) {{ }}");
                source.AppendLine();
            }

            // Type conversion constructors - only for numeric types
            if (IsNumericType(primitiveType))
            {
                GenerateConversionConstructors(source, structName, primitiveType, dimension, components, typeName);
            }

            // Readonly collection constructors
            GenerateCollectionConstructors(source, structName, primitiveType, dimension, components);
        }

        private static void GenerateConversionConstructors(StringBuilder source, string structName, string primitiveType, int dimension, string[] components, string typeName)
        {
            source.AppendLine("        // --- Type Conversion Constructors ---");

            // List of other numeric types for conversion
            var numericTypes = new[] { "float", "double", "int", "byte", "ushort", "uint", "ulong" };
            var otherTypes = numericTypes.Where(t => t != primitiveType).ToArray();

            foreach (var otherType in otherTypes)
            {
                var otherTypeName = GetTypeName(otherType);
                var otherStructName = $"{otherTypeName}{dimension}";

                source.AppendLine($"        /// <summary>Initializes from a {otherStructName} with type conversion.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var conversionParams = string.Join(", ", components.Select(c => $"({primitiveType})v.{c}").ToArray());
                source.AppendLine($"        public {structName}({otherStructName} v) : this({conversionParams}) {{ }}");
                source.AppendLine();
            }
        }

        private static void GenerateCollectionConstructors(StringBuilder source, string structName, string primitiveType, int dimension, string[] components)
        {
            source.AppendLine("        // --- Collection Constructors ---");

            // IEnumerable constructor
            source.AppendLine($"        /// <summary>Initializes from an IEnumerable collection.</summary>");
            source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            source.AppendLine($"        public {structName}(IEnumerable<{primitiveType}> values)");
            source.AppendLine("        {");
            source.AppendLine($"            var array = values.ToArray();");
            source.AppendLine($"            if (array.Length < {dimension}) throw new ArgumentException(\"Collection must contain at least {dimension} elements.\", nameof(values));");
            for (int i = 0; i < dimension; i++)
            {
                source.AppendLine($"            {components[i]} = array[{i}];");
            }
            source.AppendLine("        }");
            source.AppendLine();

            // ReadOnlySpan constructor
            source.AppendLine($"        /// <summary>Initializes from a ReadOnlySpan.</summary>");
            source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            source.AppendLine($"        public {structName}(ReadOnlySpan<{primitiveType}> span)");
            source.AppendLine("        {");
            source.AppendLine($"            if (span.Length < {dimension}) throw new ArgumentException(\"Span must contain at least {dimension} elements.\", nameof(span));");
            for (int i = 0; i < dimension; i++)
            {
                source.AppendLine($"            {components[i]} = span[{i}];");
            }
            source.AppendLine("        }");
            source.AppendLine();

            // Span constructor
            source.AppendLine($"        /// <summary>Initializes from a Span.</summary>");
            source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            source.AppendLine($"        public {structName}(Span<{primitiveType}> span)");
            source.AppendLine("        {");
            source.AppendLine($"            if (span.Length < {dimension}) throw new ArgumentException(\"Span must contain at least {dimension} elements.\", nameof(span));");
            for (int i = 0; i < dimension; i++)
            {
                source.AppendLine($"            {components[i]} = span[{i}];");
            }
            source.AppendLine("        }");
            source.AppendLine();
        }

        private static void GenerateStaticProperties(StringBuilder source, string structName, string primitiveType, int dimension, string[] components)
        {
            source.AppendLine("        // --- Static Properties ---");

            // Zero vector
            var zeroComponents = string.Join(", ", components.Select(c => GetZeroValue(primitiveType)).ToArray());
            source.AppendLine($"        /// <summary>Gets the zero vector.</summary>");
            source.AppendLine($"        public static {structName} Zero {{ get {{ return new {structName}({zeroComponents}); }} }}");

            // One vector
            var oneComponents = string.Join(", ", components.Select(c => GetOneValue(primitiveType)).ToArray());
            source.AppendLine($"        /// <summary>Gets the one vector.</summary>");
            source.AppendLine($"        public static {structName} One {{ get {{ return new {structName}({oneComponents}); }} }}");

            // Unit vectors
            for (int i = 0; i < dimension; i++)
            {
                var unitComponents = new List<string>();
                for (int j = 0; j < dimension; j++)
                {
                    unitComponents.Add(i == j ? GetOneValue(primitiveType) : GetZeroValue(primitiveType));
                }
                var componentList = string.Join(", ", unitComponents.ToArray());
                source.AppendLine($"        /// <summary>Gets the unit vector along the {components[i]}-axis.</summary>");
                source.AppendLine($"        public static {structName} Unit{components[i]} {{ get {{ return new {structName}({componentList}); }} }}");
            }

            source.AppendLine();
        }

        private static string GetZeroValue(string primitiveType)
        {
            if (primitiveType == "float") return "0f";
            if (primitiveType == "double") return "0.0";
            if (primitiveType == "byte") return "(byte)0";
            if (primitiveType == "ushort") return "(ushort)0";
            if (primitiveType == "uint") return "0u";
            if (primitiveType == "ulong") return "0ul";
            if (primitiveType == "bool") return "false";
            return "0";
        }

        private static string GetOneValue(string primitiveType)
        {
            if (primitiveType == "float") return "1f";
            if (primitiveType == "double") return "1.0";
            if (primitiveType == "byte") return "(byte)1";
            if (primitiveType == "ushort") return "(ushort)1";
            if (primitiveType == "uint") return "1u";
            if (primitiveType == "ulong") return "1ul";
            if (primitiveType == "bool") return "true";
            return "1";
        }

        private static void GenerateProperties(StringBuilder source, string primitiveType, int dimension, string[] components, string mathClass, string structName)
        {
            source.AppendLine("        // --- Properties ---");

            // Length
            source.AppendLine($"        /// <summary>Gets the magnitude (length) of the vector.</summary>");
            source.AppendLine($"        public {primitiveType} Length");
            source.AppendLine("        {");
            source.AppendLine($"            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            source.AppendLine($"            get {{ return {mathClass}.Sqrt(LengthSquared); }}");
            source.AppendLine("        }");
            source.AppendLine();

            // LengthSquared
            source.AppendLine($"        /// <summary>Gets the squared magnitude (length) of the vector.</summary>");
            source.AppendLine($"        public {primitiveType} LengthSquared");
            source.AppendLine("        {");
            source.AppendLine($"            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            var lengthSquaredExpression = string.Join(" + ", components.Select(c => $"{c} * {c}").ToArray());
            source.AppendLine($"            get {{ return {lengthSquaredExpression}; }}");
            source.AppendLine("        }");
            source.AppendLine();

            // Normalized
            source.AppendLine($"        /// <summary>Gets a normalized version of this vector.</summary>");
            source.AppendLine($"        public {structName} Normalized");
            source.AppendLine("        {");
            source.AppendLine($"            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            source.AppendLine("            get");
            source.AppendLine("            {");
            source.AppendLine($"                {primitiveType} lenSq = LengthSquared;");
            if (primitiveType == "float" || primitiveType == "double")
            {
                source.AppendLine($"                if (lenSq <= {primitiveType}.Epsilon * {primitiveType}.Epsilon)");
            }
            else
            {
                source.AppendLine($"                if (lenSq <= {primitiveType}.Epsilon)");
            }
            source.AppendLine("                {");
            source.AppendLine("                    return Zero;");
            source.AppendLine("                }");
            source.AppendLine($"                {primitiveType} invLength = {GetOneValue(primitiveType)} / {mathClass}.Sqrt(lenSq);");
            var normalizedComponents = string.Join(", ", components.Select(c => $"{c} * invLength").ToArray());
            source.AppendLine($"                return new {structName}({normalizedComponents});");
            source.AppendLine("            }");
            source.AppendLine("        }");
            source.AppendLine();
        }

        private static void GenerateIndexer(StringBuilder source, string primitiveType, int dimension, string[] components)
        {
            source.AppendLine("        // --- Indexer ---");
            source.AppendLine($"        /// <summary>Gets or sets the component at the specified index.</summary>");
            source.AppendLine($"        public {primitiveType} this[int index]");
            source.AppendLine("        {");
            source.AppendLine($"            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            source.AppendLine("            get");
            source.AppendLine("            {");
            source.AppendLine("                switch (index)");
            source.AppendLine("                {");
            for (int i = 0; i < dimension; i++)
            {
                source.AppendLine($"                    case {i}: return {components[i]};");
            }
            source.AppendLine($"                    default: throw new IndexOutOfRangeException(string.Format(\"Index must be between 0 and {dimension - 1}, but was {{0}}\", index));");
            source.AppendLine("                }");
            source.AppendLine("            }");
            source.AppendLine($"            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            source.AppendLine("            set");
            source.AppendLine("            {");
            source.AppendLine("                switch (index)");
            source.AppendLine("                {");
            for (int i = 0; i < dimension; i++)
            {
                source.AppendLine($"                    case {i}: {components[i]} = value; break;");
            }
            source.AppendLine($"                    default: throw new IndexOutOfRangeException(string.Format(\"Index must be between 0 and {dimension - 1}, but was {{0}}\", index));");
            source.AppendLine("                }");
            source.AppendLine("            }");
            source.AppendLine("        }");
            source.AppendLine();
        }

        private static void GenerateOperators(StringBuilder source, string structName, string primitiveType, int dimension, string[] components, bool isSignedType)
        {
            source.AppendLine("        // --- Vector-to-Vector Operators ---");

            // For boolean vectors, we need different operators
            if (primitiveType == "bool")
            {
                // Logical AND
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var andComponents = string.Join(", ", components.Select(c => $"a.{c} && b.{c}").ToArray());
                source.AppendLine($"        public static {structName} operator &({structName} a, {structName} b) {{ return new {structName}({andComponents}); }}");
                source.AppendLine();

                // Logical OR
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var orComponents = string.Join(", ", components.Select(c => $"a.{c} || b.{c}").ToArray());
                source.AppendLine($"        public static {structName} operator |({structName} a, {structName} b) {{ return new {structName}({orComponents}); }}");
                source.AppendLine();

                // Logical XOR
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var xorComponents = string.Join(", ", components.Select(c => $"a.{c} ^ b.{c}").ToArray());
                source.AppendLine($"        public static {structName} operator ^({structName} a, {structName} b) {{ return new {structName}({xorComponents}); }}");
                source.AppendLine();

                // Logical NOT
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var notComponents = string.Join(", ", components.Select(c => $"!v.{c}").ToArray());
                source.AppendLine($"        public static {structName} operator !({structName} v) {{ return new {structName}({notComponents}); }}");
                source.AppendLine();
            }
            else
            {
                // Vector-to-vector arithmetic operators

                // Addition
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                string addComponents;
                if (primitiveType == "byte" || primitiveType == "ushort")
                {
                    // Need explicit casting for small integer types due to integer promotion
                    addComponents = string.Join(", ", components.Select(c => $"({primitiveType})(a.{c} + b.{c})").ToArray());
                }
                else
                {
                    addComponents = string.Join(", ", components.Select(c => $"a.{c} + b.{c}").ToArray());
                }
                source.AppendLine($"        public static {structName} operator +({structName} a, {structName} b) {{ return new {structName}({addComponents}); }}");
                source.AppendLine();

                // Subtraction
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                string subComponents;
                if (primitiveType == "byte" || primitiveType == "ushort")
                {
                    subComponents = string.Join(", ", components.Select(c => $"({primitiveType})(a.{c} - b.{c})").ToArray());
                }
                else
                {
                    subComponents = string.Join(", ", components.Select(c => $"a.{c} - b.{c}").ToArray());
                }
                source.AppendLine($"        public static {structName} operator -({structName} a, {structName} b) {{ return new {structName}({subComponents}); }}");
                source.AppendLine();

                // Multiplication (component-wise)
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                string mulComponents;
                if (primitiveType == "byte" || primitiveType == "ushort")
                {
                    mulComponents = string.Join(", ", components.Select(c => $"({primitiveType})(a.{c} * b.{c})").ToArray());
                }
                else
                {
                    mulComponents = string.Join(", ", components.Select(c => $"a.{c} * b.{c}").ToArray());
                }
                source.AppendLine($"        public static {structName} operator *({structName} a, {structName} b) {{ return new {structName}({mulComponents}); }}");
                source.AppendLine();

                // Division (component-wise)
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                string divComponents;
                if (primitiveType == "byte" || primitiveType == "ushort")
                {
                    divComponents = string.Join(", ", components.Select(c => $"({primitiveType})(a.{c} / b.{c})").ToArray());
                }
                else
                {
                    divComponents = string.Join(", ", components.Select(c => $"a.{c} / b.{c}").ToArray());
                }
                source.AppendLine($"        public static {structName} operator /({structName} a, {structName} b) {{ return new {structName}({divComponents}); }}");
                source.AppendLine();

                // Modulus (component-wise)
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                string modComponents;
                if (primitiveType == "byte" || primitiveType == "ushort")
                {
                    modComponents = string.Join(", ", components.Select(c => $"({primitiveType})(a.{c} % b.{c})").ToArray());
                }
                else
                {
                    modComponents = string.Join(", ", components.Select(c => $"a.{c} % b.{c}").ToArray());
                }
                source.AppendLine($"        public static {structName} operator %({structName} a, {structName} b) {{ return new {structName}({modComponents}); }}");
                source.AppendLine();

                // Negation (only for signed types)
                if (isSignedType)
                {
                    source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    string negComponents;
                    if (primitiveType == "byte" || primitiveType == "ushort")
                    {
                        negComponents = string.Join(", ", components.Select(c => $"({primitiveType})-v.{c}").ToArray());
                    }
                    else
                    {
                        negComponents = string.Join(", ", components.Select(c => $"-v.{c}").ToArray());
                    }
                    source.AppendLine($"        public static {structName} operator -({structName} v) {{ return new {structName}({negComponents}); }}");
                    source.AppendLine();
                }

                source.AppendLine("        // --- Scalar-Vector Operators ---");

                // Generate operators for all types (including same type)
                foreach (var scalarType in types.Where(t => IsNumericType(t)))
                {
                    bool castScalar = false;
                    if (primitiveType == "ulong" && scalarType == "int")
                        castScalar = true;
                    else if (primitiveType == "int" && scalarType == "ulong")
                        castScalar = true;

                    // scalar + vector
                    source.AppendLine($"        /// <summary>{scalarType} + {structName} operator. Vector components are ({string.Join(", ", components.Select(c => $"scalar + v.{c}"))}).</summary>");
                    source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    GenerateScalarVectorOp(source, "+", scalarType, structName, primitiveType, components, true, castScalar);

                    // vector + scalar
                    source.AppendLine($"        /// <summary>{structName} + {scalarType} operator. Vector components are ({string.Join(", ", components.Select(c => $"v.{c} + scalar"))}).</summary>");
                    source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    GenerateScalarVectorOp(source, "+", scalarType, structName, primitiveType, components, false, castScalar);

                    // scalar - vector
                    source.AppendLine($"        /// <summary>{scalarType} - {structName} operator. Vector components are ({string.Join(", ", components.Select(c => $"scalar - v.{c}"))}).</summary>");
                    source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    GenerateScalarVectorOp(source, "-", scalarType, structName, primitiveType, components, true, castScalar);

                    // vector - scalar
                    source.AppendLine($"        /// <summary>{structName} - {scalarType} operator. Vector components are ({string.Join(", ", components.Select(c => $"v.{c} - scalar"))}).</summary>");
                    source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    GenerateScalarVectorOp(source, "-", scalarType, structName, primitiveType, components, false, castScalar);

                    // scalar * vector
                    source.AppendLine($"        /// <summary>{scalarType} * {structName} operator. Vector components are ({string.Join(", ", components.Select(c => $"scalar * v.{c}"))}).</summary>");
                    source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    GenerateScalarVectorOp(source, "*", scalarType, structName, primitiveType, components, true, castScalar);

                    // vector * scalar
                    source.AppendLine($"        /// <summary>{structName} * {scalarType} operator. Vector components are ({string.Join(", ", components.Select(c => $"v.{c} * scalar"))}).</summary>");
                    source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    GenerateScalarVectorOp(source, "*", scalarType, structName, primitiveType, components, false, castScalar);

                    // scalar / vector
                    source.AppendLine($"        /// <summary>{scalarType} / {structName} operator. Vector components are ({string.Join(", ", components.Select(c => $"v.{c} / scalar"))}).</summary>");
                    source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    GenerateScalarVectorOp(source, "/", scalarType, structName, primitiveType, components, false, castScalar);

                    // scalar / vector
                    source.AppendLine($"        /// <summary>{scalarType} / {structName} operator. Vector components are ({string.Join(", ", components.Select(c => $"scalar / v.{c}"))}).</summary>");
                    source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    GenerateScalarVectorOp(source, "/", scalarType, structName, primitiveType, components, true, castScalar);

                    // vector % scalar
                    source.AppendLine($"        /// <summary>{structName} % {scalarType} operator. Vector components are ({string.Join(", ", components.Select(c => $"v.{c} % scalar"))}).</summary>");
                    source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    GenerateScalarVectorOp(source, "%", scalarType, structName, primitiveType, components, false, castScalar);

                    // scalar % vector
                    source.AppendLine($"        /// <summary>{scalarType} % {structName} operator. Vector components are ({string.Join(", ", components.Select(c => $"scalar % v.{c}"))}).</summary>");
                    source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    GenerateScalarVectorOp(source, "%", scalarType, structName, primitiveType, components, true, castScalar);

                }

                // --- Component-wise Comparison Operators (Return Boolean Vector) ---
                // These are for non-bool primitive types, returning a BoolN vector.
                source.AppendLine("        // --- Component-wise Comparison Operators (Return Boolean Vector) ---");
                string boolVectorName = $"Bool{dimension}"; // e.g., Bool2, Bool3, Bool4

                string[] comparisonOperators = { "<", "<=", ">", ">=", "==", "!=" };

                foreach (string op in comparisonOperators)
                {
                    // Vector OP Vector -> BoolN
                    source.AppendLine($"        /// <summary>Returns a {boolVectorName} indicating the result of component-wise {op} comparison.</summary>");
                    source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    var comparisonComponentsVV = string.Join(", ", components.Select(c => $"a.{c} {op} b.{c}"));
                    source.AppendLine($"        public static {boolVectorName} operator {op}({structName} a, {structName} b) {{ return new {boolVectorName}({comparisonComponentsVV}); }}");
                    source.AppendLine();

                    // Vector OP Scalar -> BoolN
                    source.AppendLine($"        /// <summary>Returns a {boolVectorName} indicating the result of component-wise {op} comparison with a scalar.</summary>");
                    source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    var comparisonComponentsVS = string.Join(", ", components.Select(c => $"a.{c} {op} scalar"));
                    source.AppendLine($"        public static {boolVectorName} operator {op}({structName} a, {primitiveType} scalar) {{ return new {boolVectorName}({comparisonComponentsVS}); }}");
                    source.AppendLine();

                    // Scalar OP Vector -> BoolN
                    source.AppendLine($"        /// <summary>Returns a {boolVectorName} indicating the result of component-wise {op} comparison with a scalar.</summary>");
                    source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    var comparisonComponentsSV = string.Join(", ", components.Select(c => $"scalar {op} a.{c}"));
                    source.AppendLine($"        public static {boolVectorName} operator {op}({primitiveType} scalar, {structName} a) {{ return new {boolVectorName}({comparisonComponentsSV}); }}");
                    source.AppendLine();
                }
            }
        }

        private static void GenerateScalarVectorOp(StringBuilder source, string op, string scalarType, string vectorType, string vectorPrimitiveType, string[] components, bool scalarFirst, bool castScalar)
        {
            string operand1, operand2;
            if (scalarFirst)
            {
                operand1 = $"{scalarType} scalar";
                operand2 = $"{vectorType} v";
            }
            else
            {
                operand1 = $"{vectorType} v";
                operand2 = $"{scalarType} scalar";
            }

            source.AppendLine($"        public static {vectorType} operator {op}({operand1}, {operand2})");
            source.AppendLine("        {");

            // Generate component expressions with proper casting
            var componentExpressions = new List<string>();
            foreach (var c in components)
            {
                string expr;
                if (scalarFirst)
                {
                    expr = op switch
                    {
                        "+" => $"{(castScalar ? $"({vectorPrimitiveType})" : "")}scalar + v.{c}",
                        "-" => $"{(castScalar ? $"({vectorPrimitiveType})" : "")}scalar - v.{c}",
                        "*" => $"{(castScalar ? $"({vectorPrimitiveType})" : "")}scalar * v.{c}",
                        "/" => $"{(castScalar ? $"({vectorPrimitiveType})" : "")}scalar / v.{c}",
                        "%" => $"{(castScalar ? $"({vectorPrimitiveType})" : "")}scalar % v.{c}",
                        _ => throw new NotSupportedException($"Operator {op} not supported")
                    };
                }
                else
                {
                    expr = op switch
                    {
                        "+" => $"v.{c} + {(castScalar ? $"({vectorPrimitiveType})" : "")}scalar",
                        "-" => $"v.{c} - {(castScalar ? $"({vectorPrimitiveType})" : "")}scalar",
                        "*" => $"v.{c} * {(castScalar ? $"({vectorPrimitiveType})" : "")}scalar",
                        "/" => $"v.{c} / {(castScalar ? $"({vectorPrimitiveType})" : "")}scalar",
                        "%" => $"v.{c} % {(castScalar ? $"({vectorPrimitiveType})" : "")}scalar",
                        _ => throw new NotSupportedException($"Operator {op} not supported")
                    };
                }

                // Always cast the entire expression to the target type
                componentExpressions.Add($"({vectorPrimitiveType})({expr})");
            }

            var constructorArgs = string.Join(", ", componentExpressions);
            source.AppendLine($"            return new {vectorType}({constructorArgs});");

            source.AppendLine("        }");
            source.AppendLine();
        }

        private static void GenerateConversions(StringBuilder source, string structName, string primitiveType, int dimension, string[] components)
        {
            var typeName = GetTypeName(primitiveType);

            source.AppendLine("        // --- Implicit Conversions ---");

            // Add implicit conversions to System.Numerics types
            if (IsSystemNumericsCompatible(primitiveType, dimension))
            {
                string numericsType = $"Vector{dimension}";

                // To System.Numerics.Vector
                source.AppendLine($"        /// <summary>Implicitly converts this {structName} to a System.Numerics.{numericsType}.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var toNumericsArgs = string.Join(", ", components.Select(c => $"value.{c}"));
                source.AppendLine($"        public static implicit operator {numericsType}({structName} value) => new {numericsType}({toNumericsArgs});");
                source.AppendLine();

                // From System.Numerics.Vector
                source.AppendLine($"        /// <summary>Implicitly converts a System.Numerics.{numericsType} to this {structName}.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var fromNumericsArgs = string.Join(", ", components.Select(c => $"value.{c}"));
                source.AppendLine($"        public static implicit operator {structName}({numericsType} value) => new {structName}({fromNumericsArgs});");
                source.AppendLine();
            }

            // Add implicit conversions between same-type different dimensions
            GenerateVectorDimensionConversions(source, structName, typeName, primitiveType, dimension, components);

            // Add explicit conversions between different types same dimension
            GenerateCrossTypeConversions(source, structName, typeName, primitiveType, dimension, components);
        }

        private static void GenerateVectorDimensionConversions(StringBuilder source, string structName, string typeName, string primitiveType, int dimension, string[] components)
        {
            // Implicit conversions between different dimensions of same type

            // Convert from lower dimensions (implicit)
            if (dimension > 2)
            {
                // From Vector2 (add zeros for missing components)
                source.AppendLine($"        /// <summary>Implicitly converts a {typeName}2 to {structName} by adding default values for missing components.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var args2D = new List<string> { "value.X", "value.Y" };
                for (int i = 2; i < dimension; i++)
                {
                    args2D.Add(GetZeroValue(primitiveType));
                }
                source.AppendLine($"        public static implicit operator {structName}({typeName}2 value) => new {structName}({string.Join(", ", args2D)});");
                source.AppendLine();
            }

            if (dimension > 3)
            {
                // From Vector3 (add zeros for missing components)
                source.AppendLine($"        /// <summary>Implicitly converts a {typeName}3 to {structName} by adding default values for missing components.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var args3D = new List<string> { "value.X", "value.Y", "value.Z" };
                for (int i = 3; i < dimension; i++)
                {
                    args3D.Add(GetZeroValue(primitiveType));
                }
                source.AppendLine($"        public static implicit operator {structName}({typeName}3 value) => new {structName}({string.Join(", ", args3D)});");
                source.AppendLine();
            }

            // Explicit conversions to lower dimensions (truncation)
            if (dimension > 2)
            {
                // To Vector2 (explicit because it loses data)
                source.AppendLine($"        /// <summary>Explicitly converts {structName} to {typeName}2 by truncating components.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                source.AppendLine($"        public static explicit operator {typeName}2({structName} value) => new {typeName}2(value.X, value.Y);");
                source.AppendLine();
            }

            if (dimension > 3)
            {
                // To Vector3 (explicit because it loses data)
                source.AppendLine($"        /// <summary>Explicitly converts {structName} to {typeName}3 by truncating components.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                source.AppendLine($"        public static explicit operator {typeName}3({structName} value) => new {typeName}3(value.X, value.Y, value.Z);");
                source.AppendLine();
            }
        }

        private static void GenerateCrossTypeConversions(StringBuilder source, string structName, string typeName, string primitiveType, int dimension, string[] components)
        {
            // Explicit conversions between different types of same dimension
            var allTypes = new[] { "float", "double", "int", "byte", "ushort", "uint", "ulong" };
            var compatibleTypes = allTypes.Where(t => t != primitiveType && IsNumericType(t)).ToArray();

            foreach (var otherType in compatibleTypes)
            {
                var otherTypeName = GetTypeName(otherType);
                var otherStructName = $"{otherTypeName}{dimension}";

                // Check if conversion is narrowing or widening
                bool isNarrowing = IsNarrowingConversion(primitiveType, otherType);
                string conversionType = isNarrowing ? "explicit" : "implicit";
                string comment = isNarrowing ? "Explicitly" : "Implicitly";

                source.AppendLine($"        /// <summary>{comment} converts {structName} to {otherStructName}.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var castComponents = string.Join(", ", components.Select(c => $"({otherType})value.{c}"));
                source.AppendLine($"        public static {conversionType} operator {otherStructName}({structName} value) => new {otherStructName}({castComponents});");
                source.AppendLine();
            }
        }

        private static void GenerateMethods(StringBuilder source, string structName, string primitiveType, int dimension, string[] components, bool isFloatingPoint, string mathClass)
        {
            source.AppendLine("        // --- Methods ---");

            // For boolean vectors, add some useful methods
            if (primitiveType == "bool")
            {
                // Any() method - returns true if any component is true
                source.AppendLine($"        /// <summary>Returns true if any component is true.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var anyComponents = string.Join(" || ", components.Select(c => c).ToArray());
                source.AppendLine($"        public bool Any() {{ return {anyComponents}; }}");
                source.AppendLine();

                // All() method - returns true if all components are true
                source.AppendLine($"        /// <summary>Returns true if all components are true.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var allComponents = string.Join(" && ", components.Select(c => c).ToArray());
                source.AppendLine($"        public bool All() {{ return {allComponents}; }}");
                source.AppendLine();

                // None() method - returns true if no components are true
                source.AppendLine($"        /// <summary>Returns true if all components are false.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var noneComponents = string.Join(" && ", components.Select(c => $"!{c}").ToArray());
                source.AppendLine($"        public bool None() {{ return {noneComponents}; }}");
                source.AppendLine();
            }

            // For all non-boolean types, add comparison methods that return boolean vectors
            if (primitiveType != "bool")
            {
                var boolStructName = $"Bool{dimension}";

                // LessThan
                source.AppendLine($"        /// <summary>Returns a boolean vector indicating which components are less than the corresponding components of another vector.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var lessThanComponents = string.Join(", ", components.Select(c => $"{c} < other.{c}").ToArray());
                source.AppendLine($"        public {boolStructName} LessThan({structName} other) {{ return new {boolStructName}({lessThanComponents}); }}");
                source.AppendLine();

                // LessThanOrEqual
                source.AppendLine($"        /// <summary>Returns a boolean vector indicating which components are less than or equal to the corresponding components of another vector.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var lessThanOrEqualComponents = string.Join(", ", components.Select(c => $"{c} <= other.{c}").ToArray());
                source.AppendLine($"        public {boolStructName} LessThanOrEqual({structName} other) {{ return new {boolStructName}({lessThanOrEqualComponents}); }}");
                source.AppendLine();

                // GreaterThan
                source.AppendLine($"        /// <summary>Returns a boolean vector indicating which components are greater than the corresponding components of another vector.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var greaterThanComponents = string.Join(", ", components.Select(c => $"{c} > other.{c}").ToArray());
                source.AppendLine($"        public {boolStructName} GreaterThan({structName} other) {{ return new {boolStructName}({greaterThanComponents}); }}");
                source.AppendLine();

                // GreaterThanOrEqual
                source.AppendLine($"        /// <summary>Returns a boolean vector indicating which components are greater than or equal to the corresponding components of another vector.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var greaterThanOrEqualComponents = string.Join(", ", components.Select(c => $"{c} >= other.{c}").ToArray());
                source.AppendLine($"        public {boolStructName} GreaterThanOrEqual({structName} other) {{ return new {boolStructName}({greaterThanOrEqualComponents}); }}");
                source.AppendLine();

                // Select method - chooses between two vectors based on a boolean mask
                source.AppendLine($"        /// <summary>Selects components from two vectors based on a boolean mask.</summary>");
                source.AppendLine($"        /// <param name=\"mask\">Boolean vector mask for selection.</param>");
                source.AppendLine($"        /// <param name=\"trueValue\">Vector to select from when mask component is true.</param>");
                source.AppendLine($"        /// <param name=\"falseValue\">Vector to select from when mask component is false.</param>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var selectComponents = string.Join(", ", components.Select((c, i) => $"mask.{c} ? trueValue.{c} : falseValue.{c}").ToArray());
                source.AppendLine($"        public static {structName} Select({boolStructName} mask, {structName} trueValue, {structName} falseValue)");
                source.AppendLine("        {");
                source.AppendLine($"            return new {structName}({selectComponents});");
                source.AppendLine("        }");
                source.AppendLine();

                // IsInRange (component-wise range check)
                source.AppendLine($"        /// <summary>Returns a boolean vector indicating which components are within the specified range.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var inRangeComponents = string.Join(", ", components.Select(c => $"{c} >= min.{c} && {c} <= max.{c}").ToArray());
                source.AppendLine($"        public {boolStructName} InRange({structName} min, {structName} max)");
                source.AppendLine("        {");
                source.AppendLine($"            return new {boolStructName}({inRangeComponents});");
                source.AppendLine("        }");
                source.AppendLine();

                // EqualTo (component-wise equality that returns a boolean vector)
                source.AppendLine($"        /// <summary>Returns a boolean vector indicating which components are equal to the corresponding components of another vector.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var equalToComponents = string.Join(", ", components.Select(c => $"{c} == other.{c}").ToArray());
                source.AppendLine($"        public {boolStructName} EqualTo({structName} other) {{ return new {boolStructName}({equalToComponents}); }}");
                source.AppendLine();

                // NotEqualTo
                source.AppendLine($"        /// <summary>Returns a boolean vector indicating which components are not equal to the corresponding components of another vector.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var notEqualToComponents = string.Join(", ", components.Select(c => $"{c} != other.{c}").ToArray());
                source.AppendLine($"        public {boolStructName} NotEqualTo({structName} other) {{ return new {boolStructName}({notEqualToComponents}); }}");
                source.AppendLine();

                // ApproximatelyEqualTo (for floating-point types)
                if (isFloatingPoint)
                {
                    source.AppendLine($"        /// <summary>Returns a boolean vector indicating which components are approximately equal to the corresponding components of another vector.</summary>");
                    source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    var approxEqualComponents = string.Join(", ", components.Select(c => $"{mathClass}.Abs({c} - other.{c}) <= {primitiveType}.Epsilon").ToArray());
                    source.AppendLine($"        public {boolStructName} ApproximatelyEqualTo({structName} other) {{ return new {boolStructName}({approxEqualComponents}); }}");
                    source.AppendLine();

                    source.AppendLine($"        /// <summary>Returns a boolean vector indicating which components are approximately equal to the corresponding components of another vector with a custom tolerance.</summary>");
                    source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    var approxEqualToleranceComponents = string.Join(", ", components.Select(c => $"{mathClass}.Abs({c} - other.{c}) <= tolerance").ToArray());
                    source.AppendLine($"        public {boolStructName} ApproximatelyEqualTo({structName} other, {primitiveType} tolerance)");
                    source.AppendLine("        {");
                    source.AppendLine($"            return new {boolStructName}({approxEqualToleranceComponents});");
                    source.AppendLine("        }");
                    source.AppendLine();
                }

                // Scalar comparison methods
                source.AppendLine($"        /// <summary>Returns a boolean vector indicating which components are less than a scalar value.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var scalarLessThanComponents = string.Join(", ", components.Select(c => $"{c} < scalar").ToArray());
                source.AppendLine($"        public {boolStructName} LessThan({primitiveType} scalar) {{ return new {boolStructName}({scalarLessThanComponents}); }}");
                source.AppendLine();

                source.AppendLine($"        /// <summary>Returns a boolean vector indicating which components are greater than a scalar value.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var scalarGreaterThanComponents = string.Join(", ", components.Select(c => $"{c} > scalar").ToArray());
                source.AppendLine($"        public {boolStructName} GreaterThan({primitiveType} scalar) {{ return new {boolStructName}({scalarGreaterThanComponents}); }}");
                source.AppendLine();

                source.AppendLine($"        /// <summary>Returns a boolean vector indicating which components are equal to a scalar value.</summary>");
                source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                var scalarEqualToComponents = string.Join(", ", components.Select(c => $"{c} == scalar").ToArray());
                source.AppendLine($"        public {boolStructName} EqualTo({primitiveType} scalar) {{ return new {boolStructName}({scalarEqualToComponents}); }}");
                source.AppendLine();
            }

            // ToArray
            source.AppendLine($"        /// <summary>Returns an array of components.</summary>");
            source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            source.AppendLine($"        public {primitiveType}[] ToArray() {{ return new {primitiveType}[] {{ {string.Join(", ", components)} }}; }}");
            source.AppendLine($"        /// <summary>Returns an array of components.</summary>");

            // Equals
            source.AppendLine($"        public override bool Equals(object obj) {{ return obj is {structName} && Equals(({structName})obj); }}");
            source.AppendLine();

            source.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            var equalsComponents = string.Join(" && ", components.Select(c => $"{c} == other.{c}").ToArray());
            source.AppendLine($"        public bool Equals({structName} other) {{ return {equalsComponents}; }}");
            source.AppendLine();

            // GetHashCode
            if (dimension == 2)
            {
                source.AppendLine($"        public override int GetHashCode() {{ return {components[0]}.GetHashCode() ^ ({components[1]}.GetHashCode() << 2); }}");
            }
            else if (dimension == 3)
            {
                source.AppendLine($"        public override int GetHashCode() {{ return {components[0]}.GetHashCode() ^ ({components[1]}.GetHashCode() << 2) ^ ({components[2]}.GetHashCode() >> 2); }}");
            }
            else if (dimension == 4)
            {
                source.AppendLine($"        public override int GetHashCode() {{ return {components[0]}.GetHashCode() ^ ({components[1]}.GetHashCode() << 2) ^ ({components[2]}.GetHashCode() >> 2) ^ ({components[3]}.GetHashCode() >> 1); }}");
            }
            source.AppendLine();

            // ToString - different implementation for bool vs other types
            if (primitiveType == "bool")
            {
                // For boolean vectors, use True/False instead of numeric formatting
                source.AppendLine($"        public override string ToString()");
                source.AppendLine("        {");
                source.AppendLine($"            return ToString(CultureInfo.CurrentCulture);");
                source.AppendLine("        }");
                source.AppendLine();

                source.AppendLine($"        public string ToString(IFormatProvider formatProvider)");
                source.AppendLine("        {");
                source.AppendLine($"            string separator = (formatProvider is CultureInfo) ? ((CultureInfo)formatProvider).TextInfo.ListSeparator : \", \";");
                var boolToStringComponents = new List<string>();
                foreach (var c in components)
                {
                    boolToStringComponents.Add($"{c}.ToString()");
                }
                var boolToStringExpression = string.Join(" + separator + ", boolToStringComponents.ToArray());
                source.AppendLine($"            return \"(\" + {boolToStringExpression} + \")\";");
                source.AppendLine("        }");
                source.AppendLine();

                // Simplified format method for bool (format parameter is ignored for booleans)
                source.AppendLine($"        public string ToString(string format) {{ return ToString(CultureInfo.CurrentCulture); }}");
                source.AppendLine();

                source.AppendLine($"        public string ToString(string format, IFormatProvider formatProvider)");
                source.AppendLine("        {");
                source.AppendLine("            // Format is ignored for boolean vectors");
                source.AppendLine("            return ToString(formatProvider);");
                source.AppendLine("        }");
            }
            else
            {
                // For numeric types, use the existing format-based ToString
                source.AppendLine($"        public override string ToString() {{ return ToString(\"G\", CultureInfo.CurrentCulture); }}");
                source.AppendLine();

                source.AppendLine($"        public string ToString(string format) {{ return ToString(format, CultureInfo.CurrentCulture); }}");
                source.AppendLine();

                source.AppendLine($"        public string ToString(string format, IFormatProvider formatProvider)");
                source.AppendLine("        {");
                source.AppendLine($"            string separator = (formatProvider is CultureInfo) ? ((CultureInfo)formatProvider).TextInfo.ListSeparator : \", \";");
                var toStringComponents = new List<string>();
                foreach (var c in components)
                {
                    toStringComponents.Add($"{c}.ToString(format, formatProvider)");
                }
                var toStringExpression = string.Join(" + separator + ", toStringComponents.ToArray());
                source.AppendLine($"            return \"(\" + {toStringExpression} + \")\";");
                source.AppendLine("        }");
            }
        }

        private static bool IsNarrowingConversion(string fromType, string toType)
        {
            // Define type hierarchies for implicit conversion
            var typeHierarchy = new Dictionary<string, int>
            {
                ["byte"] = 1,
                ["ushort"] = 2,
                ["uint"] = 3,
                ["ulong"] = 4,
                ["int"] = 5,
                ["float"] = 6,
                ["double"] = 7
            };

            if (!typeHierarchy.ContainsKey(fromType) || !typeHierarchy.ContainsKey(toType))
                return true; // Unknown types default to explicit

            // Conversion is narrowing if going to a lower-ranked type
            return typeHierarchy[fromType] > typeHierarchy[toType];
        }

        #endregion

        #region Swizzle Generation Methods

        // Generates the swizzle code string for a single configured struct
        private static string GenerateSwizzlesForStruct(VectorSwizzleConfig config)
        {
            StringBuilder sb = new StringBuilder();

            // Add auto-generated header comment
            AddHeader(sb);

            sb.AppendLine("using System.Runtime.CompilerServices;");
            sb.AppendLine();
            sb.AppendLine($"namespace Prowl.Vector");
            sb.AppendLine("{");
            sb.AppendLine($"    public partial struct {config.StructName}"); // Generate as partial
            sb.AppendLine("    {");

            int sourceDimension = config.SourceFieldNames.Length;

            foreach (var charSetDef in s_swizzleCharSets)
            {
                // Determine the effective swizzle characters for this vector's dimension
                char[] currentSwizzleChars = charSetDef.Chars.Take(sourceDimension).ToArray();
                if (currentSwizzleChars.Length == 0) continue;

                for (int outputDim = 1; outputDim <= MAX_OUTPUT_SWIZZLE_DIMENSION; outputDim++)
                {
                    // Recursive call to generate permutations and append to StringBuilder
                    GeneratePermutationsRecursive(sb, config, currentSwizzleChars, sourceDimension, outputDim, new int[outputDim], 0);
                }
            }
            sb.AppendLine("    }"); // end struct
            sb.AppendLine("}"); // end namespace

            return sb.ToString();
        }

        // Recursive helper to generate permutations and build the code string
        private static void GeneratePermutationsRecursive(StringBuilder sb, VectorSwizzleConfig config, char[] swizzleChars, int sourceDim, int outputDim, int[] currentPermutationIndices, int depth)
        {
            if (depth == outputDim)
            {
                // process the completed permutation
                AppendSwizzleProperty(sb, config, swizzleChars, currentPermutationIndices, outputDim);
                return;
            }

            for (int i = 0; i < sourceDim; i++) // Iterate through available source component indices
            {
                currentPermutationIndices[depth] = i;
                GeneratePermutationsRecursive(sb, config, swizzleChars, sourceDim, outputDim, currentPermutationIndices, depth + 1);
            }
        }

        // Appends a single swizzle property definition to the StringBuilder
        private static void AppendSwizzleProperty(StringBuilder sb, VectorSwizzleConfig config, char[] availableSwizzleChars, int[] componentIndices, int outputDim)
        {
            string propertyName = "";
            for (int i = 0; i < outputDim; i++)
            {
                // Ensure index is valid for the available swizzle chars for this source dimension
                if (componentIndices[i] >= availableSwizzleChars.Length) continue; // Should not happen with Take(sourceDimension) logic but safe check
                propertyName += availableSwizzleChars[componentIndices[i]];
            }
            if (string.IsNullOrEmpty(propertyName)) return; // Skip if property name couldn't be formed

            // Skip the base case of a single component (e.g., .X, .Y, etc.)
            if (outputDim == 1)
            {
                // Skip if the property name is a single character (e.g., .X, .Y, etc.)
                if (propertyName.Equals("X", StringComparison.OrdinalIgnoreCase) ||
                    propertyName.Equals("Y", StringComparison.OrdinalIgnoreCase) ||
                    propertyName.Equals("Z", StringComparison.OrdinalIgnoreCase) ||
                    propertyName.Equals("W", StringComparison.OrdinalIgnoreCase))
                {
                    return; // Skip single character properties
                }
            }

            // Determine return type
            string returnTypeName;
            if (outputDim == 1)
            {
                returnTypeName = config.ComponentTypeName;
            }
            else
            {
                // Construct type name like "Float2", "Int3" etc.
                returnTypeName = $"{config.OutputStructPrefix}{outputDim}";
            }

            sb.AppendLine($"        /// <summary>Gets or sets the {propertyName.ToLowerInvariant()} swizzle.</summary>");
            sb.AppendLine($"        public {returnTypeName} {propertyName}");
            sb.AppendLine("        {");

            // --- Getter ---
            sb.Append($"            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ");
            if (outputDim == 1)
            {
                // Access like this.X, this.Y etc.
                sb.Append($"this.{config.SourceFieldNames[componentIndices[0]]};");
            }
            else
            {
                // Construct like new Float2(this.X, this.Y)
                sb.Append($"new {returnTypeName}(");
                for (int i = 0; i < outputDim; i++)
                {
                    sb.Append($"this.{config.SourceFieldNames[componentIndices[i]]}");
                    if (i < outputDim - 1) sb.Append(", ");
                }
                sb.Append(");");
            }
            sb.AppendLine();

            // --- Setter ---
            // Generate setter if 1 component OR >1 component AND no duplicates
            bool canSet = (outputDim == 1) || NoDuplicatedIndices(componentIndices);
            if (canSet)
            {
                sb.AppendLine("            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                sb.Append("            set { ");
                if (outputDim == 1)
                {
                    // Assignment like this.X = value;
                    sb.Append($"this.{config.SourceFieldNames[componentIndices[0]]} = value;");
                }
                else
                {
                    // Assignment like this.X = value.X; this.Y = value.Y;
                    // Assumes the 'value' (e.g., Float2) has X, Y, Z, W standard component names for access
                    string[] valueAccessors = { "X", "Y", "Z", "W" };
                    for (int i = 0; i < outputDim; i++)
                    {
                        // Ensure we don't try to access beyond value's dimension (e.g., value.Z for Float2)
                        if (i >= valueAccessors.Length) break;
                        sb.Append($"this.{config.SourceFieldNames[componentIndices[i]]} = value.{valueAccessors[i]}; ");
                        if (i < outputDim - 1) sb.Append(" ");
                    }
                }
                sb.Append(" }");
                sb.AppendLine();
            }

            sb.AppendLine("        }"); // end property
            sb.AppendLine();
        }

        // Helper to check for duplicate indices in a permutation (for setter generation)
        private static bool NoDuplicatedIndices(int[] indices)
        {
            var distinctCount = indices.Distinct().Count();
            return distinctCount == indices.Length;
        }

        #endregion

        #region Math Class

        private static Dictionary<string, MathFunctionGenerator> DiscoverMathFunctions()
        {
            var generators = new Dictionary<string, MathFunctionGenerator>();

            var assembly = Assembly.GetExecutingAssembly();
            var generatorTypes = assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(MathFunctionGenerator)) &&
                           !t.IsAbstract &&
                           t.GetCustomAttribute<MathFunctionAttribute>() != null);

            foreach (var type in generatorTypes)
            {
                var attribute = type.GetCustomAttribute<MathFunctionAttribute>();
                var instance = (MathFunctionGenerator)Activator.CreateInstance(type);
                generators[attribute.FunctionName] = instance;
            }

            return generators;
        }

        private static string GenerateMathSource(Dictionary<string, MathFunctionGenerator> generators)
        {
            var source = new StringBuilder();

            // Add auto-generated header comment
            AddHeader(source);

            source.AppendLine("using System;");
            source.AppendLine("using System.Runtime.CompilerServices;");
            source.AppendLine();
            source.AppendLine("namespace Prowl.Vector");
            source.AppendLine("{");
            source.AppendLine("    /// <summary>");
            source.AppendLine("    /// A static class containing mathematical functions for vectors and scalars.");
            source.AppendLine("    /// </summary>");
            source.AppendLine("    public static partial class Maths");
            source.AppendLine("    {");
            source.AppendLine();

            // Generate functions for each discovered generator
            foreach (var generator in generators.OrderBy(kvp => kvp.Key))
            {
                source.AppendLine($"        // {generator.Key} functions");

                var functionGenerator = generator.Value;

                // Generate scalar version if supported
                if (functionGenerator.SupportsScalars)
                {
                    foreach (var type in functionGenerator.SupportedTypes)
                    {
                        if (functionGenerator.SupportsType(type, 1))
                        {
                            source.AppendLine(functionGenerator.GenerateFunction(type, 1, null));
                        }
                    }
                }

                // Generate vector versions
                foreach (var type in functionGenerator.SupportedTypes)
                {
                    foreach (var dimension in functionGenerator.SupportedDimensions)
                    {
                        if (functionGenerator.SupportsType(type, dimension))
                        {
                            var components = GetComponents(dimension);
                            source.AppendLine(functionGenerator.GenerateFunction(type, dimension, components));
                        }
                    }
                }
            }

            source.AppendLine("    }");
            source.AppendLine("}");

            return source.ToString();
        }

        #endregion

        #region Matrix Generation Methods

        private static string GenerateMatrixStruct(MatrixConfig config)
        {
            var sb = new StringBuilder();
            var structName = config.StructName;
            var componentType = config.ComponentTypeName;
            var rows = config.Rows;
            var cols = config.Columns;
            var columnVectorType = config.GetColumnVectorType();

            // Add auto-generated header comment
            AddHeader(sb);

            sb.AppendLine("using System;");
            sb.AppendLine("using System.Globalization;");
            sb.AppendLine("using System.Runtime.CompilerServices;");
            sb.AppendLine("using System.Text;");
            sb.AppendLine("using Prowl.Vector;");
            sb.AppendLine();
            sb.AppendLine($"namespace Prowl.Vector");
            sb.AppendLine("{");
            sb.AppendLine($"    /// <summary>A {rows}x{cols} matrix of {componentType}s.</summary>");
            sb.AppendLine($"    [System.Serializable]");
            sb.AppendLine($"    public partial struct {structName} : System.IEquatable<{structName}>, IFormattable");
            sb.AppendLine("    {");

            // --- Fields (Column Vectors) ---
            for (int c = 0; c < cols; c++)
            {
                sb.AppendLine($"        /// <summary>Column {c} of the matrix.</summary>");
                sb.AppendLine($"        public {columnVectorType} c{c};");
            }
            sb.AppendLine();

            // --- Static Properties (Identity, Zero) ---
            GenerateMatrixStaticFields(sb, config);

            // --- Constructors ---
            GenerateMatrixConstructors(sb, config);

            // --- Component-wise Operators ---
            GenerateMatrixComponentWiseOperators(sb, config);

            // --- Multiplication Operators ---
            GenerateMatrixMultiplication(sb, config);

            // --- Indexer ---
            GenerateMatrixIndexer(sb, config);

            // --- Standard Methods (Equals, GetHashCode, ToString) ---
            GenerateMatrixStandardMethods(sb, config);

            sb.AppendLine("    }"); // end struct
            sb.AppendLine("}"); // end namespace

            return sb.ToString();
        }

        private static void GenerateMatrixStaticFields(StringBuilder sb, MatrixConfig config)
        {
            var structName = config.StructName;
            var componentType = config.ComponentTypeName;
            var rows = config.Rows;
            var cols = config.Columns;
            var columnVectorType = config.GetColumnVectorType();

            // Identity matrix only makes sense for square matrices
            if (rows == cols)
            {
                sb.AppendLine($"        /// <summary>{structName} identity transform.</summary>");
                var identityParams = new List<string>();
                for (int c = 0; c < cols; c++)
                {
                    var vectorComponents = new string[rows];
                    for (int r = 0; r < rows; r++)
                    {
                        vectorComponents[r] = (r == c) ? GetOneValue(componentType) : GetZeroValue(componentType);
                    }
                    identityParams.Add($"new {columnVectorType}({string.Join(", ", vectorComponents)})");
                }
                sb.AppendLine($"        public static readonly {structName} Identity = new {structName}({string.Join(", ", identityParams)});");
                sb.AppendLine();
            }

            // Zero matrix works for all matrix dimensions
            sb.AppendLine($"        /// <summary>{structName} zero value.</summary>");
            var zeroParams = new List<string>();
            for (int c = 0; c < cols; c++)
            {
                zeroParams.Add($"{columnVectorType}.Zero");
            }
            // For bool matrices, Zero means all false
            if (componentType == "bool")
            {
                zeroParams.Clear();
                for (int c = 0; c < cols; c++)
                {
                    zeroParams.Add($"new {columnVectorType}(false)");
                }
            }
            sb.AppendLine($"        public static readonly {structName} Zero = new {structName}({string.Join(", ", zeroParams)});");
            sb.AppendLine();
        }

        private static void GenerateMatrixConstructors(StringBuilder sb, MatrixConfig config)
        {
            var structName = config.StructName;
            var componentType = config.ComponentTypeName;
            var rows = config.Rows;
            var cols = config.Columns;
            var columnVectorType = config.GetColumnVectorType();

            // Constructor from column vectors
            sb.AppendLine($"        /// <summary>Constructs a {structName} matrix from {cols} {columnVectorType} vectors.</summary>");
            var colParams = new List<string>();
            for (int c = 0; c < cols; c++) colParams.Add($"{columnVectorType} col{c}");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public {structName}({string.Join(", ", colParams)})");
            sb.AppendLine("        {");
            for (int c = 0; c < cols; c++) sb.AppendLine($"            this.c{c} = col{c};");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Constructor from all scalar components (row-major input, stored as column vectors)
            sb.AppendLine($"        /// <summary>Constructs a {structName} matrix from {rows * cols} {componentType} values given in row-major order.</summary>");
            var scalarParams = new List<string>();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    scalarParams.Add($"{componentType} m{r}{c}");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public {structName}({string.Join(", ", scalarParams)})");
            sb.AppendLine("        {");
            for (int c = 0; c < cols; c++) // Iterate through columns to construct column vectors
            {
                var colVecComponents = new List<string>();
                for (int r = 0; r < rows; r++) // Iterate through rows for this column
                {
                    colVecComponents.Add($"m{r}{c}");
                }
                sb.AppendLine($"            this.c{c} = new {columnVectorType}({string.Join(", ", colVecComponents)});");
            }
            sb.AppendLine("        }");
            sb.AppendLine();

            // Constructor from a single scalar (assigns to all components of all column vectors)
            if (componentType != "bool") // Bools handle this differently, often via select
            {
                sb.AppendLine($"        /// <summary>Constructs a {structName} matrix from a single {componentType} value by assigning it to every component.</summary>");
                sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                sb.AppendLine($"        public {structName}({componentType} v)");
                sb.AppendLine("        {");
                for (int c = 0; c < cols; c++)
                {
                    // Assumes vector types have a constructor that takes a single scalar
                    // and broadcasts it to all its components.
                    sb.AppendLine($"            this.c{c} = new {columnVectorType}(v);");
                }
                sb.AppendLine("        }");
                sb.AppendLine();
            }
        }

        private static void GenerateMatrixComponentWiseOperators(StringBuilder sb, MatrixConfig config)
        {
            var structName = config.StructName;
            var componentType = config.ComponentTypeName;
            var cols = config.Columns;
            var columnVectorType = config.GetColumnVectorType();

            // Arithmetic operators (for non-bool types)
            if (componentType != "bool")
            {
                string[] ops = { "+", "-", "/", "%" };
                foreach (var op in ops)
                {
                    // Matrix op Matrix
                    sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    var opParamsM = new List<string>();
                    for (int c = 0; c < cols; c++) opParamsM.Add($"lhs.c{c} {op} rhs.c{c}");
                    sb.AppendLine($"        public static {structName} operator {op}({structName} lhs, {structName} rhs) {{ return new {structName}({string.Join(", ", opParamsM)}); }}");

                    // Matrix op Scalar
                    sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    var opParamsS1 = new List<string>();
                    for (int c = 0; c < cols; c++) opParamsS1.Add($"lhs.c{c} {op} rhs");
                    sb.AppendLine($"        public static {structName} operator {op}({structName} lhs, {componentType} rhs) {{ return new {structName}({string.Join(", ", opParamsS1)}); }}");

                    // Scalar op Matrix
                    sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    var opParamsS2 = new List<string>();
                    for (int c = 0; c < cols; c++) opParamsS2.Add($"lhs {op} rhs.c{c}");
                    sb.AppendLine($"        public static {structName} operator {op}({componentType} lhs, {structName} rhs) {{ return new {structName}({string.Join(", ", opParamsS2)}); }}");
                    sb.AppendLine();
                }

                // Negation
                if (IsSignedType(componentType))
                {
                    sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    var opParamsNeg = new List<string>();
                    for (int c = 0; c < cols; c++) opParamsNeg.Add($"-val.c{c}");
                    sb.AppendLine($"        public static {structName} operator -({structName} val) {{ return new {structName}({string.Join(", ", opParamsNeg)}); }}");
                }


                // Comparison operators (return BoolNxM matrix)
                string[] compOps = { "<", "<=", ">", ">=", "==", "!=" };
                string boolResultMatrixType = config.GetFullBoolMatrixType(); // e.g., Bool4x4
                string boolColumnVectorType = config.GetFullBoolColumnVectorType(); // e.g., Bool4

                foreach (var op in compOps)
                {
                    // Matrix op Matrix
                    sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    var opParamsM = new List<string>();
                    for (int c = 0; c < cols; c++) opParamsM.Add($"lhs.c{c} {op} rhs.c{c}"); // Assumes vector comparison returns bool vector
                    sb.AppendLine($"        public static {boolResultMatrixType} operator {op}({structName} lhs, {structName} rhs) {{ return new {boolResultMatrixType}({string.Join(", ", opParamsM)}); }}");

                    if (componentType != "bool") // Scalar comparisons don't make sense for bool matrix vs bool scalar in this way
                    {
                        // Matrix op Scalar
                        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                        var opParamsS1 = new List<string>();
                        for (int c = 0; c < cols; c++) opParamsS1.Add($"lhs.c{c} {op} rhs");
                        sb.AppendLine($"        public static {boolResultMatrixType} operator {op}({structName} lhs, {componentType} rhs) {{ return new {boolResultMatrixType}({string.Join(", ", opParamsS1)}); }}");

                        // Scalar op Matrix
                        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                        var opParamsS2 = new List<string>();
                        for (int c = 0; c < cols; c++) opParamsS2.Add($"lhs {op} rhs.c{c}");
                        sb.AppendLine($"        public static {boolResultMatrixType} operator {op}({componentType} lhs, {structName} rhs) {{ return new {boolResultMatrixType}({string.Join(", ", opParamsS2)}); }}");
                    }
                    sb.AppendLine();
                }
            }
        }

        private static void GenerateMatrixMultiplication(StringBuilder sb, MatrixConfig config)
        {
            var structName = config.StructName;
            var componentType = config.ComponentTypeName;
            var rows = config.Rows;
            var cols = config.Columns;

            // Only generate multiplication for numeric types
            if (componentType == "bool") return;

            // Matrix * Matrix multiplication (for square matrices)
            if (rows == cols)
            {
                sb.AppendLine($"        /// <summary>Returns the product of two {structName} matrices.</summary>");
                sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                sb.AppendLine($"        public static {structName} operator *({structName} lhs, {structName} rhs)");
                sb.AppendLine("        {");
                sb.AppendLine("            return new " + structName + "(");

                for (int col = 0; col < cols; col++)
                {
                    var components = new List<string>();
                    for (int row = 0; row < rows; row++)
                    {
                        var dotProduct = new List<string>();
                        for (int k = 0; k < rows; k++)
                        {
                            dotProduct.Add($"lhs.c{k}.{GetComponents(rows)[row]} * rhs.c{col}.{GetComponents(rows)[k]}");
                        }
                        components.Add($"({string.Join(" + ", dotProduct)})");
                    }
                    sb.AppendLine($"                new {config.GetColumnVectorType()}({string.Join(", ", components)}){(col < cols - 1 ? "," : "")}");
                }

                sb.AppendLine("            );");
                sb.AppendLine("        }");
                sb.AppendLine();
            }
        }

        private static void GenerateMatrixIndexer(StringBuilder sb, MatrixConfig config)
        {
            var structName = config.StructName;
            var componentType = config.ComponentTypeName;
            var cols = config.Columns;
            var columnVectorType = config.GetColumnVectorType();

            sb.AppendLine($"        /// <summary>Returns a reference to the {columnVectorType} (column) at a specified index.</summary>");
            sb.AppendLine($"        unsafe public ref {columnVectorType} this[int index]"); // unsafe keyword for fixed and pointers
            sb.AppendLine("        {");
            sb.AppendLine("            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("            get");
            sb.AppendLine("            {");
            sb.AppendLine($"                if ((uint)index >= {cols})"); // Bounds check
            sb.AppendLine($"                    throw new System.ArgumentOutOfRangeException(nameof(index), $\"Column index must be between 0 and {cols - 1}, but was {{index}}.\");");
            sb.AppendLine();
            // Get a pointer to the first column field (c0)
            // and then perform pointer arithmetic. This relies on fields c0, c1, ... being contiguous.
            sb.AppendLine($"                fixed ({columnVectorType}* pC0 = &this.c0)");
            sb.AppendLine("                {");
            sb.AppendLine($"                    return ref pC0[index];");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine($"        /// <summary>Returns the element at row and column indices.</summary>");
            sb.AppendLine($"        public {componentType} this[int row, int column]");
            sb.AppendLine("        {");
            sb.AppendLine("            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("            get");
            sb.AppendLine("            {");
            sb.AppendLine($"                if ((uint)column >= {cols})");
            sb.AppendLine($"                    throw new System.ArgumentOutOfRangeException(nameof(column));");
            sb.AppendLine("                return this[column][row];");
            sb.AppendLine("            }");
            sb.AppendLine("            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("            set");
            sb.AppendLine("            {");
            sb.AppendLine($"                if ((uint)column >= {cols})");
            sb.AppendLine($"                    throw new System.ArgumentOutOfRangeException(nameof(column));");
            sb.AppendLine("                var temp = this[column];");
            sb.AppendLine("                temp[row] = value;");
            sb.AppendLine("                this[column] = temp;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
        }

        private static void GenerateMatrixStandardMethods(StringBuilder sb, MatrixConfig config)
        {
            var structName = config.StructName;
            var componentType = config.ComponentTypeName;
            var rows = config.Rows;
            var cols = config.Columns;

            // ToArray
            sb.AppendLine($"        /// <summary>Returns an array of components.</summary>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public {componentType}[] ToArray()");
            sb.AppendLine("        {");
            sb.AppendLine($"            {componentType}[] array = new {componentType}[{rows * cols}];");
            sb.AppendLine($"            for (int i = 0; i < {rows}; i++)");
            sb.AppendLine($"                for (int j = 0; j < {cols}; j++)");
            sb.AppendLine($"                    array[i * {cols} + j] = this[i, j];");
            sb.AppendLine($"            return array;");
            sb.AppendLine("        }");

            // Equals (strongly-typed)
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            var equalsClauses = new List<string>();
            for (int c = 0; c < cols; c++) equalsClauses.Add($"this.c{c}.Equals(rhs.c{c})");
            sb.AppendLine($"        public bool Equals({structName} rhs) {{ return {string.Join(" && ", equalsClauses)}; }}");
            sb.AppendLine();

            // Equals (object)
            sb.AppendLine($"        public override bool Equals(object o) {{ return o is {structName} converted && Equals(converted); }}");
            sb.AppendLine();

            // GetHashCode
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public override int GetHashCode()");
            sb.AppendLine("        {");
            sb.AppendLine("            unchecked // Overflow is fine, just wrap");
            sb.AppendLine("            {");
            sb.AppendLine("                int hash = 17;"); // Or GetType().GetHashCode();
            for (int c = 0; c < cols; c++)
            {
                sb.AppendLine($"                hash = hash * 23 + c{c}.GetHashCode();");
            }
            sb.AppendLine("                return hash;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();

            // ToString
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public override string ToString() {{ return ToString(null, CultureInfo.CurrentCulture); }}");
            sb.AppendLine();

            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public string ToString(string format) {{ return ToString(format, CultureInfo.CurrentCulture); }}");
            sb.AppendLine();

            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public string ToString(string format, IFormatProvider formatProvider)");
            sb.AppendLine("        {");
            sb.AppendLine("            StringBuilder sb = new StringBuilder();");
            sb.AppendLine($"            sb.Append(\"{structName}(\");");

            bool isBool = componentType == "bool";

            // Output in row-major order for readability
            var componentAccessors = GetComponents(rows);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    sb.AppendLine($"            sb.Append(this.c{c}.{componentAccessors[r]}.ToString({(isBool == false ? "format, " : "")}formatProvider));");
                    if (!(r == rows - 1 && c == cols - 1)) // Not the last element
                    {
                        sb.AppendLine("            sb.Append(\", \");");
                    }
                }
                if (r < rows - 1) // Add visual break between rows (except for the last row)
                {
                    sb.AppendLine("            sb.Append(\"  \");");
                }
            }
            sb.AppendLine("            sb.Append(\")\");");
            sb.AppendLine("            return sb.ToString();");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        #endregion

        private static string[] GetComponents(int dimension)
        {
            if (dimension == 2) return new[] { "X", "Y" };
            if (dimension == 3) return new[] { "X", "Y", "Z" };
            if (dimension == 4) return new[] { "X", "Y", "Z", "W" };
            throw new ArgumentException($"Unsupported dimension: {dimension}");
        }
    }
}
