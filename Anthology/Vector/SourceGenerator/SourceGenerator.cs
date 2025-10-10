using System.Reflection;
using System.Text;

namespace SourceGenerator;

class SourceGenerator
{
    public static string[] numericTypes = ["float", "double", "int", "byte", "ushort", "uint", "ulong"];

    private static readonly HashSet<(string from, string to)> s_wideningConversions = new()
    {
        // Based on C# implicit numeric conversions
        ("byte", "ushort"), ("byte", "short"), ("byte", "int"), ("byte", "uint"), ("byte", "long"), ("byte", "ulong"), ("byte", "float"), ("byte", "double"),
        ("short", "int"), ("short", "long"), ("short", "float"), ("short", "double"),
        ("ushort", "int"), ("ushort", "uint"), ("ushort", "long"), ("ushort", "ulong"), ("ushort", "float"), ("ushort", "double"),
        ("int", "long"), ("int", "float"), ("int", "double"),
        ("uint", "long"), ("uint", "ulong"), ("uint", "float"), ("uint", "double"),
        ("long", "float"), ("long", "double"),
        ("ulong", "float"), ("ulong", "double"),
        ("float", "double"),
    };

    const string Inline = "[MethodImpl(MethodImplOptions.AggressiveInlining)]";

    internal class MatrixConfig
    {
        public string StructName => $"{Prefix}{Rows}x{Columns}";

        public string PrimitiveType { get; } // e.g., "float"
        public int Rows { get; }
        public int Columns { get; }
        public string Prefix { get; } // e.g., "Float" for Float2, Float3, Float4 columns
        public string BoolVectorTypeNamePrefix { get; } // e.g., "Bool" for Bool2, Bool3, Bool4 results of comparisons

        public MatrixConfig(string componentTypeName, int rows, int columns, string vectorTypeNamePrefix, string boolVectorTypeNamePrefix = "Bool")
        {
            PrimitiveType = componentTypeName;
            Rows = rows;
            Columns = columns;
            Prefix = vectorTypeNamePrefix;
            BoolVectorTypeNamePrefix = boolVectorTypeNamePrefix;
        }

        public string GetColumnVectorType() => $"{Prefix}{Rows}"; // e.g., Float4
        public string GetFullBoolMatrixType() => $"{BoolVectorTypeNamePrefix}{Rows}x{Columns}"; // e.g. Bool4x4
        public string GetFullBoolColumnVectorType() => $"{BoolVectorTypeNamePrefix}{Rows}"; // e.g. Bool4
        public bool IsTranslationMatrix()
        {
            return ((Rows == 3 && Columns == 3) || (Rows == 4 && Columns == 4)) &&
                (PrimitiveType == "float" || PrimitiveType == "double");
        }
    }

    internal class VectorConfig
    {
        public string StructName => $"{Prefix}{Dimensions}";
        public int Dimensions => Components.Length;

        public string PrimitiveType { get; }
        public string[] Components { get; }
        public string Prefix { get; }

        public VectorConfig(string componentTypeName, string[] sourceFieldNames, string outputStructPrefix)
        {
            PrimitiveType = componentTypeName;
            Components = sourceFieldNames;
            Prefix = outputStructPrefix;
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

    internal class TemplateConfig
    {
        public string TemplateName { get; }
        public string[] SupportedTypes { get; }
        public string Namespace { get; }
        public string OutputFolder { get; }

        public TemplateConfig(string templateName, string[] supportedTypes, string namespaceName, string outputFolder)
        {
            TemplateName = templateName;
            SupportedTypes = supportedTypes;
            Namespace = namespaceName;
            OutputFolder = outputFolder;
        }
    }

    internal class TemplateTypeMapping
    {
        public string TypeName { get; }
        public string TypePrefix { get; }
        public string TemplateSuffix { get; }
        public string ZeroValue { get; }
        public string OneValue { get; }
        public string TwoValue { get; }
        public string EpsilonValue { get; }

        public TemplateTypeMapping(string typeName, string typePrefix, string templateSuffix, string zeroValue, string oneValue, string twoValue, string epsilonValue)
        {
            TypeName = typeName;
            TypePrefix = typePrefix;
            TemplateSuffix = templateSuffix;
            ZeroValue = zeroValue;
            OneValue = oneValue;
            TwoValue = twoValue;
            EpsilonValue = epsilonValue;
        }
    }

    // Swizzle configuration
    private static readonly VectorConfig[] s_vectorConfigs =
    [
        new VectorConfig("float", ["X", "Y"], "Float"),
        new VectorConfig("float", ["X", "Y", "Z"], "Float"),
        new VectorConfig("float", ["X", "Y", "Z", "W"], "Float"),

        new VectorConfig("double", ["X", "Y"], "Double"),
        new VectorConfig("double", ["X", "Y", "Z"], "Double"),
        new VectorConfig("double", ["X", "Y", "Z", "W"], "Double"),

        new VectorConfig("int", ["X", "Y"], "Int"),
        new VectorConfig("int", ["X", "Y", "Z"], "Int"),
        new VectorConfig("int", ["X", "Y", "Z", "W"], "Int"),

        new VectorConfig("byte", ["X", "Y"], "Byte"),
        new VectorConfig("byte", ["X", "Y", "Z"], "Byte"),
        new VectorConfig("byte", ["X", "Y", "Z", "W"], "Byte"),

        new VectorConfig("ushort", ["X", "Y"], "UShort"),
        new VectorConfig("ushort", ["X", "Y", "Z"], "UShort"),
        new VectorConfig("ushort", ["X", "Y", "Z", "W"], "UShort"),

        new VectorConfig("uint", ["X", "Y"], "UInt"),
        new VectorConfig("uint", ["X", "Y", "Z"], "UInt"),
        new VectorConfig("uint", ["X", "Y", "Z", "W"], "UInt"),

        new VectorConfig("ulong", ["X", "Y"], "ULong"),
        new VectorConfig("ulong", ["X", "Y", "Z"], "ULong"),
        new VectorConfig("ulong", ["X", "Y", "Z", "W"], "ULong"),

        new VectorConfig("bool", ["X", "Y"], "Bool"),
        new VectorConfig("bool", ["X", "Y", "Z"], "Bool"),
        new VectorConfig("bool", ["X", "Y", "Z", "W"], "Bool"),
    ];

    // Could probably not be a static array... should probably do that cause this is a whole lot of duplicate code
    private static readonly MatrixConfig[] s_matrixConfigs =
    [
        // Square Matrices - Float
        new MatrixConfig("float", 2, 2, "Float"),
        new MatrixConfig("float", 3, 3, "Float"),
        new MatrixConfig("float", 4, 4, "Float"),

        // Non-Square Matrices - Float
        new MatrixConfig("float", 2, 3, "Float"),
        new MatrixConfig("float", 2, 4, "Float"),
        new MatrixConfig("float", 3, 2, "Float"),
        new MatrixConfig("float", 3, 4, "Float"),
        new MatrixConfig("float", 4, 2, "Float"),
        new MatrixConfig("float", 4, 3, "Float"),

        // Square Matrices - Double
        new MatrixConfig("double", 2, 2, "Double"),
        new MatrixConfig("double", 3, 3, "Double"),
        new MatrixConfig("double", 4, 4, "Double"),

        // Non-Square Matrices - Double
        new MatrixConfig("double", 2, 3, "Double"),
        new MatrixConfig("double", 2, 4, "Double"),
        new MatrixConfig("double", 3, 2, "Double"),
        new MatrixConfig("double", 3, 4, "Double"),
        new MatrixConfig("double", 4, 2, "Double"),
        new MatrixConfig("double", 4, 3, "Double"),

        // Square Matrices - Int
        new MatrixConfig("int", 2, 2, "Int"),
        new MatrixConfig("int", 3, 3, "Int"),
        new MatrixConfig("int", 4, 4, "Int"),

        // Non-Square Matrices - Int
        new MatrixConfig("int", 2, 3, "Int"),
        new MatrixConfig("int", 2, 4, "Int"),
        new MatrixConfig("int", 3, 2, "Int"),
        new MatrixConfig("int", 3, 4, "Int"),
        new MatrixConfig("int", 4, 2, "Int"),
        new MatrixConfig("int", 4, 3, "Int"),

        // Square Matrices - UInt
        new MatrixConfig("uint", 2, 2, "UInt"),
        new MatrixConfig("uint", 3, 3, "UInt"),
        new MatrixConfig("uint", 4, 4, "UInt"),

        // Non-Square Matrices - UInt
        new MatrixConfig("uint", 2, 3, "UInt"),
        new MatrixConfig("uint", 2, 4, "UInt"),
        new MatrixConfig("uint", 3, 2, "UInt"),
        new MatrixConfig("uint", 3, 4, "UInt"),
        new MatrixConfig("uint", 4, 2, "UInt"),
        new MatrixConfig("uint", 4, 3, "UInt"),

        // Square Bool Matrices (for results of comparisons)
        new MatrixConfig("bool", 2, 2, "Bool"),
        new MatrixConfig("bool", 3, 3, "Bool"),
        new MatrixConfig("bool", 4, 4, "Bool"),

        // Non-Square Bool Matrices (for results of comparisons)
        new MatrixConfig("bool", 2, 3, "Bool"),
        new MatrixConfig("bool", 2, 4, "Bool"),
        new MatrixConfig("bool", 3, 2, "Bool"),
        new MatrixConfig("bool", 3, 4, "Bool"),
        new MatrixConfig("bool", 4, 2, "Bool"),
        new MatrixConfig("bool", 4, 3, "Bool"),
    ];

    private static readonly SwizzleCharSetDefinition[] s_swizzleCharSets =
    [
        new SwizzleCharSetDefinition(['X', 'Y', 'Z', 'W']), // Primary accessors
        new SwizzleCharSetDefinition(['R', 'G', 'B', 'A']), // Color accessors
    ];

    private static readonly Dictionary<string, TemplateTypeMapping> s_typeMappings = new()
    {
        ["float"] = new TemplateTypeMapping("float", "Float", "", "0f", "1f", "2f", "float.Epsilon"),
        ["double"] = new TemplateTypeMapping("double", "Double", "D", "0.0", "1.0", "2.0", "double.Epsilon"),
        ["int"] = new TemplateTypeMapping("int", "Int", "Int", "0", "1", "2", "1"),
        ["byte"] = new TemplateTypeMapping("byte", "Byte", "Byte", "(byte)0", "(byte)1", "(byte)2", "(byte)1"),
        ["uint"] = new TemplateTypeMapping("uint", "UInt", "UInt", "0u", "1u", "2u", "1u"),
        ["ulong"] = new TemplateTypeMapping("ulong", "ULong", "ULong", "0ul", "1ul", "2ul", "1ul")
    };

    private const int MAX_OUTPUT_SWIZZLE_DIMENSION = 4;

    static void Main(string[] args)
    {
        string outputDirectory = args.Length > 0 ? args[0] : "Generated";
        bool generateSwizzles = args.Length > 1 ? bool.Parse(args[1]) : true;
        bool generateTests = args.Length > 2 ? bool.Parse(args[2]) : true;

        // Create output directory if it doesn't exist
        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        Console.WriteLine($"Generating vector types to directory: {outputDirectory}");

        // Generate basic vector structs
        foreach (var config in s_vectorConfigs)
        {
            var source = GenerateVectorStruct(config);
            var fileName = $"{config.StructName}.cs";
            var filePath = Path.Combine(outputDirectory, "Vectors", fileName);
            Directory.CreateDirectory(new FileInfo(filePath).Directory.FullName);
            File.WriteAllText(filePath, source, Encoding.UTF8);
            Console.WriteLine($"Generated: {fileName}");
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
                var filePath = Path.Combine(outputDirectory, "Matrices", fileName);
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

        // Generate templates
        GenerateTemplates(outputDirectory);

        // Generate tests if enabled
        if (generateTests)
        {
            Console.WriteLine("\nGenerating math function tests...");
            GenerateMathTests(outputDirectory);
        }

        int totalFiles = s_vectorConfigs.Length + (generateSwizzles ? s_vectorConfigs.Length : 0);
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

    private static string GenerateVectorStruct(VectorConfig config)
    {
        var componentDeclarations = string.Join(", ", config.Components);

        var isFloatingPoint = IsFloatingPoint(config.PrimitiveType);
        var isSignedType = IsSignedType(config.PrimitiveType);

        var mathClass = config.PrimitiveType == "float" ? "MathF" : "Math";

        var source = new StringBuilder();

        // Add auto-generated header comment
        AddHeader(source);

        source.AppendLine("using System;");
        source.AppendLine("using System.Globalization;");
        source.AppendLine("using System.Runtime.CompilerServices;");
        source.AppendLine("using System.Collections.Generic;");
        source.AppendLine("using System.Linq;");

        if (IsSystemNumericsCompatible(config.PrimitiveType, config.Dimensions))
            source.AppendLine("using System.Numerics;");

        source.AppendLine();
        source.AppendLine("namespace Prowl.Vector");
        source.AppendLine("{");
        source.AppendLine();
        source.AppendLine($"/// <summary>");
        source.AppendLine($"/// Represents a {config.Dimensions}-component vector using {config.PrimitiveType} precision.");
        source.AppendLine($"/// </summary>");
        source.AppendLine($"[System.Serializable]");
        source.AppendLine($"public partial struct {config.StructName} : IEquatable<{config.StructName}>, IFormattable");
        source.AppendLine($"{{");

        // Fields
        source.AppendLine($"\tpublic {config.PrimitiveType} {componentDeclarations};");
        source.AppendLine();

        // Constructors
        GenerateConstructors(source, config);

        // Static Properties
        GenerateStaticProperties(source, config);

        // Properties (only for floating-point types)
        if (isFloatingPoint)
        {
            GenerateProperties(source, config, mathClass);
        }

        // Indexer
        GenerateIndexer(source, config);

        // Operators
        GenerateOperators(source, config, isSignedType);

        if (IsNumericType(config.PrimitiveType))
            GenerateConversions(source, config);

        // Methods
        GenerateMethods(source, config, isFloatingPoint, mathClass);

        source.AppendLine("}");
        source.AppendLine("}");

        return source.ToString();
    }


    private static void GenerateConstructors(StringBuilder source, VectorConfig config)
    {
        source.AppendLine($"\t// --- Constructors ---");

        // Scalar constructor
        source.AppendLine($"\t/// <summary>Initializes all components to a single scalar value.</summary>");
        source.AppendLine($"\t{Inline}");
        var componentList = string.Join(", ", config.Components.Select(c => "scalar").ToArray());
        source.AppendLine($"\tpublic {config.StructName}({config.PrimitiveType} scalar) : this({componentList}) {{ }}");
        source.AppendLine();

        // Individual components constructor
        source.AppendLine($"\t/// <summary>Initializes with specified component values.</summary>");
        source.AppendLine($"\t{Inline}");
        var parameters = string.Join(", ", config.Components.Select((c, i) => $"{config.PrimitiveType} {c.ToLower()}").ToArray());
        source.AppendLine($"\tpublic {config.StructName}({parameters})");
        source.AppendLine($"\t{{");

        foreach (var component in config.Components)
        {
            source.AppendLine($"\t\t{component} = {component.ToLower()};");
        }

        source.AppendLine($"\t}}");
        source.AppendLine();

        // Copy constructor
        source.AppendLine($"\t/// <summary>Initializes by copying components from another {config.StructName}.</summary>");
        source.AppendLine($"\t{Inline}");
        var copyParameters = string.Join(", ", config.Components.Select(c => $"v.{c}").ToArray());
        source.AppendLine($"\tpublic {config.StructName}({config.StructName} v) : this({copyParameters}) {{ }}");
        source.AppendLine();

        // Array constructor
        source.AppendLine($"\t/// <summary>Initializes from an array.</summary>");
        source.AppendLine($"\t{Inline}");
        source.AppendLine($"\tpublic {config.StructName}({config.PrimitiveType}[] array)");
        source.AppendLine($"\t{{");
        source.AppendLine($"\t\tif (array == null) throw new ArgumentNullException(nameof(array));");
        source.AppendLine($"\t\tif (array.Length < {config.Dimensions}) throw new ArgumentException(\"Array must contain at least {config.Dimensions} elements.\", nameof(array));");
        for (int i = 0; i < config.Dimensions; i++)
        {
            source.AppendLine($"\t\t{config.Components[i]} = array[{i}];");
        }
        source.AppendLine($"\t}}");
        source.AppendLine();

        if (config.Dimensions == 3)
        {
            // Float3(Float2, float) - XY + Z
            source.AppendLine($"\t/// <summary>Initializes from a {config.Prefix}2 and Z component.</summary>");
            source.AppendLine($"\t{Inline}");
            source.AppendLine($"\tpublic {config.StructName}({config.Prefix}2 xy, {config.PrimitiveType} z) : this(xy.X, xy.Y, z) {{ }}");
            source.AppendLine();

            // Float3(float, Float2) - X + YZ
            source.AppendLine($"\t/// <summary>Initializes from X component and a {config.Prefix}2.</summary>");
            source.AppendLine($"\t{Inline}");
            source.AppendLine($"\tpublic {config.StructName}({config.PrimitiveType} x, {config.Prefix}2 yz) : this(x, yz.X, yz.Y) {{ }}");
            source.AppendLine();
        }
        else if (config.Dimensions == 4)
        {
            // Float4(Float2, float, float) - XY + Z + W
            source.AppendLine($"\t/// <summary>Initializes from a {config.Prefix}2 and Z, W components.</summary>");
            source.AppendLine($"\t{Inline}");
            source.AppendLine($"\tpublic {config.StructName}({config.Prefix}2 xy, {config.PrimitiveType} z, {config.PrimitiveType} w) : this(xy.X, xy.Y, z, w) {{ }}");
            source.AppendLine();

            // Float4(float, Float2, float) - X + YZ + W
            source.AppendLine($"\t/// <summary>Initializes from X component, a {config.Prefix}2, and W component.</summary>");
            source.AppendLine($"\t{Inline}");
            source.AppendLine($"\tpublic {config.StructName}({config.PrimitiveType} x, {config.Prefix}2 yz, {config.PrimitiveType} w) : this(x, yz.X, yz.Y, w) {{ }}");
            source.AppendLine();

            // Float4(float, float, Float2) - X + Y + ZW
            source.AppendLine($"\t/// <summary>Initializes from X, Y components and a {config.Prefix}2.</summary>");
            source.AppendLine($"\t{Inline}");
            source.AppendLine($"\tpublic {config.StructName}({config.PrimitiveType} x, {config.PrimitiveType} y, {config.Prefix}2 zw) : this(x, y, zw.X, zw.Y) {{ }}");
            source.AppendLine();

            // Float4(Float2, Float2) - XY + ZW
            source.AppendLine($"\t/// <summary>Initializes from two {config.Prefix}2 vectors.</summary>");
            source.AppendLine($"\t{Inline}");
            source.AppendLine($"\tpublic {config.StructName}({config.Prefix}2 xy, {config.Prefix}2 zw) : this(xy.X, xy.Y, zw.X, zw.Y) {{ }}");
            source.AppendLine();

            // Float4(Float3, float) - XYZ + W
            source.AppendLine($"\t/// <summary>Initializes from a {config.Prefix}3 and W component.</summary>");
            source.AppendLine($"\t{Inline}");
            source.AppendLine($"\tpublic {config.StructName}({config.Prefix}3 xyz, {config.PrimitiveType} w) : this(xyz.X, xyz.Y, xyz.Z, w) {{ }}");
            source.AppendLine();

            // Float4(float, Float3) - X + YZW
            source.AppendLine($"\t/// <summary>Initializes from X component and a {config.Prefix}3.</summary>");
            source.AppendLine($"\t{Inline}");
            source.AppendLine($"\tpublic {config.StructName}({config.PrimitiveType} x, {config.Prefix}3 yzw) : this(x, yzw.X, yzw.Y, yzw.Z) {{ }}");
            source.AppendLine();
        }

        // Type conversion constructors - only for numeric types
        if (IsNumericType(config.PrimitiveType))
        {
            GenerateConversionConstructors(source, config);
        }

        // Readonly collection constructors
        GenerateCollectionConstructors(source, config);
    }

    private static void GenerateConversionConstructors(StringBuilder source, VectorConfig config)
    {
        source.AppendLine($"\t// --- Type Conversion Constructors ---");

        var otherTypes = s_vectorConfigs.Where(t =>
            t.PrimitiveType != config.PrimitiveType &&
            IsNumericType(t.PrimitiveType) &&
            t.Dimensions == config.Dimensions).ToArray();

        foreach (var otherType in otherTypes)
        {
            source.AppendLine($"\t/// <summary>Initializes from a {otherType.StructName} with type conversion.</summary>");
            source.AppendLine($"\t{Inline}");
            var conversionParams = string.Join(", ", config.Components.Select(c => $"({config.PrimitiveType})v.{c}").ToArray());
            source.AppendLine($"\tpublic {config.StructName}({otherType.StructName} v) : this({conversionParams}) {{ }}");
            source.AppendLine();
        }
    }

    private static void GenerateCollectionConstructors(StringBuilder source, VectorConfig config)
    {
        source.AppendLine($"\t// --- Collection Constructors ---");

        // IEnumerable constructor
        source.AppendLine($"\t/// <summary>Initializes from an IEnumerable collection.</summary>");
        source.AppendLine($"\t{Inline}");
        source.AppendLine($"\tpublic {config.StructName}(IEnumerable<{config.PrimitiveType}> values)");
        source.AppendLine($"\t{{");
        source.AppendLine($"\t\tvar array = values.ToArray();");
        source.AppendLine($"\t\tif (array.Length < {config.Dimensions}) throw new ArgumentException(\"Collection must contain at least {config.Dimensions} elements.\", nameof(values));");

        for (int i = 0; i < config.Dimensions; i++)
            source.AppendLine($"\t\t{config.Components[i]} = array[{i}];");

        source.AppendLine($"\t}}");
        source.AppendLine();

        // ReadOnlySpan constructor
        source.AppendLine($"\t/// <summary>Initializes from a ReadOnlySpan.</summary>");
        source.AppendLine($"\t{Inline}");
        source.AppendLine($"\tpublic {config.StructName}(ReadOnlySpan<{config.PrimitiveType}> span)");
        source.AppendLine($"\t{{");
        source.AppendLine($"\t\tif (span.Length < {config.Dimensions}) throw new ArgumentException(\"Span must contain at least {config.Dimensions} elements.\", nameof(span));");

        for (int i = 0; i < config.Dimensions; i++)
            source.AppendLine($"\t\t{config.Components[i]} = span[{i}];");

        source.AppendLine($"\t}}");
        source.AppendLine();

        // Span constructor
        source.AppendLine($"\t/// <summary>Initializes from a Span.</summary>");
        source.AppendLine($"\t{Inline}");
        source.AppendLine($"\tpublic {config.StructName}(Span<{config.PrimitiveType}> span)");
        source.AppendLine($"\t{{");
        source.AppendLine($"\t\tif (span.Length < {config.Dimensions}) throw new ArgumentException(\"Span must contain at least {config.Dimensions} elements.\", nameof(span));");

        for (int i = 0; i < config.Dimensions; i++)
            source.AppendLine($"\t\t{config.Components[i]} = span[{i}];");

        source.AppendLine($"\t}}");
        source.AppendLine();
    }

    private static void GenerateStaticProperties(StringBuilder source, VectorConfig config)
    {
        source.AppendLine($"\t// --- Static Properties ---");

        // Zero vector
        var zeroComponents = string.Join(", ", config.Components.Select(c => GetZeroValue(config.PrimitiveType)).ToArray());
        source.AppendLine($"\t/// <summary>Gets the zero vector.</summary>");
        source.AppendLine($"\tpublic static {config.StructName} Zero {{ get {{ return new {config.StructName}({zeroComponents}); }} }}");

        // One vector
        var oneComponents = string.Join(", ", config.Components.Select(c => GetOneValue(config.PrimitiveType)).ToArray());
        source.AppendLine($"\t/// <summary>Gets the one vector.</summary>");
        source.AppendLine($"\tpublic static {config.StructName} One {{ get {{ return new {config.StructName}({oneComponents}); }} }}");

        // Unit vectors
        for (int i = 0; i < config.Dimensions; i++)
        {
            var unitComponents = new List<string>();
            for (int j = 0; j < config.Dimensions; j++)
            {
                unitComponents.Add(i == j ? GetOneValue(config.PrimitiveType) : GetZeroValue(config.PrimitiveType));
            }
            var componentList = string.Join(", ", unitComponents.ToArray());
            source.AppendLine($"\t/// <summary>Gets the unit vector along the {config.Components[i]}-axis.</summary>");
            source.AppendLine($"\tpublic static {config.StructName} Unit{config.Components[i]} {{ get {{ return new {config.StructName}({componentList}); }} }}");
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

    private static void GenerateProperties(StringBuilder source, VectorConfig config, string mathClass)
    {
        source.AppendLine($"\t// --- Properties ---");

        // Length
        source.AppendLine($"\t/// <summary>Gets the magnitude (length) of the vector.</summary>");
        source.AppendLine($"\tpublic {config.PrimitiveType} Length");
        source.AppendLine($"\t{{");
        source.AppendLine($"\t\t{Inline}");
        source.AppendLine($"\t\tget {{ return {mathClass}.Sqrt(LengthSquared); }}");
        source.AppendLine($"\t}}");
        source.AppendLine();

        // LengthSquared
        source.AppendLine($"\t/// <summary>Gets the squared magnitude (length) of the vector.</summary>");
        source.AppendLine($"\tpublic {config.PrimitiveType} LengthSquared");
        source.AppendLine($"\t{{");
        source.AppendLine($"\t\t{Inline}");
        var lengthSquaredExpression = string.Join(" + ", config.Components.Select(c => $"{c} * {c}").ToArray());
        source.AppendLine($"\t\tget {{ return {lengthSquaredExpression}; }}");
        source.AppendLine($"\t}}");
        source.AppendLine();

        // Normalized
        source.AppendLine($"\t/// <summary>Gets a normalized version of this vector.</summary>");
        source.AppendLine($"\tpublic {config.StructName} Normalized");
        source.AppendLine($"\t{{");
        source.AppendLine($"\t\t{Inline}");
        source.AppendLine($"\t\tget");
        source.AppendLine($"\t\t{{");
        source.AppendLine($"\t\t\t{config.PrimitiveType} lenSq = LengthSquared;");
        if (config.PrimitiveType == "float" || config.PrimitiveType == "double")
        {
            source.AppendLine($"\t\t\tif (lenSq <= {config.PrimitiveType}.Epsilon * {config.PrimitiveType}.Epsilon)");
        }
        else
        {
            source.AppendLine($"\t\t\tif (lenSq <= {config.PrimitiveType}.Epsilon)");
        }
        source.AppendLine($"\t\t\t{{");
        source.AppendLine($"\t\t\t\treturn Zero;");
        source.AppendLine($"\t\t\t}}");
        source.AppendLine($"\t\t\t{config.PrimitiveType} invLength = {GetOneValue(config.PrimitiveType)} / {mathClass}.Sqrt(lenSq);");
        var normalizedComponents = string.Join(", ", config.Components.Select(c => $"{c} * invLength").ToArray());
        source.AppendLine($"\t\t\treturn new {config.StructName}({normalizedComponents});");
        source.AppendLine($"\t\t}}");
        source.AppendLine($"\t}}");
        source.AppendLine();
    }

    private static void GenerateIndexer(StringBuilder source, VectorConfig config)
    {
        source.AppendLine($"\t// --- Indexer ---");
        source.AppendLine($"\t/// <summary>Gets or sets the component at the specified index.</summary>");
        source.AppendLine($"\tpublic {config.PrimitiveType} this[int index]");
        source.AppendLine($"\t{{");
        source.AppendLine($"\t\t{Inline}");
        source.AppendLine($"\t\tget");
        source.AppendLine($"\t\t{{");
        source.AppendLine($"\t\t\tswitch (index)");
        source.AppendLine($"\t\t\t{{");
        for (int i = 0; i < config.Dimensions; i++)
        {
            source.AppendLine($"\t\t\t\tcase {i}: return {config.Components[i]};");
        }
        source.AppendLine($"\t\t\t\tdefault: throw new IndexOutOfRangeException(string.Format(\"Index must be between 0 and {config.Dimensions - 1}, but was {{0}}\", index));");
        source.AppendLine($"\t\t\t}}");
        source.AppendLine($"\t\t}}");
        source.AppendLine($"\t\t{Inline}");
        source.AppendLine($"\t\tset");
        source.AppendLine($"\t\t{{");
        source.AppendLine($"\t\t\tswitch (index)");
        source.AppendLine($"\t\t\t{{");
        for (int i = 0; i < config.Dimensions; i++)
        {
            source.AppendLine($"\t\t\t\tcase {i}: {config.Components[i]} = value; break;");
        }
        source.AppendLine($"\t\t\t\tdefault: throw new IndexOutOfRangeException(string.Format(\"Index must be between 0 and {config.Dimensions - 1}, but was {{0}}\", index));");
        source.AppendLine($"\t\t\t}}");
        source.AppendLine($"\t\t}}");
        source.AppendLine($"\t}}");
        source.AppendLine();
    }

    private static void GenerateOperators(StringBuilder source, VectorConfig config, bool isSignedType)
    {
        source.AppendLine($"\t// --- Vector-to-Vector Operators ---");

        // For boolean vectors, we need different operators
        if (config.PrimitiveType == "bool")
        {
            // Logical AND
            source.AppendLine($"\t{Inline}");
            var andComponents = string.Join(", ", config.Components.Select(c => $"a.{c} && b.{c}").ToArray());
            source.AppendLine($"\tpublic static {config.StructName} operator &({config.StructName} a, {config.StructName} b) {{ return new {config.StructName}({andComponents}); }}");
            source.AppendLine();

            // Logical OR
            source.AppendLine($"\t{Inline}");
            var orComponents = string.Join(", ", config.Components.Select(c => $"a.{c} || b.{c}").ToArray());
            source.AppendLine($"\tpublic static {config.StructName} operator |({config.StructName} a, {config.StructName} b) {{ return new {config.StructName}({orComponents}); }}");
            source.AppendLine();

            // Logical XOR
            source.AppendLine($"\t{Inline}");
            var xorComponents = string.Join(", ", config.Components.Select(c => $"a.{c} ^ b.{c}").ToArray());
            source.AppendLine($"\tpublic static {config.StructName} operator ^({config.StructName} a, {config.StructName} b) {{ return new {config.StructName}({xorComponents}); }}");
            source.AppendLine();

            // Logical NOT
            source.AppendLine($"\t{Inline}");
            var notComponents = string.Join(", ", config.Components.Select(c => $"!v.{c}").ToArray());
            source.AppendLine($"\tpublic static {config.StructName} operator !({config.StructName} v) {{ return new {config.StructName}({notComponents}); }}");
            source.AppendLine();

            source.AppendLine($"\t// --- Equality Operators (Return Single Bool) ---");

            // Equality operator
            source.AppendLine($"\t/// <summary>Returns true if all components of both vectors are equal.</summary>");
            source.AppendLine($"\t{Inline}");
            var equalityComponents = string.Join(" && ", config.Components.Select(c => $"lhs.{c} == rhs.{c}").ToArray());
            source.AppendLine($"\tpublic static bool operator ==({config.StructName} lhs, {config.StructName} rhs) {{ return {equalityComponents}; }}");
            source.AppendLine();

            // Inequality operator  
            source.AppendLine($"\t/// <summary>Returns true if any component of the vectors are not equal.</summary>");
            source.AppendLine($"\t{Inline}");
            source.AppendLine($"\tpublic static bool operator !=({config.StructName} lhs, {config.StructName} rhs) {{ return !(lhs == rhs); }}");
            source.AppendLine();
        }
        else
        {
            // Vector-to-vector arithmetic operators

            // Addition
            source.AppendLine($"\t{Inline}");
            string addComponents;
            if (config.PrimitiveType == "byte" || config.PrimitiveType == "ushort")
            {
                // Need explicit casting for small integer types due to integer promotion
                addComponents = string.Join(", ", config.Components.Select(c => $"({config.PrimitiveType})(a.{c} + b.{c})").ToArray());
            }
            else
            {
                addComponents = string.Join(", ", config.Components.Select(c => $"a.{c} + b.{c}").ToArray());
            }
            source.AppendLine($"\tpublic static {config.StructName} operator +({config.StructName} a, {config.StructName} b) {{ return new {config.StructName}({addComponents}); }}");
            source.AppendLine();

            // Subtraction
            source.AppendLine($"\t{Inline}");
            string subComponents;
            if (config.PrimitiveType == "byte" || config.PrimitiveType == "ushort")
            {
                subComponents = string.Join(", ", config.Components.Select(c => $"({config.PrimitiveType})(a.{c} - b.{c})").ToArray());
            }
            else
            {
                subComponents = string.Join(", ", config.Components.Select(c => $"a.{c} - b.{c}").ToArray());
            }
            source.AppendLine($"\tpublic static {config.StructName} operator -({config.StructName} a, {config.StructName} b) {{ return new {config.StructName}({subComponents}); }}");
            source.AppendLine();

            // Multiplication (component-wise)
            source.AppendLine($"\t{Inline}");
            string mulComponents;
            if (config.PrimitiveType == "byte" || config.PrimitiveType == "ushort")
            {
                mulComponents = string.Join(", ", config.Components.Select(c => $"({config.PrimitiveType})(a.{c} * b.{c})").ToArray());
            }
            else
            {
                mulComponents = string.Join(", ", config.Components.Select(c => $"a.{c} * b.{c}").ToArray());
            }
            source.AppendLine($"\tpublic static {config.StructName} operator *({config.StructName} a, {config.StructName} b) {{ return new {config.StructName}({mulComponents}); }}");
            source.AppendLine();

            // Division (component-wise)
            source.AppendLine($"\t{Inline}");
            string divComponents;
            if (config.PrimitiveType == "byte" || config.PrimitiveType == "ushort")
            {
                divComponents = string.Join(", ", config.Components.Select(c => $"({config.PrimitiveType})(a.{c} / b.{c})").ToArray());
            }
            else
            {
                divComponents = string.Join(", ", config.Components.Select(c => $"a.{c} / b.{c}").ToArray());
            }
            source.AppendLine($"\tpublic static {config.StructName} operator /({config.StructName} a, {config.StructName} b) {{ return new {config.StructName}({divComponents}); }}");
            source.AppendLine();

            // Modulus (component-wise)
            source.AppendLine($"\t{Inline}");
            string modComponents;
            if (config.PrimitiveType == "byte" || config.PrimitiveType == "ushort")
            {
                modComponents = string.Join(", ", config.Components.Select(c => $"({config.PrimitiveType})(a.{c} % b.{c})").ToArray());
            }
            else
            {
                modComponents = string.Join(", ", config.Components.Select(c => $"a.{c} % b.{c}").ToArray());
            }
            source.AppendLine($"\tpublic static {config.StructName} operator %({config.StructName} a, {config.StructName} b) {{ return new {config.StructName}({modComponents}); }}");
            source.AppendLine();

            // Negation (only for signed types)
            if (isSignedType)
            {
                source.AppendLine($"\t{Inline}");
                string negComponents;
                if (config.PrimitiveType == "byte" || config.PrimitiveType == "ushort")
                {
                    negComponents = string.Join(", ", config.Components.Select(c => $"({config.PrimitiveType})-v.{c}").ToArray());
                }
                else
                {
                    negComponents = string.Join(", ", config.Components.Select(c => $"-v.{c}").ToArray());
                }
                source.AppendLine($"\tpublic static {config.StructName} operator -({config.StructName} v) {{ return new {config.StructName}({negComponents}); }}");
                source.AppendLine();
            }

            // --- Bitwise Operators for Integer Types ---
            if (config.PrimitiveType == "int" || config.PrimitiveType == "byte" || config.PrimitiveType == "ushort" || config.PrimitiveType == "uint" || config.PrimitiveType == "ulong")
            {
                // Bitwise AND
                source.AppendLine($"\t{Inline}");
                string bitwiseAndComponents;
                if (config.PrimitiveType == "byte" || config.PrimitiveType == "ushort")
                {
                    bitwiseAndComponents = string.Join(", ", config.Components.Select(c => $"({config.PrimitiveType})(a.{c} & b.{c})").ToArray());
                }
                else
                {
                    bitwiseAndComponents = string.Join(", ", config.Components.Select(c => $"a.{c} & b.{c}").ToArray());
                }
                source.AppendLine($"\tpublic static {config.StructName} operator &({config.StructName} a, {config.StructName} b) {{ return new {config.StructName}({bitwiseAndComponents}); }}");
                source.AppendLine();

                // Bitwise OR
                source.AppendLine($"\t{Inline}");
                string bitwiseOrComponents;
                if (config.PrimitiveType == "byte" || config.PrimitiveType == "ushort")
                {
                    bitwiseOrComponents = string.Join(", ", config.Components.Select(c => $"({config.PrimitiveType})(a.{c} | b.{c})").ToArray());
                }
                else
                {
                    bitwiseOrComponents = string.Join(", ", config.Components.Select(c => $"a.{c} | b.{c}").ToArray());
                }
                source.AppendLine($"\tpublic static {config.StructName} operator |({config.StructName} a, {config.StructName} b) {{ return new {config.StructName}({bitwiseOrComponents}); }}");
                source.AppendLine();

                // Bitwise XOR
                source.AppendLine($"\t{Inline}");
                string bitwiseXorComponents;
                if (config.PrimitiveType == "byte" || config.PrimitiveType == "ushort")
                {
                    bitwiseXorComponents = string.Join(", ", config.Components.Select(c => $"({config.PrimitiveType})(a.{c} ^ b.{c})").ToArray());
                }
                else
                {
                    bitwiseXorComponents = string.Join(", ", config.Components.Select(c => $"a.{c} ^ b.{c}").ToArray());
                }
                source.AppendLine($"\tpublic static {config.StructName} operator ^({config.StructName} a, {config.StructName} b) {{ return new {config.StructName}({bitwiseXorComponents}); }}");
                source.AppendLine();

                // Bitwise NOT (unary)
                source.AppendLine($"\t{Inline}");
                string bitwiseNotComponents;
                if (config.PrimitiveType == "byte" || config.PrimitiveType == "ushort")
                {
                    bitwiseNotComponents = string.Join(", ", config.Components.Select(c => $"({config.PrimitiveType})(~v.{c})").ToArray());
                }
                else
                {
                    bitwiseNotComponents = string.Join(", ", config.Components.Select(c => $"~v.{c}").ToArray());
                }
                source.AppendLine($"\tpublic static {config.StructName} operator ~({config.StructName} v) {{ return new {config.StructName}({bitwiseNotComponents}); }}");
                source.AppendLine();

                // Left Shift (vector by scalar int)
                source.AppendLine($"\t{Inline}");
                string leftShiftComponents;
                if (config.PrimitiveType == "byte" || config.PrimitiveType == "ushort")
                {
                    leftShiftComponents = string.Join(", ", config.Components.Select(c => $"({config.PrimitiveType})(v.{c} << amount)").ToArray());
                }
                else
                {
                    leftShiftComponents = string.Join(", ", config.Components.Select(c => $"v.{c} << amount").ToArray());
                }
                source.AppendLine($"\tpublic static {config.StructName} operator <<({config.StructName} v, int amount) {{ return new {config.StructName}({leftShiftComponents}); }}");
                source.AppendLine();

                // Right Shift (vector by scalar int)
                source.AppendLine($"\t{Inline}");
                string rightShiftComponents;
                if (config.PrimitiveType == "byte" || config.PrimitiveType == "ushort")
                {
                    rightShiftComponents = string.Join(", ", config.Components.Select(c => $"({config.PrimitiveType})(v.{c} >> amount)").ToArray());
                }
                else
                {
                    rightShiftComponents = string.Join(", ", config.Components.Select(c => $"v.{c} >> amount").ToArray());
                }
                source.AppendLine($"\tpublic static {config.StructName} operator >>({config.StructName} v, int amount) {{ return new {config.StructName}({rightShiftComponents}); }}");
                source.AppendLine();
            }

            source.AppendLine($"\t// --- Scalar-Vector Operators ---");

            // Generate operators for all types (including same type)
            foreach (var scalarType in numericTypes.Where(t => IsNumericType(t)))
            {
                bool castScalar = false;
                if (config.PrimitiveType == "ulong" && scalarType == "int")
                    castScalar = true;
                else if (config.PrimitiveType == "int" && scalarType == "ulong")
                    castScalar = true;

                // scalar + vector
                source.AppendLine($"\t/// <summary>{scalarType} + {config.StructName} operator. Vector components are ({string.Join(", ", config.Components.Select(c => $"scalar + v.{c}"))}).</summary>");
                source.AppendLine($"\t{Inline}");
                GenerateScalarVectorOp(source, "+", scalarType, config.StructName, config.PrimitiveType, config.Components, true, castScalar);

                // vector + scalar
                source.AppendLine($"\t/// <summary>{config.StructName} + {scalarType} operator. Vector components are ({string.Join(", ", config.Components.Select(c => $"v.{c} + scalar"))}).</summary>");
                source.AppendLine($"\t{Inline}");
                GenerateScalarVectorOp(source, "+", scalarType, config.StructName, config.PrimitiveType, config.Components, false, castScalar);

                // scalar - vector
                source.AppendLine($"\t/// <summary>{scalarType} - {config.StructName} operator. Vector components are ({string.Join(", ", config.Components.Select(c => $"scalar - v.{c}"))}).</summary>");
                source.AppendLine($"\t{Inline}");
                GenerateScalarVectorOp(source, "-", scalarType, config.StructName, config.PrimitiveType, config.Components, true, castScalar);

                // vector - scalar
                source.AppendLine($"\t/// <summary>{config.StructName} - {scalarType} operator. Vector components are ({string.Join(", ", config.Components.Select(c => $"v.{c} - scalar"))}).</summary>");
                source.AppendLine($"\t{Inline}");
                GenerateScalarVectorOp(source, "-", scalarType, config.StructName, config.PrimitiveType, config.Components, false, castScalar);

                // scalar * vector
                source.AppendLine($"\t/// <summary>{scalarType} * {config.StructName} operator. Vector components are ({string.Join(", ", config.Components.Select(c => $"scalar * v.{c}"))}).</summary>");
                source.AppendLine($"\t{Inline}");
                GenerateScalarVectorOp(source, "*", scalarType, config.StructName, config.PrimitiveType, config.Components, true, castScalar);

                // vector * scalar
                source.AppendLine($"\t/// <summary>{config.StructName} * {scalarType} operator. Vector components are ({string.Join(", ", config.Components.Select(c => $"v.{c} * scalar"))}).</summary>");
                source.AppendLine($"\t{Inline}");
                GenerateScalarVectorOp(source, "*", scalarType, config.StructName, config.PrimitiveType, config.Components, false, castScalar);

                // scalar / vector
                source.AppendLine($"\t/// <summary>{scalarType} / {config.StructName} operator. Vector components are ({string.Join(", ", config.Components.Select(c => $"v.{c} / scalar"))}).</summary>");
                source.AppendLine($"\t{Inline}");
                GenerateScalarVectorOp(source, "/", scalarType, config.StructName, config.PrimitiveType, config.Components, false, castScalar);

                // scalar / vector
                source.AppendLine($"\t/// <summary>{scalarType} / {config.StructName} operator. Vector components are ({string.Join(", ", config.Components.Select(c => $"scalar / v.{c}"))}).</summary>");
                source.AppendLine($"\t{Inline}");
                GenerateScalarVectorOp(source, "/", scalarType, config.StructName, config.PrimitiveType, config.Components, true, castScalar);

                // vector % scalar
                source.AppendLine($"\t/// <summary>{config.StructName} % {scalarType} operator. Vector components are ({string.Join(", ", config.Components.Select(c => $"v.{c} % scalar"))}).</summary>");
                source.AppendLine($"\t{Inline}");
                GenerateScalarVectorOp(source, "%", scalarType, config.StructName, config.PrimitiveType, config.Components, false, castScalar);

                // scalar % vector
                source.AppendLine($"\t/// <summary>{scalarType} % {config.StructName} operator. Vector components are ({string.Join(", ", config.Components.Select(c => $"scalar % v.{c}"))}).</summary>");
                source.AppendLine($"\t{Inline}");
                GenerateScalarVectorOp(source, "%", scalarType, config.StructName, config.PrimitiveType, config.Components, true, castScalar);

            }

            // --- Component-wise Comparison Operators (Return Boolean Vector) ---
            // These are for non-bool primitive types, returning a BoolN vector.
            source.AppendLine($"\t// --- Component-wise Comparison Operators (Return Boolean Vector) ---");
            string boolVectorName = $"Bool{config.Dimensions}"; // e.g., Bool2, Bool3, Bool4

            string[] comparisonOperators = new string[] { "<", "<=", ">", ">=", "==", "!=" };

            foreach (string op in comparisonOperators)
            {
                // Vector OP Vector -> BoolN
                source.AppendLine($"\t/// <summary>Returns a {boolVectorName} indicating the result of component-wise {op} comparison.</summary>");
                source.AppendLine($"\t{Inline}");
                var comparisonComponentsVV = string.Join(", ", config.Components.Select(c => $"a.{c} {op} b.{c}"));
                source.AppendLine($"\tpublic static {boolVectorName} operator {op}({config.StructName} a, {config.StructName} b) {{ return new {boolVectorName}({comparisonComponentsVV}); }}");
                source.AppendLine();

                // Vector OP Scalar -> BoolN
                source.AppendLine($"\t/// <summary>Returns a {boolVectorName} indicating the result of component-wise {op} comparison with a scalar.</summary>");
                source.AppendLine($"\t{Inline}");
                var comparisonComponentsVS = string.Join(", ", config.Components.Select(c => $"a.{c} {op} scalar"));
                source.AppendLine($"\tpublic static {boolVectorName} operator {op}({config.StructName} a, {config.PrimitiveType} scalar) {{ return new {boolVectorName}({comparisonComponentsVS}); }}");
                source.AppendLine();

                // Scalar OP Vector -> BoolN
                source.AppendLine($"\t/// <summary>Returns a {boolVectorName} indicating the result of component-wise {op} comparison with a scalar.</summary>");
                source.AppendLine($"\t{Inline}");
                var comparisonComponentsSV = string.Join(", ", config.Components.Select(c => $"scalar {op} a.{c}"));
                source.AppendLine($"\tpublic static {boolVectorName} operator {op}({config.PrimitiveType} scalar, {config.StructName} a) {{ return new {boolVectorName}({comparisonComponentsSV}); }}");
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

        source.AppendLine($"\tpublic static {vectorType} operator {op}({operand1}, {operand2})");
        source.AppendLine($"\t{{");

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
        source.AppendLine($"\t\treturn new {vectorType}({constructorArgs});");

        source.AppendLine($"\t}}");
        source.AppendLine();
    }

    private static void GenerateConversions(StringBuilder source, VectorConfig config)
    {
        source.AppendLine($"\t// --- Casting ---");

        // Add implicit conversions to System.Numerics types
        if (IsSystemNumericsCompatible(config.PrimitiveType, config.Dimensions))
        {
            string numericsType = $"Vector{config.Dimensions}";

            // To System.Numerics.Vector
            source.AppendLine($"\t/// <summary>Implicitly converts this {config.StructName} to a System.Numerics.{numericsType}.</summary>");
            source.AppendLine($"\t{Inline}");
            var toNumericsArgs = string.Join(", ", config.Components.Select(c => $"value.{c}"));
            source.AppendLine($"\tpublic static implicit operator {numericsType}({config.StructName} value) => new {numericsType}({toNumericsArgs});");
            source.AppendLine();

            // From System.Numerics.Vector
            source.AppendLine($"\t/// <summary>Implicitly converts a System.Numerics.{numericsType} to this {config.StructName}.</summary>");
            source.AppendLine($"\t{Inline}");
            var fromNumericsArgs = string.Join(", ", config.Components.Select(c => $"value.{c}"));
            source.AppendLine($"\tpublic static implicit operator {config.StructName}({numericsType} value) => new {config.StructName}({fromNumericsArgs});");
            source.AppendLine();
        }

        // Add conversions between same-type different dimensions
        GenerateVectorDimensionConversions(source, config);

        // Add conversions between different types same dimension
        GenerateCrossTypeConversions(source, config);
    }

    private static void GenerateVectorDimensionConversions(StringBuilder source, VectorConfig config)
    {
        source.AppendLine("\t// --- Cross-Dimensions Casting Operators ---");

        // Convert from lower dimensions (implicit)
        if (config.Dimensions > 2)
        {
            // From Vector2 (add zeros for missing components)
            source.AppendLine($"\t/// <summary>Implicitly converts a {config.Prefix}2 to {config.StructName} by adding default values for missing components.</summary>");
            source.AppendLine($"\t{Inline}");
            var args2D = new List<string> { "value.X", "value.Y" };

            for (int i = 2; i < config.Dimensions; i++)
                args2D.Add(GetZeroValue(config.PrimitiveType));

            source.AppendLine($"\tpublic static implicit operator {config.StructName}({config.Prefix}2 value) => new {config.StructName}({string.Join(", ", args2D)});");
            source.AppendLine();
        }

        if (config.Dimensions > 3)
        {
            // From Vector3 (add zeros for missing components)
            source.AppendLine($"\t/// <summary>Implicitly converts a {config.Prefix}3 to {config.StructName} by adding default values for missing components.</summary>");
            source.AppendLine($"\t{Inline}");
            var args3D = new List<string> { "value.X", "value.Y", "value.Z" };

            for (int i = 3; i < config.Dimensions; i++)
                args3D.Add(GetZeroValue(config.PrimitiveType));

            source.AppendLine($"\tpublic static implicit operator {config.StructName}({config.Prefix}3 value) => new {config.StructName}({string.Join(", ", args3D)});");
            source.AppendLine();
        }

        // Explicit conversions to lower dimensions (truncation)
        if (config.Dimensions > 2)
        {
            // To Vector2 (explicit because it loses data)
            source.AppendLine($"\t/// <summary>Explicitly converts {config.StructName} to {config.Prefix}2 by truncating components.</summary>");
            source.AppendLine($"\t{Inline}");
            source.AppendLine($"\tpublic static explicit operator {config.Prefix}2({config.StructName} value) => new {config.Prefix}2(value.X, value.Y);");
            source.AppendLine();
        }

        if (config.Dimensions > 3)
        {
            // To Vector3 (explicit because it loses data)
            source.AppendLine($"\t/// <summary>Explicitly converts {config.StructName} to {config.Prefix}3 by truncating components.</summary>");
            source.AppendLine($"\t{Inline}");
            source.AppendLine($"\tpublic static explicit operator {config.Prefix}3({config.StructName} value) => new {config.Prefix}3(value.X, value.Y, value.Z);");
            source.AppendLine();
        }
    }

    private static void GenerateCrossTypeConversions(StringBuilder source, VectorConfig config)
    {
        if (!IsNumericType(config.PrimitiveType)) return;

        source.AppendLine("\t// --- Cross-Type Casting Operators ---");

        var compatibleTypes = s_vectorConfigs.Where(t =>
            t.PrimitiveType != config.PrimitiveType &&
            IsNumericType(t.PrimitiveType) &&
            t.Dimensions == config.Dimensions);

        foreach (var otherType in compatibleTypes)
        {
            // Operator to convert FROM otherType TO this config's type
            bool isWidening = IsWideningConversion(otherType.PrimitiveType, config.PrimitiveType);
            string castKeyword = isWidening ? "implicit" : "explicit";

            source.AppendLine($"\t/// <summary>{(isWidening ? "Implicitly" : "Explicitly")} converts a {otherType.StructName} to a {config.StructName}.</summary>");
            source.AppendLine($"\t{Inline}");
            // Use the component-casting constructor we already generate
            source.AppendLine($"\tpublic static {castKeyword} operator {config.StructName}({otherType.StructName} v) => new {config.StructName}(v);");
            source.AppendLine();
        }
    }

    private static void GenerateMethods(StringBuilder source, VectorConfig config, bool isFloatingPoint, string mathClass)
    {
        source.AppendLine($"\t// --- Methods ---");

        // For boolean vectors, add some useful methods
        if (config.PrimitiveType == "bool")
        {
            // Any() method - returns true if any component is true
            source.AppendLine($"\t/// <summary>Returns true if any component is true.</summary>");
            source.AppendLine($"\t{Inline}");
            var anyComponents = string.Join(" || ", config.Components.Select(c => c).ToArray());
            source.AppendLine($"\tpublic bool Any() {{ return {anyComponents}; }}");
            source.AppendLine();

            // All() method - returns true if all components are true
            source.AppendLine($"\t/// <summary>Returns true if all components are true.</summary>");
            source.AppendLine($"\t{Inline}");
            var allComponents = string.Join(" && ", config.Components.Select(c => c).ToArray());
            source.AppendLine($"\tpublic bool All() {{ return {allComponents}; }}");
            source.AppendLine();

            // None() method - returns true if no components are true
            source.AppendLine($"\t/// <summary>Returns true if all components are false.</summary>");
            source.AppendLine($"\t{Inline}");
            var noneComponents = string.Join(" && ", config.Components.Select(c => $"!{c}").ToArray());
            source.AppendLine($"\tpublic bool None() {{ return {noneComponents}; }}");
            source.AppendLine();
        }

        // For all non-boolean types, add comparison methods that return boolean vectors
        if (config.PrimitiveType != "bool")
        {
            var boolStructName = $"Bool{config.Dimensions}";

            // LessThan
            source.AppendLine($"\t/// <summary>Returns a boolean vector indicating which components are less than the corresponding components of another vector.</summary>");
            source.AppendLine($"\t{Inline}");
            var lessThanComponents = string.Join(", ", config.Components.Select(c => $"{c} < other.{c}").ToArray());
            source.AppendLine($"\tpublic {boolStructName} LessThan({config.StructName} other) {{ return new {boolStructName}({lessThanComponents}); }}");
            source.AppendLine();

            // LessThanOrEqual
            source.AppendLine($"\t/// <summary>Returns a boolean vector indicating which components are less than or equal to the corresponding components of another vector.</summary>");
            source.AppendLine($"\t{Inline}");
            var lessThanOrEqualComponents = string.Join(", ", config.Components.Select(c => $"{c} <= other.{c}").ToArray());
            source.AppendLine($"\tpublic {boolStructName} LessThanOrEqual({config.StructName} other) {{ return new {boolStructName}({lessThanOrEqualComponents}); }}");
            source.AppendLine();

            // GreaterThan
            source.AppendLine($"\t/// <summary>Returns a boolean vector indicating which components are greater than the corresponding components of another vector.</summary>");
            source.AppendLine($"\t{Inline}");
            var greaterThanComponents = string.Join(", ", config.Components.Select(c => $"{c} > other.{c}").ToArray());
            source.AppendLine($"\tpublic {boolStructName} GreaterThan({config.StructName} other) {{ return new {boolStructName}({greaterThanComponents}); }}");
            source.AppendLine();

            // GreaterThanOrEqual
            source.AppendLine($"\t/// <summary>Returns a boolean vector indicating which components are greater than or equal to the corresponding components of another vector.</summary>");
            source.AppendLine($"\t{Inline}");
            var greaterThanOrEqualComponents = string.Join(", ", config.Components.Select(c => $"{c} >= other.{c}").ToArray());
            source.AppendLine($"\tpublic {boolStructName} GreaterThanOrEqual({config.StructName} other) {{ return new {boolStructName}({greaterThanOrEqualComponents}); }}");
            source.AppendLine();

            // Select method - chooses between two vectors based on a boolean mask
            source.AppendLine($"\t/// <summary>Selects components from two vectors based on a boolean mask.</summary>");
            source.AppendLine($"\t/// <param name=\"mask\">Boolean vector mask for selection.</param>");
            source.AppendLine($"\t/// <param name=\"trueValue\">Vector to select from when mask component is true.</param>");
            source.AppendLine($"\t/// <param name=\"falseValue\">Vector to select from when mask component is false.</param>");
            source.AppendLine($"\t{Inline}");
            var selectComponents = string.Join(", ", config.Components.Select((c, i) => $"mask.{c} ? trueValue.{c} : falseValue.{c}").ToArray());
            source.AppendLine($"\tpublic static {config.StructName} Select({boolStructName} mask, {config.StructName} trueValue, {config.StructName} falseValue)");
            source.AppendLine($"\t{{");
            source.AppendLine($"\t\treturn new {config.StructName}({selectComponents});");
            source.AppendLine($"\t}}");
            source.AppendLine();

            // IsInRange (component-wise range check)
            source.AppendLine($"\t/// <summary>Returns a boolean vector indicating which components are within the specified range.</summary>");
            source.AppendLine($"\t{Inline}");
            var inRangeComponents = string.Join(", ", config.Components.Select(c => $"{c} >= min.{c} && {c} <= max.{c}").ToArray());
            source.AppendLine($"\tpublic {boolStructName} InRange({config.StructName} min, {config.StructName} max)");
            source.AppendLine($"\t{{");
            source.AppendLine($"\t\treturn new {boolStructName}({inRangeComponents});");
            source.AppendLine($"\t}}");
            source.AppendLine();

            // EqualTo (component-wise equality that returns a boolean vector)
            source.AppendLine($"\t/// <summary>Returns a boolean vector indicating which components are equal to the corresponding components of another vector.</summary>");
            source.AppendLine($"\t{Inline}");
            var equalToComponents = string.Join(", ", config.Components.Select(c => $"{c} == other.{c}").ToArray());
            source.AppendLine($"\tpublic {boolStructName} EqualTo({config.StructName} other) {{ return new {boolStructName}({equalToComponents}); }}");
            source.AppendLine();

            // NotEqualTo
            source.AppendLine($"\t/// <summary>Returns a boolean vector indicating which components are not equal to the corresponding components of another vector.</summary>");
            source.AppendLine($"\t{Inline}");
            var notEqualToComponents = string.Join(", ", config.Components.Select(c => $"{c} != other.{c}").ToArray());
            source.AppendLine($"\tpublic {boolStructName} NotEqualTo({config.StructName} other) {{ return new {boolStructName}({notEqualToComponents}); }}");
            source.AppendLine();

            // ApproximatelyEqualTo (for floating-point types)
            if (isFloatingPoint)
            {
                source.AppendLine($"\t/// <summary>Returns a boolean vector indicating which components are approximately equal to the corresponding components of another vector.</summary>");
                source.AppendLine($"\t{Inline}");
                var approxEqualComponents = string.Join(", ", config.Components.Select(c => $"{mathClass}.Abs({c} - other.{c}) <= {config.PrimitiveType}.Epsilon").ToArray());
                source.AppendLine($"\tpublic {boolStructName} ApproximatelyEqualTo({config.StructName} other) {{ return new {boolStructName}({approxEqualComponents}); }}");
                source.AppendLine();

                source.AppendLine($"\t/// <summary>Returns a boolean vector indicating which components are approximately equal to the corresponding components of another vector with a custom tolerance.</summary>");
                source.AppendLine($"\t{Inline}");
                var approxEqualToleranceComponents = string.Join(", ", config.Components.Select(c => $"{mathClass}.Abs({c} - other.{c}) <= tolerance").ToArray());
                source.AppendLine($"\tpublic {boolStructName} ApproximatelyEqualTo({config.StructName} other, {config.PrimitiveType} tolerance)");
                source.AppendLine($"\t{{");
                source.AppendLine($"\t\treturn new {boolStructName}({approxEqualToleranceComponents});");
                source.AppendLine($"\t}}");
                source.AppendLine();
            }

            // Scalar comparison methods
            source.AppendLine($"\t/// <summary>Returns a boolean vector indicating which components are less than a scalar value.</summary>");
            source.AppendLine($"\t{Inline}");
            var scalarLessThanComponents = string.Join(", ", config.Components.Select(c => $"{c} < scalar").ToArray());
            source.AppendLine($"\tpublic {boolStructName} LessThan({config.PrimitiveType} scalar) {{ return new {boolStructName}({scalarLessThanComponents}); }}");
            source.AppendLine();

            source.AppendLine($"\t/// <summary>Returns a boolean vector indicating which components are greater than a scalar value.</summary>");
            source.AppendLine($"\t{Inline}");
            var scalarGreaterThanComponents = string.Join(", ", config.Components.Select(c => $"{c} > scalar").ToArray());
            source.AppendLine($"\tpublic {boolStructName} GreaterThan({config.PrimitiveType} scalar) {{ return new {boolStructName}({scalarGreaterThanComponents}); }}");
            source.AppendLine();

            source.AppendLine($"\t/// <summary>Returns a boolean vector indicating which components are equal to a scalar value.</summary>");
            source.AppendLine($"\t{Inline}");
            var scalarEqualToComponents = string.Join(", ", config.Components.Select(c => $"{c} == scalar").ToArray());
            source.AppendLine($"\tpublic {boolStructName} EqualTo({config.PrimitiveType} scalar) {{ return new {boolStructName}({scalarEqualToComponents}); }}");
            source.AppendLine();
        }

        // ToArray
        source.AppendLine($"\t/// <summary>Returns an array of components.</summary>");
        source.AppendLine($"\t{Inline}");
        source.AppendLine($"\tpublic {config.PrimitiveType}[] ToArray() {{ return new {config.PrimitiveType}[] {{ {string.Join(", ", config.Components)} }}; }}");
        source.AppendLine($"\t/// <summary>Returns an array of components.</summary>");

        // Equals
        source.AppendLine($"\tpublic override bool Equals(object? obj) {{ return obj is {config.StructName} && Equals(({config.StructName})obj); }}");
        source.AppendLine();

        source.AppendLine($"\t{Inline}");
        var equalsComponents = string.Join(" && ", config.Components.Select(c => $"{c} == other.{c}").ToArray());
        source.AppendLine($"\tpublic bool Equals({config.StructName} other) {{ return {equalsComponents}; }}");
        source.AppendLine();

        // GetHashCode
        if (config.Dimensions == 2)
        {
            source.AppendLine($"\tpublic override int GetHashCode() {{ return {config.Components[0]}.GetHashCode() ^ ({config.Components[1]}.GetHashCode() << 2); }}");
        }
        else if (config.Dimensions == 3)
        {
            source.AppendLine($"\tpublic override int GetHashCode() {{ return {config.Components[0]}.GetHashCode() ^ ({config.Components[1]}.GetHashCode() << 2) ^ ({config.Components[2]}.GetHashCode() >> 2); }}");
        }
        else if (config.Dimensions == 4)
        {
            source.AppendLine($"\tpublic override int GetHashCode() {{ return {config.Components[0]}.GetHashCode() ^ ({config.Components[1]}.GetHashCode() << 2) ^ ({config.Components[2]}.GetHashCode() >> 2) ^ ({config.Components[3]}.GetHashCode() >> 1); }}");
        }
        source.AppendLine();

        // ToString - different implementation for bool vs other types
        if (config.PrimitiveType == "bool")
        {
            // For boolean vectors, use True/False instead of numeric formatting
            source.AppendLine($"\tpublic override string ToString()");
            source.AppendLine($"\t{{");
            source.AppendLine($"\t\treturn ToString(CultureInfo.CurrentCulture);");
            source.AppendLine($"\t}}");
            source.AppendLine();

            source.AppendLine($"\tpublic string ToString(IFormatProvider formatProvider)");
            source.AppendLine($"\t{{");
            source.AppendLine($"\t\tstring separator = (formatProvider is CultureInfo) ? ((CultureInfo)formatProvider).TextInfo.ListSeparator : \", \";");
            var boolToStringComponents = new List<string>();
            foreach (var c in config.Components)
            {
                boolToStringComponents.Add($"{c}.ToString()");
            }
            var boolToStringExpression = string.Join(" + separator + ", boolToStringComponents.ToArray());
            source.AppendLine($"\t\treturn \"(\" + {boolToStringExpression} + \")\";");
            source.AppendLine($"\t}}");
            source.AppendLine();

            // Simplified format method for bool (format parameter is ignored for booleans)
            source.AppendLine($"\tpublic string ToString(string format) {{ return ToString(CultureInfo.CurrentCulture); }}");
            source.AppendLine();

            source.AppendLine($"\tpublic string ToString(string format, IFormatProvider formatProvider)");
            source.AppendLine($"\t{{");
            source.AppendLine($"\t\t// Format is ignored for boolean vectors");
            source.AppendLine($"\t\treturn ToString(formatProvider);");
            source.AppendLine($"\t}}");
        }
        else
        {
            // For numeric types, use the existing format-based ToString
            source.AppendLine($"\tpublic override string ToString() {{ return ToString(\"G\", CultureInfo.CurrentCulture); }}");
            source.AppendLine();

            source.AppendLine($"\tpublic string ToString(string format) {{ return ToString(format, CultureInfo.CurrentCulture); }}");
            source.AppendLine();

            source.AppendLine($"\tpublic string ToString(string format, IFormatProvider formatProvider)");
            source.AppendLine($"\t{{");
            source.AppendLine($"\t\tstring separator = (formatProvider is CultureInfo) ? ((CultureInfo)formatProvider).TextInfo.ListSeparator : \", \";");
            var toStringComponents = new List<string>();
            foreach (var c in config.Components)
            {
                toStringComponents.Add($"{c}.ToString(format, formatProvider)");
            }
            var toStringExpression = string.Join(" + separator + ", toStringComponents.ToArray());
            source.AppendLine($"\t\treturn \"(\" + {toStringExpression} + \")\";");
            source.AppendLine($"\t}}");
        }
    }

    private static bool IsWideningConversion(string fromType, string toType)
    {
        if (fromType == toType) return true;
        return s_wideningConversions.Contains((fromType, toType));
    }

    #endregion

    #region Swizzle Generation Methods

    // Generates the swizzle code string for a single configured struct
    private static string GenerateSwizzlesForStruct(VectorConfig config)
    {
        StringBuilder sb = new StringBuilder();

        // Add auto-generated header comment
        AddHeader(sb);

        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine();
        sb.AppendLine($"namespace Prowl.Vector");
        sb.AppendLine("{");
        sb.AppendLine();
        sb.AppendLine($"public partial struct {config.StructName}"); // Generate as partial
        sb.AppendLine("{");

        int sourceDimension = config.Components.Length;

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
        sb.AppendLine("}"); // end struct
        sb.AppendLine("}"); // end namespace

        return sb.ToString();
    }

    // Recursive helper to generate permutations and build the code string
    private static void GeneratePermutationsRecursive(StringBuilder sb, VectorConfig config, char[] swizzleChars, int sourceDim, int outputDim, int[] currentPermutationIndices, int depth)
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
    private static void AppendSwizzleProperty(StringBuilder sb, VectorConfig config, char[] availableSwizzleChars, int[] componentIndices, int outputDim)
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
            returnTypeName = config.PrimitiveType;
        }
        else
        {
            // Construct type name like "Float2", "Int3" etc.
            returnTypeName = $"{config.Prefix}{outputDim}";
        }

        sb.AppendLine($"\t/// <summary>Gets or sets the {propertyName.ToLowerInvariant()} swizzle.</summary>");
        sb.AppendLine($"\tpublic {returnTypeName} {propertyName}");
        sb.AppendLine($"\t{{");

        // --- Getter ---
        sb.Append($"\t\t{Inline} get => ");
        if (outputDim == 1)
        {
            // Access like this.X, this.Y etc.
            sb.Append($"this.{config.Components[componentIndices[0]]};");
        }
        else
        {
            // Construct like new Float2(this.X, this.Y)
            sb.Append($"new {returnTypeName}(");
            for (int i = 0; i < outputDim; i++)
            {
                sb.Append($"this.{config.Components[componentIndices[i]]}");
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
            sb.AppendLine($"\t\t{Inline}");
            sb.Append($"\t\tset {{");
            if (outputDim == 1)
            {
                // Assignment like this.X = value;
                sb.Append($"this.{config.Components[componentIndices[0]]} = value;");
            }
            else
            {
                // Assignment like this.X = value.X; this.Y = value.Y;
                // Assumes the 'value' (e.g., Float2) has X, Y, Z, W standard component names for access
                string[] valueAccessors = ["X", "Y", "Z", "W"];
                for (int i = 0; i < outputDim; i++)
                {
                    // Ensure we don't try to access beyond value's dimension (e.g., value.Z for Float2)
                    if (i >= valueAccessors.Length) break;
                    sb.Append($"this.{config.Components[componentIndices[i]]} = value.{valueAccessors[i]}; ");
                    if (i < outputDim - 1) sb.Append(" ");
                }
            }
            sb.Append(" }");
            sb.AppendLine();
        }

        sb.AppendLine($"\t}}"); // end property
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

        source.AppendLine("using System; ");
        source.AppendLine("using System.Runtime.CompilerServices;");
        source.AppendLine();
        source.AppendLine("namespace Prowl.Vector");
        source.AppendLine("{");
        source.AppendLine("/// <summary>");
        source.AppendLine("/// A static class containing mathematical functions for vectors and scalars.");
        source.AppendLine("/// </summary>");
        source.AppendLine("public static partial class Maths");
        source.AppendLine("{");
        source.AppendLine();

        // Generate functions for each discovered generator
        foreach (var generator in generators.OrderBy(kvp => kvp.Key))
        {
            source.AppendLine($"\t// {generator.Key} functions");

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

        source.AppendLine("}");
        source.AppendLine("}");

        return source.ToString();
    }

    private static void GenerateMathTests(string outputDirectory)
    {
        var testOutputDirectory = Path.Combine(outputDirectory, "Tests", "Generated");
        Directory.CreateDirectory(testOutputDirectory);

        var generators = DiscoverMathFunctions();
        var testMethods = new StringBuilder();

        foreach (var generatorEntry in generators.OrderBy(kvp => kvp.Key))
        {
            var functionGenerator = generatorEntry.Value;
            var functionName = generatorEntry.Key;

            // Generate scalar tests if supported
            if (functionGenerator.SupportsScalars)
            {
                foreach (var type in functionGenerator.SupportedTypes)
                {
                    if (functionGenerator.SupportsType(type, 1))
                    {
                        foreach (var method in functionGenerator.GenerateTestMethods(type, 1, null))
                        {
                            testMethods.AppendLine(method);
                            testMethods.AppendLine(); // Add a blank line for readability
                        }
                    }
                }
            }

            // Generate vector tests
            foreach (var type in functionGenerator.SupportedTypes)
            {
                foreach (var dimension in functionGenerator.SupportedDimensions)
                {
                    if (functionGenerator.SupportsType(type, dimension))
                    {
                        var components = GetComponents(dimension);
                        foreach (var method in functionGenerator.GenerateTestMethods(type, dimension, components))
                        {
                            testMethods.AppendLine(method);
                            testMethods.AppendLine(); // Add a blank line for readability
                        }
                    }
                }
            }
        }

        var testSource = new StringBuilder();
        AddHeader(testSource);

        testSource.AppendLine("using Xunit;");
        testSource.AppendLine("using Prowl.Vector;");
        testSource.AppendLine("using System;");
        testSource.AppendLine();
        testSource.AppendLine("namespace Prowl.Vector.Tests");
        testSource.AppendLine("{");
        testSource.AppendLine("    public class GeneratedMathTests");
        testSource.AppendLine("    {");
        testSource.Append(testMethods.ToString()); // Append all generated test methods
        testSource.AppendLine("    }");
        testSource.AppendLine("}");

        var fileName = "GeneratedMathTests.cs";
        var filePath = Path.Combine(testOutputDirectory, fileName);
        File.WriteAllText(filePath, testSource.ToString(), Encoding.UTF8);
        Console.WriteLine($"Generated: {fileName}");
    }

    #endregion

    #region Matrix Generation Methods

    private static string GenerateMatrixStruct(MatrixConfig config)
    {
        var sb = new StringBuilder();
        var structName = config.StructName;
        var componentType = config.PrimitiveType;
        var rows = config.Rows;
        var cols = config.Columns;
        var columnVectorType = config.GetColumnVectorType();

        // Add auto-generated header comment
        AddHeader(sb);

        sb.AppendLine("using System; ");
        sb.AppendLine("using System.Globalization;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using System.Text;");
        sb.AppendLine("using Prowl.Vector;");
        sb.AppendLine();
        sb.AppendLine($"namespace Prowl.Vector");
        sb.AppendLine("{");
        sb.AppendLine();
        sb.AppendLine($"/// <summary>A {rows}x{cols} matrix of {componentType}s.</summary>");
        sb.AppendLine($"[System.Serializable]");
        sb.AppendLine($"public partial struct {structName} : System.IEquatable<{structName}>, IFormattable");
        sb.AppendLine($"{{");

        // --- Fields (Column Vectors) ---
        for (int c = 0; c < cols; c++)
        {
            sb.AppendLine($"\t/// <summary>Column {c} of the matrix.</summary>");
            sb.AppendLine($"\tpublic {columnVectorType} c{c};");
        }
        sb.AppendLine();

        // --- Static Properties (Identity, Zero) ---
        GenerateMatrixStaticFields(sb, config);

        // --- Translation Property (Getter, Setter) ---
        if (config.IsTranslationMatrix())
        {
            sb.AppendLine($"\t/// <summary>Gets or sets the translation component of the matrix.</summary>");
            sb.AppendLine($"\tpublic {columnVectorType} Translation");
            sb.AppendLine($"\t{{");
            sb.AppendLine($"\t\t{Inline} get => c{cols - 1};");
            sb.AppendLine($"\t\t{Inline} set => c{cols - 1} = value;");
            sb.AppendLine($"\t}}");
            sb.AppendLine();
        }

        // --- Constructors ---
        GenerateMatrixConstructors(sb, config);

        // --- Indexer ---
        GenerateMatrixIndexer(sb, config);

        // --- Component-wise Operators ---
        GenerateMatrixComponentWiseOperators(sb, config);

        // --- Casting Operators ---
        GenerateMatrixCrossTypeCasting(sb, config);
        GenerateMatrixNumericsConversions(sb, config);

        // --- Standard Methods (Equals, GetHashCode, ToString) ---
        GenerateMatrixStandardMethods(sb, config);

        sb.AppendLine("}"); // end struct
        sb.AppendLine("}"); // end namespace

        return sb.ToString();
    }

    private static bool IsSystemNumericsMatrixCompatible(MatrixConfig config)
    {
        // System.Numerics has Matrix3x2 and Matrix4x4, both are float-only.
        if (config.PrimitiveType != "float") return false;
        if (config.Rows == 3 && config.Columns == 2) return true;
        if (config.Rows == 4 && config.Columns == 4) return true;
        return false;
    }

    private static void GenerateMatrixStaticFields(StringBuilder sb, MatrixConfig config)
    {
        var structName = config.StructName;
        var componentType = config.PrimitiveType;
        var rows = config.Rows;
        var cols = config.Columns;
        var columnVectorType = config.GetColumnVectorType();

        // Identity matrix only makes sense for square matrices
        if (rows == cols)
        {
            sb.AppendLine($"\t/// <summary>{structName} identity transform.</summary>");
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
            sb.AppendLine($"\tpublic static readonly {structName} Identity = new {structName}({string.Join(", ", identityParams)});");
            sb.AppendLine();
        }

        // Zero matrix works for all matrix dimensions
        sb.AppendLine($"\t/// <summary>{structName} zero value.</summary>");
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
        sb.AppendLine($"\tpublic static readonly {structName} Zero = new {structName}({string.Join(", ", zeroParams)});");
        sb.AppendLine();
    }

    private static void GenerateMatrixConstructors(StringBuilder sb, MatrixConfig config)
    {
        var structName = config.StructName;
        var componentType = config.PrimitiveType;
        var rows = config.Rows;
        var cols = config.Columns;
        var columnVectorType = config.GetColumnVectorType();

        // Constructor from column vectors
        sb.AppendLine($"\t/// <summary>Constructs a {structName} matrix from {cols} {columnVectorType} vectors.</summary>");
        var colParams = new List<string>();
        for (int c = 0; c < cols; c++) colParams.Add($"{columnVectorType} col{c}");
        sb.AppendLine($"\t{Inline}");
        sb.AppendLine($"\tpublic {structName}({string.Join(", ", colParams)})");
        sb.AppendLine($"\t{{");
        for (int c = 0; c < cols; c++) sb.AppendLine($"\t\tthis.c{c} = col{c};");
        sb.AppendLine($"\t}}");
        sb.AppendLine();

        // Constructor from all scalar components (row-major input, stored as column vectors)
        sb.AppendLine($"\t/// <summary>Constructs a {structName} matrix from {rows * cols} {componentType} values given in row-major order.</summary>");
        var scalarParams = new List<string>();
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                scalarParams.Add($"{componentType} m{r}{c}");
        sb.AppendLine($"\t{Inline}");
        sb.AppendLine($"\tpublic {structName}({string.Join(", ", scalarParams)})");
        sb.AppendLine($"\t{{");
        for (int c = 0; c < cols; c++) // Iterate through columns to construct column vectors
        {
            var colVecComponents = new List<string>();
            for (int r = 0; r < rows; r++) // Iterate through rows for this column
            {
                colVecComponents.Add($"m{r}{c}");
            }
            sb.AppendLine($"\t\tthis.c{c} = new {columnVectorType}({string.Join(", ", colVecComponents)});");
        }
        sb.AppendLine($"\t}}");
        sb.AppendLine();

        // Constructor from a single scalar (assigns to all components of all column vectors)
        if (componentType != "bool") // Bools handle this differently, often via select
        {
            sb.AppendLine($"\t/// <summary>Constructs a {structName} matrix from a single {componentType} value by assigning it to every component.</summary>");
            sb.AppendLine($"\t{Inline}");
            sb.AppendLine($"\tpublic {structName}({componentType} v)");
            sb.AppendLine($"\t{{");
            for (int c = 0; c < cols; c++)
            {
                // Assumes vector types have a constructor that takes a single scalar
                // and broadcasts it to all its components.
                sb.AppendLine($"\t\tthis.c{c} = new {columnVectorType}(v);");
            }
            sb.AppendLine($"\t}}");
            sb.AppendLine();
        }

        // Type conversion constructor
        if (IsNumericType(componentType))
        {
            var otherTypes = s_matrixConfigs.Where(m =>
                m.PrimitiveType != componentType &&
                IsNumericType(m.PrimitiveType) &&
                m.Rows == rows &&
                m.Columns == cols);

            foreach (var otherType in otherTypes)
            {
                sb.AppendLine($"\t/// <summary>Constructs a {structName} from a {otherType.StructName} with type conversion.</summary>");
                sb.AppendLine($"\t{Inline}");
                sb.AppendLine($"\tpublic {structName}({otherType.StructName} m)");
                sb.AppendLine($"\t{{");

                for (int c = 0; c < cols; c++)
                {
                    sb.AppendLine($"\t\tthis.c{c} = new {columnVectorType}(m.c{c});");
                }

                sb.AppendLine($"\t}}");
                sb.AppendLine();
            }
        }
    }

    private static void GenerateMatrixComponentWiseOperators(StringBuilder sb, MatrixConfig config)
    {
        var structName = config.StructName;
        var componentType = config.PrimitiveType;
        var cols = config.Columns;
        var columnVectorType = config.GetColumnVectorType();

        sb.AppendLine($"\t// --- Component-wise Operators ---");

        // Arithmetic operators (for non-bool types)
        if (componentType != "bool")
        {
            string[] ops = ["+", "-", "*", "/", "%"];
            foreach (var op in ops)
            {
                // Matrix op Matrix
                sb.AppendLine($"\t/// <summary>Returns the component-wise {op} of two matrices.</summary>");
                sb.AppendLine($"\t{Inline}");
                var opParamsM = new List<string>();
                for (int c = 0; c < cols; c++) opParamsM.Add($"lhs.c{c} {op} rhs.c{c}");
                sb.AppendLine($"\tpublic static {structName} operator {op}({structName} lhs, {structName} rhs) => new {structName}({string.Join(", ", opParamsM)});");
                sb.AppendLine();

                // Matrix op Scalar
                sb.AppendLine($"\t/// <summary>Returns the component-wise {op} of matrix and scalar.</summary>");
                sb.AppendLine($"\t{Inline}");
                var opParamsS1 = new List<string>();
                for (int c = 0; c < cols; c++) opParamsS1.Add($"lhs.c{c} {op} rhs");
                sb.AppendLine($"\tpublic static {structName} operator {op}({structName} lhs, {componentType} rhs) => new {structName}({string.Join(", ", opParamsS1)});");
                sb.AppendLine();

                // Scalar op Matrix
                sb.AppendLine($"\t/// <summary>Returns the component-wise {op} of scalar and matrix.</summary>");
                sb.AppendLine($"\t{Inline}");
                var opParamsS2 = new List<string>();
                for (int c = 0; c < cols; c++) opParamsS2.Add($"lhs {op} rhs.c{c}");
                sb.AppendLine($"\tpublic static {structName} operator {op}({componentType} lhs, {structName} rhs) => new {structName}({string.Join(", ", opParamsS2)});");
                sb.AppendLine();
            }

            // Bitwise operators for integer types
            if (componentType == "int" || componentType == "uint" || componentType == "byte" || componentType == "ushort" || componentType == "ulong")
            {
                string[] bitwiseOps = ["&", "|", "^", "<<", ">>"];
                foreach (var op in bitwiseOps)
                {
                    if (op == "<<" || op == ">>")
                    {
                        // Shift operators only work with int as second operand
                        sb.AppendLine($"\t/// <summary>Returns the component-wise {op} of matrix by scalar.</summary>");
                        sb.AppendLine($"\t{Inline}");
                        var shiftParams = new List<string>();
                        for (int c = 0; c < cols; c++) shiftParams.Add($"lhs.c{c} {op} rhs");
                        sb.AppendLine($"\tpublic static {structName} operator {op}({structName} lhs, int rhs) => new {structName}({string.Join(", ", shiftParams)});");
                        sb.AppendLine();
                    }
                    else
                    {
                        // Regular bitwise operators
                        sb.AppendLine($"\t/// <summary>Returns the component-wise {op} of two matrices.</summary>");
                        sb.AppendLine($"\t{Inline}");
                        var bitwiseParamsM = new List<string>();
                        for (int c = 0; c < cols; c++) bitwiseParamsM.Add($"lhs.c{c} {op} rhs.c{c}");
                        sb.AppendLine($"\tpublic static {structName} operator {op}({structName} lhs, {structName} rhs) => new {structName}({string.Join(", ", bitwiseParamsM)});");
                        sb.AppendLine();
                    }
                }

                // Bitwise NOT
                sb.AppendLine($"\t/// <summary>Returns the component-wise bitwise NOT of the matrix.</summary>");
                sb.AppendLine($"\t{Inline}");
                var notParams = new List<string>();
                for (int c = 0; c < cols; c++) notParams.Add($"~val.c{c}");
                sb.AppendLine($"\tpublic static {structName} operator ~({structName} val) => new {structName}({string.Join(", ", notParams)});");
                sb.AppendLine();
            }

            // Unary operators
            if (IsSignedType(componentType))
            {
                // Negation
                sb.AppendLine($"\t/// <summary>Returns the component-wise negation of the matrix.</summary>");
                sb.AppendLine($"\t{Inline}");
                var opParamsNeg = new List<string>();
                for (int c = 0; c < cols; c++) opParamsNeg.Add($"-val.c{c}");
                sb.AppendLine($"\tpublic static {structName} operator -({structName} val) => new {structName}({string.Join(", ", opParamsNeg)});");
                sb.AppendLine();
            }

            // Comparison operators (return BoolNxM matrix)
            string[] compOps = ["<", "<=", ">", ">=", "==", "!="];
            string boolResultMatrixType = config.GetFullBoolMatrixType();

            foreach (var op in compOps)
            {
                // Matrix op Matrix
                sb.AppendLine($"\t/// <summary>Returns a {boolResultMatrixType} indicating component-wise {op} comparison.</summary>");
                sb.AppendLine($"\t{Inline}");
                var opParamsM = new List<string>();
                for (int c = 0; c < cols; c++) opParamsM.Add($"lhs.c{c} {op} rhs.c{c}");
                sb.AppendLine($"\tpublic static {boolResultMatrixType} operator {op}({structName} lhs, {structName} rhs) => new {boolResultMatrixType}({string.Join(", ", opParamsM)});");
                sb.AppendLine();

                // Matrix op Scalar
                sb.AppendLine($"\t/// <summary>Returns a {boolResultMatrixType} indicating component-wise {op} comparison with scalar.</summary>");
                sb.AppendLine($"\t{Inline}");
                var opParamsS1 = new List<string>();
                for (int c = 0; c < cols; c++) opParamsS1.Add($"lhs.c{c} {op} rhs");
                sb.AppendLine($"\tpublic static {boolResultMatrixType} operator {op}({structName} lhs, {componentType} rhs) => new {boolResultMatrixType}({string.Join(", ", opParamsS1)});");
                sb.AppendLine();

                // Scalar op Matrix
                sb.AppendLine($"\t/// <summary>Returns a {boolResultMatrixType} indicating component-wise {op} comparison with scalar.</summary>");
                sb.AppendLine($"\t{Inline}");
                var opParamsS2 = new List<string>();
                for (int c = 0; c < cols; c++) opParamsS2.Add($"lhs {op} rhs.c{c}");
                sb.AppendLine($"\tpublic static {boolResultMatrixType} operator {op}({componentType} lhs, {structName} rhs) => new {boolResultMatrixType}({string.Join(", ", opParamsS2)});");
                sb.AppendLine();
            }
        }
        else // Bool matrices
        {
            // Logical operators for bool matrices
            string[] logicalOps = ["&", "|", "^"];
            foreach (var op in logicalOps)
            {
                sb.AppendLine($"\t/// <summary>Returns the component-wise logical {op} of two matrices.</summary>");
                sb.AppendLine($"\t{Inline}");
                var opParams = new List<string>();
                for (int c = 0; c < cols; c++) opParams.Add($"lhs.c{c} {op} rhs.c{c}");
                sb.AppendLine($"\tpublic static {structName} operator {op}({structName} lhs, {structName} rhs) => new {structName}({string.Join(", ", opParams)});");
                sb.AppendLine();
            }

            // Logical NOT
            sb.AppendLine($"\t/// <summary>Returns the component-wise logical NOT of the matrix.</summary>");
            sb.AppendLine($"\t{Inline}");
            var notParams = new List<string>();
            for (int c = 0; c < cols; c++) notParams.Add($"!val.c{c}");
            sb.AppendLine($"\tpublic static {structName} operator !({structName} val) => new {structName}({string.Join(", ", notParams)});");
            sb.AppendLine();

            // Equality operators
            sb.AppendLine($"\t/// <summary>Returns true if all components are equal.</summary>");
            sb.AppendLine($"\t{Inline}");
            var equalClauses = new List<string>();
            for (int c = 0; c < cols; c++) equalClauses.Add($"lhs.c{c} == rhs.c{c}");
            sb.AppendLine($"\tpublic static bool operator ==({structName} lhs, {structName} rhs) => {string.Join(" && ", equalClauses)};");
            sb.AppendLine();

            sb.AppendLine($"\t/// <summary>Returns true if any component is not equal.</summary>");
            sb.AppendLine($"\t{Inline}");
            sb.AppendLine($"\tpublic static bool operator !=({structName} lhs, {structName} rhs) => !(lhs == rhs);");
            sb.AppendLine();
        }
    }

    private static void GenerateMatrixCrossTypeCasting(StringBuilder sb, MatrixConfig config)
    {
        if (!IsNumericType(config.PrimitiveType)) return;

        sb.AppendLine("\t// --- Cross-Type Casting Operators ---");

        var compatibleMatrices = s_matrixConfigs.Where(m =>
            m.PrimitiveType != config.PrimitiveType &&
            IsNumericType(m.PrimitiveType) &&
            m.Rows == config.Rows &&
            m.Columns == config.Columns);

        foreach (var otherMatrix in compatibleMatrices)
        {
            bool isWidening = IsWideningConversion(otherMatrix.PrimitiveType, config.PrimitiveType);
            string castKeyword = isWidening ? "implicit" : "explicit";

            sb.AppendLine($"\t/// <summary>{(isWidening ? "Implicitly" : "Explicitly")} converts a {otherMatrix.StructName} to a {config.StructName}.</summary>");
            sb.AppendLine($"\t{Inline}");
            sb.AppendLine($"\tpublic static {castKeyword} operator {config.StructName}({otherMatrix.StructName} m)");
            sb.AppendLine($"\t{{");

            // Generate column conversions
            var columnConversions = new List<string>();
            for (int c = 0; c < config.Columns; c++)
            {
                columnConversions.Add($"({config.GetColumnVectorType()})m.c{c}");
            }

            sb.AppendLine($"\t\treturn new {config.StructName}({string.Join(", ", columnConversions)});");
            sb.AppendLine($"\t}}");
            sb.AppendLine();
        }
    }

    private static void GenerateMatrixIndexer(StringBuilder sb, MatrixConfig config)
    {
        var structName = config.StructName;
        var componentType = config.PrimitiveType;
        var cols = config.Columns;
        var columnVectorType = config.GetColumnVectorType();

        sb.AppendLine($"\t/// <summary>Returns a reference to the {columnVectorType} (column) at a specified index.</summary>");
        sb.AppendLine($"\tunsafe public ref {columnVectorType} this[int index]"); // unsafe keyword for fixed and pointers
        sb.AppendLine($"\t{{");
        sb.AppendLine($"\t\t{Inline}");
        sb.AppendLine($"\t\tget");
        sb.AppendLine($"\t\t{{");
        sb.AppendLine($"\t\t\tif ((uint)index >= {cols})"); // Bounds check
        sb.AppendLine($"\t\t\t\tthrow new System.ArgumentOutOfRangeException(nameof(index), $\"Column index must be between 0 and {cols - 1}, but was {{index}}.\");");
        sb.AppendLine();
        // Get a pointer to the first column field (c0)
        // and then perform pointer arithmetic. This relies on fields c0, c1, ... being contiguous.
        sb.AppendLine($"\t\t\tfixed ({columnVectorType}* pC0 = &this.c0)");
        sb.AppendLine($"\t\t\t{{");
        sb.AppendLine($"\t\t\t\treturn ref pC0[index];");
        sb.AppendLine($"\t\t\t}}");
        sb.AppendLine($"\t\t}}");
        sb.AppendLine($"\t}}");
        sb.AppendLine();

        sb.AppendLine($"\t/// <summary>Returns the element at row and column indices.</summary>");
        sb.AppendLine($"\tpublic {componentType} this[int row, int column]");
        sb.AppendLine($"\t{{");
        sb.AppendLine($"\t\t{Inline}");
        sb.AppendLine($"\t\tget");
        sb.AppendLine($"\t\t{{");
        sb.AppendLine($"\t\t\tif ((uint)column >= {cols})");
        sb.AppendLine($"\t\t\t\tthrow new System.ArgumentOutOfRangeException(nameof(column));");
        sb.AppendLine($"\t\t\treturn this[column][row];");
        sb.AppendLine($"\t\t}}");
        sb.AppendLine($"\t\t{Inline}");
        sb.AppendLine($"\t\tset");
        sb.AppendLine($"\t\t{{");
        sb.AppendLine($"\t\t\tif ((uint)column >= {cols})");
        sb.AppendLine($"\t\t\t\tthrow new System.ArgumentOutOfRangeException(nameof(column));");
        sb.AppendLine($"\t\t\tvar temp = this[column];");
        sb.AppendLine($"\t\t\ttemp[row] = value;");
        sb.AppendLine($"\t\t\tthis[column] = temp;");
        sb.AppendLine($"\t\t}}");
        sb.AppendLine($"\t}}");
    }

    private static void GenerateMatrixStandardMethods(StringBuilder sb, MatrixConfig config)
    {
        var structName = config.StructName;
        var componentType = config.PrimitiveType;
        var rows = config.Rows;
        var cols = config.Columns;

        sb.AppendLine($"\t// --- Matrix Methods ---");

        if (componentType != "bool")
        {
            // Transpose property
            sb.AppendLine($"\t/// <summary>Gets the transpose of this matrix.</summary>");
            sb.AppendLine($"\tpublic {GetTransposeType(config)} Transpose");
            sb.AppendLine($"\t{{");
            sb.AppendLine($"\t\t{Inline}");
            sb.AppendLine($"\t\tget => Maths.Transpose(this);");
            sb.AppendLine($"\t}}");
            sb.AppendLine();
        }

        // Determinant (for square matrices)
        if (rows == cols && componentType != "bool" && componentType != "int" && componentType != "uint" && componentType != "byte" && componentType != "ushort" && componentType != "ulong")
        {
            sb.AppendLine($"\t/// <summary>Gets the determinant of this matrix.</summary>");
            sb.AppendLine($"\tpublic {componentType} Determinant");
            sb.AppendLine($"\t{{");
            sb.AppendLine($"\t\t{Inline}");
            sb.AppendLine($"\t\tget => Maths.Determinant(this);");
            sb.AppendLine($"\t}}");
            sb.AppendLine();
        }

        // Get/Set row methods
        for (int r = 0; r < rows; r++)
        {
            var rowVectorType = $"{config.Prefix}{cols}";

            sb.AppendLine($"\t/// <summary>Gets row {r} of the matrix.</summary>");
            sb.AppendLine($"\t{Inline}");
            sb.AppendLine($"\tpublic {rowVectorType} GetRow{r}()");
            sb.AppendLine($"\t{{");
            var rowComponents = new List<string>();
            for (int c = 0; c < cols; c++)
            {
                rowComponents.Add($"c{c}.{GetComponents(rows)[r]}");
            }
            sb.AppendLine($"\t\treturn new {rowVectorType}({string.Join(", ", rowComponents)});");
            sb.AppendLine($"\t}}");
            sb.AppendLine();

            sb.AppendLine($"\t/// <summary>Sets row {r} of the matrix.</summary>");
            sb.AppendLine($"\t{Inline}");
            sb.AppendLine($"\tpublic void SetRow{r}({rowVectorType} value)");
            sb.AppendLine($"\t{{");
            for (int c = 0; c < cols; c++)
            {
                sb.AppendLine($"\t\tc{c}.{GetComponents(rows)[r]} = value.{GetComponents(cols)[c]};");
            }
            sb.AppendLine($"\t}}");
            sb.AppendLine();
        }

        // Bool matrix specific methods
        if (componentType == "bool")
        {
            sb.AppendLine($"\t/// <summary>Returns true if any component is true.</summary>");
            sb.AppendLine($"\t{Inline}");
            sb.AppendLine($"\tpublic bool Any()");
            sb.AppendLine($"\t{{");
            var anyComponents = new List<string>();
            for (int c = 0; c < cols; c++)
            {
                anyComponents.Add($"c{c}.Any()");
            }
            sb.AppendLine($"\t\treturn {string.Join(" || ", anyComponents)};");
            sb.AppendLine($"\t}}");
            sb.AppendLine();

            sb.AppendLine($"\t/// <summary>Returns true if all components are true.</summary>");
            sb.AppendLine($"\t{Inline}");
            sb.AppendLine($"\tpublic bool All()");
            sb.AppendLine($"\t{{");
            var allComponents = new List<string>();
            for (int c = 0; c < cols; c++)
            {
                allComponents.Add($"c{c}.All()");
            }
            sb.AppendLine($"\t\treturn {string.Join(" && ", allComponents)};");
            sb.AppendLine($"\t}}");
            sb.AppendLine();
        }

        // ToArray
        sb.AppendLine($"\t/// <summary>Returns an array of components.</summary>");
        sb.AppendLine($"\t{Inline}");
        sb.AppendLine($"\tpublic {componentType}[] ToArray()");
        sb.AppendLine($"\t{{");
        sb.AppendLine($"\t\t{componentType}[] array = new {componentType}[{rows * cols}];");
        sb.AppendLine($"\t\tfor (int i = 0; i < {rows}; i++)");
        sb.AppendLine($"\t\t\tfor (int j = 0; j < {cols}; j++)");
        sb.AppendLine($"\t\t\t\tarray[i * {cols} + j] = this[i, j];");
        sb.AppendLine($"\t\treturn array;");
        sb.AppendLine($"\t}}");

        // Equals (strongly-typed)
        sb.AppendLine($"\t{Inline}");
        var equalsClauses = new List<string>();
        for (int c = 0; c < cols; c++) equalsClauses.Add($"this.c{c}.Equals(rhs.c{c})");
        sb.AppendLine($"\tpublic bool Equals({structName} rhs) {{ return {string.Join(" && ", equalsClauses)}; }}");
        sb.AppendLine();

        // Equals (object)
        sb.AppendLine($"\tpublic override bool Equals(object? o) {{ return o is {structName} converted && Equals(converted); }}");
        sb.AppendLine();

        // GetHashCode
        sb.AppendLine($"\t{Inline}");
        sb.AppendLine($"\tpublic override int GetHashCode()");
        sb.AppendLine($"\t{{");
        sb.AppendLine($"\t\tunchecked // Overflow is fine, just wrap");
        sb.AppendLine($"\t\t{{");
        sb.AppendLine($"\t\t\tint hash = 17;"); // Or GetType().GetHashCode();
        for (int c = 0; c < cols; c++)
        {
            sb.AppendLine($"\t\t\thash = hash * 23 + c{c}.GetHashCode();");
        }
        sb.AppendLine($"\t\t\treturn hash;");
        sb.AppendLine($"\t\t}}");
        sb.AppendLine($"\t}}");
        sb.AppendLine();

        // ToString
        sb.AppendLine($"\t{Inline}");
        sb.AppendLine($"\tpublic override string ToString() {{ return ToString(null, CultureInfo.CurrentCulture); }}");
        sb.AppendLine();

        sb.AppendLine($"\t{Inline}");
        sb.AppendLine($"\tpublic string ToString(string format) {{ return ToString(format, CultureInfo.CurrentCulture); }}");
        sb.AppendLine();

        sb.AppendLine($"\t{Inline}");
        sb.AppendLine($"\tpublic string ToString(string format, IFormatProvider formatProvider)");
        sb.AppendLine($"\t{{");
        sb.AppendLine($"\t\tStringBuilder sb = new StringBuilder();");
        sb.AppendLine($"\t\tsb.Append(\"{structName}(\");");

        bool isBool = componentType == "bool";

        // Output in row-major order for readability
        var componentAccessors = GetComponents(rows);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                sb.AppendLine($"\t\tsb.Append(this.c{c}.{componentAccessors[r]}.ToString({(isBool == false ? "format, " : "")}formatProvider));");
                if (!(r == rows - 1 && c == cols - 1)) // Not the last element
                {
                    sb.AppendLine($"\t\tsb.Append(\", \");");
                }
            }
            if (r < rows - 1) // Add visual break between rows (except for the last row)
            {
                sb.AppendLine($"\t\tsb.Append(\"  \");");
            }
        }
        sb.AppendLine($"\t\tsb.Append(\")\");");
        sb.AppendLine($"\t\treturn sb.ToString();");
        sb.AppendLine($"\t}}");
        sb.AppendLine();
    }

    private static void GenerateMatrixNumericsConversions(StringBuilder sb, MatrixConfig config)
    {
        if (!IsSystemNumericsMatrixCompatible(config))
            return;

        string numericsType = "";
        if (config.Rows == 4 && config.Columns == 4)
            numericsType = "System.Numerics.Matrix4x4";
        else if (config.Rows == 3 && config.Columns == 2)
            numericsType = "System.Numerics.Matrix3x2";
        else
            return; // Should not happen due to the check above

        sb.AppendLine($"\t// --- System.Numerics Conversions ---");

        // To System.Numerics
        sb.AppendLine($"\t/// <summary>Implicitly converts this {config.StructName} to a {numericsType}.</summary>");
        sb.AppendLine($"\t{Inline}");
        sb.AppendLine($"\tpublic static implicit operator {numericsType}({config.StructName} m)");
        sb.AppendLine($"\t{{");
        if (numericsType.EndsWith("Matrix4x4"))
        {
            sb.AppendLine($"\t\treturn new {numericsType}(");
            sb.AppendLine($"\t\t\tm.c0.X, m.c1.X, m.c2.X, m.c3.X,");
            sb.AppendLine($"\t\t\tm.c0.Y, m.c1.Y, m.c2.Y, m.c3.Y,");
            sb.AppendLine($"\t\t\tm.c0.Z, m.c1.Z, m.c2.Z, m.c3.Z,");
            sb.AppendLine($"\t\t\tm.c0.W, m.c1.W, m.c2.W, m.c3.W);");
        }
        else // Matrix3x2
        {
            sb.AppendLine($"\t\treturn new {numericsType}(");
            sb.AppendLine($"\t\t\tm.c0.X, m.c1.X,");
            sb.AppendLine($"\t\t\tm.c0.Y, m.c1.Y,");
            sb.AppendLine($"\t\t\tm.c0.Z, m.c1.Z);");
        }
        sb.AppendLine($"\t}}");
        sb.AppendLine();

        // From System.Numerics
        sb.AppendLine($"\t/// <summary>Implicitly converts a {numericsType} to this {config.StructName}.</summary>");
        sb.AppendLine($"\t{Inline}");
        sb.AppendLine($"\tpublic static implicit operator {config.StructName}({numericsType} m)");
        sb.AppendLine($"\t{{");
        if (numericsType.EndsWith("Matrix4x4"))
        {
            sb.AppendLine($"\t\treturn new {config.StructName}(");
            sb.AppendLine($"\t\t\tm.M11, m.M12, m.M13, m.M14,");
            sb.AppendLine($"\t\t\tm.M21, m.M22, m.M23, m.M24,");
            sb.AppendLine($"\t\t\tm.M31, m.M32, m.M33, m.M34,");
            sb.AppendLine($"\t\t\tm.M41, m.M42, m.M43, m.M44);");
        }
        else // Matrix3x2
        {
            // Our constructor takes row-major components, so this mapping is direct.
            sb.AppendLine($"\t\treturn new {config.StructName}(");
            sb.AppendLine($"\t\t\tm.M11, m.M12,");
            sb.AppendLine($"\t\t\tm.M21, m.M22,");
            sb.AppendLine($"\t\t\tm.M31, m.M32);");
        }
        sb.AppendLine($"\t}}");
        sb.AppendLine();
    }


    private static string GetTransposeType(MatrixConfig config)
    {
        // For MxN matrix, transpose is NxM
        return $"{config.Prefix}{config.Columns}x{config.Rows}";
    }

    private static string GetEpsilonValue(string type)
    {
        return type switch
        {
            "float" => "1e-6f",
            "double" => "1e-14",
            _ => "0"
        };
    }

    #endregion

    #region Template Files

    private static void GenerateTemplates(string outputDirectory)
    {
        string templatesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Templates");

        if (!Directory.Exists(templatesDirectory))
        {
            Console.WriteLine($"Templates directory not found: {templatesDirectory}");
            return;
        }

        Console.WriteLine("\nGenerating templates...");

        // Discover all template directories
        var templateDirs = Directory.GetDirectories(templatesDirectory, "Prowl.Vector.*", SearchOption.TopDirectoryOnly);

        foreach (var templateDir in templateDirs)
        {
            string namespaceSuffix = Path.GetFileName(templateDir).Substring("Prowl.Vector.".Length);
            string outputSubDir = Path.Combine(outputDirectory, namespaceSuffix);

            ProcessTemplateDirectory(templateDir, namespaceSuffix, outputSubDir);
        }
    }

    private static void ProcessTemplateDirectory(string templateDir, string namespaceSuffix, string outputDir)
    {
        var templateFiles = Directory.GetFiles(templateDir, "*.template", SearchOption.TopDirectoryOnly);

        foreach (var templateFile in templateFiles)
        {
            string templateName = Path.GetFileNameWithoutExtension(templateFile);
            string templateContent = File.ReadAllText(templateFile);

            // Parse template header to determine supported types
            var supportedTypes = ParseTemplateHeader(templateContent);

            foreach (var typeName in supportedTypes)
            {
                if (s_typeMappings.TryGetValue(typeName, out var typeMapping))
                {
                    string generatedContent = ProcessTemplate(templateContent, typeMapping, namespaceSuffix, templateName);
                    string outputFileName = $"{templateName}{typeMapping.TemplateSuffix}.cs";
                    string outputPath = Path.Combine(outputDir, outputFileName);

                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                    File.WriteAllText(outputPath, generatedContent, Encoding.UTF8);

                    Console.WriteLine($"Generated: {outputFileName}");
                }
            }
        }
    }

    private static string[] ParseTemplateHeader(string templateContent)
    {
        // Look for a header comment like: // TEMPLATE_TYPES: float, double
        var lines = templateContent.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("// TEMPLATE_TYPES:"))
            {
                var typesString = trimmed.Substring("// TEMPLATE_TYPES:".Length).Trim();
                return typesString.Split(',').Select(t => t.Trim()).ToArray();
            }
        }

        // Default to float and double if no header found
        return new[] { "float", "double" };
    }

    private static string ApplyTypePreprocessor(string templateContent, TemplateTypeMapping typeMapping)
    {
        var lines = templateContent.Split('\n');
        var output = new List<string>();
        bool include = true;
        var stack = new Stack<bool>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("##IF "))
            {
                var condition = trimmed.Substring("##IF ".Length).Trim();
                bool matches = string.Equals(condition, typeMapping.TypePrefix, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(condition, typeMapping.TypeName, StringComparison.OrdinalIgnoreCase);
                stack.Push(include);
                include = include && matches;
            }
            else if (trimmed == "##ELSE")
            {
                if (stack.Count > 0)
                {
                    bool parent = stack.Peek();
                    include = parent && !include;
                }
            }
            else if (trimmed == "##ENDIF")
            {
                if (stack.Count > 0)
                {
                    include = stack.Pop();
                }
            }
            else if (include)
            {
                output.Add(line);
            }
        }

        return string.Join('\n', output);
    }

    private static string ProcessTemplate(string templateContent, TemplateTypeMapping typeMapping, string namespaceSuffix, string templateName)
    {
        var result = ApplyTypePreprocessor(templateContent, typeMapping);

        // Add auto-generated header
        var header = new StringBuilder();
        AddHeader(header);

        // Replace template placeholders
        result = result.Replace("{{TYPE}}", typeMapping.TypeName);
        result = result.Replace("{{TYPE_PREFIX}}", typeMapping.TypePrefix);
        result = result.Replace("{{CLASS_NAME}}", $"{templateName}{typeMapping.TemplateSuffix}");
        result = result.Replace("{{ZERO}}", typeMapping.ZeroValue);
        result = result.Replace("{{ONE}}", typeMapping.OneValue);
        result = result.Replace("{{TWO}}", typeMapping.TwoValue);
        result = result.Replace("{{EPSILON}}", typeMapping.EpsilonValue);
        result = result.Replace("{{NAMESPACE_SUFFIX}}", namespaceSuffix);

        // Replace vector type references
        result = result.Replace("{{TYPE_PREFIX}}2", $"{typeMapping.TypePrefix}2");
        result = result.Replace("{{TYPE_PREFIX}}3", $"{typeMapping.TypePrefix}3");
        result = result.Replace("{{TYPE_PREFIX}}4", $"{typeMapping.TypePrefix}4");
        result = result.Replace("{{TYPE_PREFIX}}4x4", $"{typeMapping.TypePrefix}4x4");

        // Replace template type references (other generated template types)
        result = result.Replace("{{TEMPLATE_SUFFIX}}", typeMapping.TemplateSuffix);

        // Remove template header line
        var lines = result.Split('\n');
        var filteredLines = lines.Where(line => !line.Trim().StartsWith("// TEMPLATE_TYPES:")).ToArray();
        result = string.Join('\n', filteredLines);

        // Prepend auto-generated header
        return header.ToString() + result;
    }

    #endregion

    private static string[] GetComponents(int dimension)
    {
        if (dimension == 2) return ["X", "Y"];
        if (dimension == 3) return ["X", "Y", "Z"];
        if (dimension == 4) return ["X", "Y", "Z", "W"];
        throw new ArgumentException($"Unsupported dimension: {dimension}");
    }
}
