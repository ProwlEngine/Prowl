using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Prowl.Echo.SourceGenerator;

[Generator]
public class SerializerGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Add the attribute to the compilation
        context.RegisterPostInitializationOutput(ctx => ctx.AddSource(
            "GenerateSerializerAttribute.g.cs",
            SourceText.From(AttributeSource, Encoding.UTF8)));

        // Find all types with the [GenerateSerializer] attribute
        var typesToGenerate = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Prowl.Echo.GenerateSerializerAttribute",
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (ctx, _) => GetTypeToGenerate(ctx))
            .Where(static type => type is not null);

        // Generate the source for each type
        context.RegisterSourceOutput(typesToGenerate, static (spc, typeInfo) =>
        {
            if (typeInfo is null) return;
            var source = GenerateSerializerSource(typeInfo);
            spc.AddSource($"{typeInfo.FullTypeName}.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }

    private static TypeToGenerate? GetTypeToGenerate(GeneratorAttributeSyntaxContext context)
    {
        var typeSymbol = context.TargetSymbol as INamedTypeSymbol;
        if (typeSymbol is null) return null;

        var typeDeclaration = context.TargetNode as TypeDeclarationSyntax;
        if (typeDeclaration is null) return null;

        // Check if type has FixedEchoStructure attribute
        var fixedStructureAttr = context.SemanticModel.Compilation.GetTypeByMetadataName("Prowl.Echo.FixedEchoStructureAttribute");
        bool isFixedStructure = typeSymbol.GetAttributes().Any(a =>
            SymbolEqualityComparer.Default.Equals(a.AttributeClass, fixedStructureAttr));

        // Get all fields that should be serialized
        var fields = GetSerializableFields(typeSymbol, context.SemanticModel.Compilation);

        return new TypeToGenerate(
            TypeName: typeSymbol.Name,
            FullTypeName: typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""),
            Namespace: typeSymbol.ContainingNamespace?.ToDisplayString(),
            IsPartial: typeDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)),
            IsStruct: typeSymbol.TypeKind == TypeKind.Struct,
            IsFixedStructure: isFixedStructure,
            Fields: fields
        );
    }

    private static List<FieldToSerialize> GetSerializableFields(INamedTypeSymbol typeSymbol, Compilation compilation)
    {
        var fields = new List<FieldToSerialize>();

        // Get attribute symbols for checking
        var serializeIgnoreAttr = compilation.GetTypeByMetadataName("Prowl.Echo.SerializeIgnoreAttribute");
        var nonSerializedAttr = compilation.GetTypeByMetadataName("System.NonSerializedAttribute");
        var serializeFieldAttr = compilation.GetTypeByMetadataName("Prowl.Echo.SerializeFieldAttribute");
        var ignoreOnNullAttr = compilation.GetTypeByMetadataName("Prowl.Echo.IgnoreOnNullAttribute");
        var serializeIfAttr = compilation.GetTypeByMetadataName("Prowl.Echo.SerializeIfAttribute");
        var formerlySerializedAsAttr = compilation.GetTypeByMetadataName("Prowl.Echo.FormerlySerializedAsAttribute");

        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is not IFieldSymbol field) continue;

            // Skip const, static, and readonly fields
            if (field.IsConst || field.IsStatic || field.IsReadOnly) continue;

            // Check if field should be ignored
            bool hasSerializeIgnore = field.GetAttributes().Any(a =>
                SymbolEqualityComparer.Default.Equals(a.AttributeClass, serializeIgnoreAttr));
            bool hasNonSerialized = field.GetAttributes().Any(a =>
                SymbolEqualityComparer.Default.Equals(a.AttributeClass, nonSerializedAttr));

            if (hasSerializeIgnore || hasNonSerialized) continue;

            // Check if field should be serialized
            bool hasSerializeField = field.GetAttributes().Any(a =>
                SymbolEqualityComparer.Default.Equals(a.AttributeClass, serializeFieldAttr));
            bool isPublic = field.DeclaredAccessibility == Accessibility.Public;

            // Only serialize public fields or private fields with [SerializeField]
            if (!isPublic && !hasSerializeField) continue;

            // Get additional attributes
            bool hasIgnoreOnNull = field.GetAttributes().Any(a =>
                SymbolEqualityComparer.Default.Equals(a.AttributeClass, ignoreOnNullAttr));

            var serializeIfAttrs = field.GetAttributes()
                .Where(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, serializeIfAttr))
                .Select(a => a.ConstructorArguments.FirstOrDefault().Value?.ToString())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            var formerlySerializedAs = field.GetAttributes()
                .Where(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, formerlySerializedAsAttr))
                .Select(a => a.ConstructorArguments.FirstOrDefault().Value?.ToString())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            // Classify field type for inline serialization
            var category = ClassifyFieldType(field.Type);
            string? elementTypeName = null;
            var elementCategory = FieldTypeCategory.Fallback;

            switch (category)
            {
                case FieldTypeCategory.ListOfKnown:
                {
                    var listType = (INamedTypeSymbol)field.Type;
                    var elemType = listType.TypeArguments[0];
                    elementTypeName = elemType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "");
                    elementCategory = ClassifyFieldType(elemType);
                    break;
                }
                case FieldTypeCategory.ArrayOfKnown:
                {
                    var arrType = (IArrayTypeSymbol)field.Type;
                    elementTypeName = arrType.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "");
                    elementCategory = ClassifyFieldType(arrType.ElementType);
                    break;
                }
                case FieldTypeCategory.DictStringToKnown:
                {
                    var dictType = (INamedTypeSymbol)field.Type;
                    var valType = dictType.TypeArguments[1];
                    elementTypeName = valType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "");
                    elementCategory = ClassifyFieldType(valType);
                    break;
                }
            }

            fields.Add(new FieldToSerialize(
                Name: field.Name,
                TypeName: field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""),
                HasIgnoreOnNull: hasIgnoreOnNull,
                SerializeIfConditions: serializeIfAttrs!,
                FormerlySerializedAs: formerlySerializedAs!,
                Category: category,
                ElementTypeName: elementTypeName,
                ElementCategory: elementCategory
            ));
        }

        return fields;
    }

    #region Type Classification

    private enum FieldTypeCategory
    {
        Fallback,
        Byte, SByte, Short, UShort, Int, UInt, Long, ULong,
        Float, Double, Decimal,
        Bool, Char,
        String, ByteArray,
        Enum,
        DateTime, Guid, TimeSpan,
        ListOfKnown, ArrayOfKnown, DictStringToKnown,
    }

    private static FieldTypeCategory ClassifyFieldType(ITypeSymbol type)
    {
        // Nullable<T> → always Fallback (complex wrapping format)
        if (type is INamedTypeSymbol namedNullable &&
            namedNullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return FieldTypeCategory.Fallback;

        // Primitives via SpecialType
        switch (type.SpecialType)
        {
            case SpecialType.System_Byte: return FieldTypeCategory.Byte;
            case SpecialType.System_SByte: return FieldTypeCategory.SByte;
            case SpecialType.System_Int16: return FieldTypeCategory.Short;
            case SpecialType.System_UInt16: return FieldTypeCategory.UShort;
            case SpecialType.System_Int32: return FieldTypeCategory.Int;
            case SpecialType.System_UInt32: return FieldTypeCategory.UInt;
            case SpecialType.System_Int64: return FieldTypeCategory.Long;
            case SpecialType.System_UInt64: return FieldTypeCategory.ULong;
            case SpecialType.System_Single: return FieldTypeCategory.Float;
            case SpecialType.System_Double: return FieldTypeCategory.Double;
            case SpecialType.System_Decimal: return FieldTypeCategory.Decimal;
            case SpecialType.System_Boolean: return FieldTypeCategory.Bool;
            case SpecialType.System_Char: return FieldTypeCategory.Char;
            case SpecialType.System_String: return FieldTypeCategory.String;
        }

        // byte[] (must check before general array)
        if (type is IArrayTypeSymbol byteArr && byteArr.Rank == 1 &&
            byteArr.ElementType.SpecialType == SpecialType.System_Byte)
            return FieldTypeCategory.ByteArray;

        // Enum
        if (type.TypeKind == TypeKind.Enum)
            return FieldTypeCategory.Enum;

        // DateTime, Guid, TimeSpan
        if (type.ContainingNamespace?.ToDisplayString() == "System")
        {
            switch (type.Name)
            {
                case "DateTime": return FieldTypeCategory.DateTime;
                case "Guid": return FieldTypeCategory.Guid;
                case "TimeSpan": return FieldTypeCategory.TimeSpan;
            }
        }

        // List<T> where T is a simple element type
        if (type is INamedTypeSymbol listType &&
            listType.IsGenericType &&
            listType.TypeArguments.Length == 1 &&
            listType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>")
        {
            var elemCat = ClassifyFieldType(listType.TypeArguments[0]);
            if (IsSimpleElementCategory(elemCat))
                return FieldTypeCategory.ListOfKnown;
        }

        // T[] where T is a simple element type (rank-1 only, byte[] already handled)
        if (type is IArrayTypeSymbol arrType && arrType.Rank == 1)
        {
            var elemCat = ClassifyFieldType(arrType.ElementType);
            if (IsSimpleElementCategory(elemCat))
                return FieldTypeCategory.ArrayOfKnown;
        }

        // Dictionary<string, T> where T is a simple element type
        if (type is INamedTypeSymbol dictType &&
            dictType.IsGenericType &&
            dictType.TypeArguments.Length == 2 &&
            dictType.TypeArguments[0].SpecialType == SpecialType.System_String &&
            dictType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.Dictionary<TKey, TValue>")
        {
            var valCat = ClassifyFieldType(dictType.TypeArguments[1]);
            if (IsSimpleElementCategory(valCat))
                return FieldTypeCategory.DictStringToKnown;
        }

        return FieldTypeCategory.Fallback;
    }

    private static bool IsSimpleElementCategory(FieldTypeCategory cat) => cat switch
    {
        FieldTypeCategory.Byte or FieldTypeCategory.SByte or
        FieldTypeCategory.Short or FieldTypeCategory.UShort or
        FieldTypeCategory.Int or FieldTypeCategory.UInt or
        FieldTypeCategory.Long or FieldTypeCategory.ULong or
        FieldTypeCategory.Float or FieldTypeCategory.Double or
        FieldTypeCategory.Decimal or FieldTypeCategory.Bool or
        FieldTypeCategory.Char or FieldTypeCategory.String or
        FieldTypeCategory.Enum or FieldTypeCategory.Guid => true,
        _ => false,
    };

    #endregion

    #region Inline Serialization Helpers

    private static string GetValueAccessor(FieldTypeCategory cat) => cat switch
    {
        FieldTypeCategory.Byte => "ByteValue",
        FieldTypeCategory.SByte => "sByteValue",
        FieldTypeCategory.Short => "ShortValue",
        FieldTypeCategory.UShort => "UShortValue",
        FieldTypeCategory.Int => "IntValue",
        FieldTypeCategory.UInt => "UIntValue",
        FieldTypeCategory.Long => "LongValue",
        FieldTypeCategory.ULong => "ULongValue",
        FieldTypeCategory.Float => "FloatValue",
        FieldTypeCategory.Double => "DoubleValue",
        FieldTypeCategory.Decimal => "DecimalValue",
        FieldTypeCategory.Bool => "BoolValue",
        _ => throw new System.ArgumentException($"No value accessor for {cat}")
    };

    /// <summary>
    /// Returns a C# expression that serializes <paramref name="expr"/> into an EchoObject.
    /// Only works for simple categories (not DateTime, TimeSpan, or collections).
    /// </summary>
    private static string GetSerializeExpression(FieldTypeCategory cat, string expr) => cat switch
    {
        FieldTypeCategory.Byte or FieldTypeCategory.SByte or
        FieldTypeCategory.Short or FieldTypeCategory.UShort or
        FieldTypeCategory.Int or FieldTypeCategory.UInt or
        FieldTypeCategory.Long or FieldTypeCategory.ULong or
        FieldTypeCategory.Float or FieldTypeCategory.Double or
        FieldTypeCategory.Decimal or FieldTypeCategory.Bool
            => $"new EchoObject({expr})",
        FieldTypeCategory.Char => $"new EchoObject((byte){expr})",
        FieldTypeCategory.String => $"({expr} != null ? new EchoObject({expr}) : new EchoObject())",
        FieldTypeCategory.ByteArray => $"({expr} != null ? new EchoObject({expr}) : new EchoObject())",
        FieldTypeCategory.Enum => $"new EchoObject(EchoType.Int, (int){expr})",
        FieldTypeCategory.Guid => $"new EchoObject(EchoType.String, {expr}.ToString())",
        _ => throw new System.ArgumentException($"No serialize expression for {cat}")
    };

    /// <summary>
    /// Returns a C# expression that deserializes an EchoObject <paramref name="expr"/> to the target type.
    /// Works for simple categories + DateTime, TimeSpan, Guid.
    /// </summary>
    private static string GetDeserializeExpression(FieldTypeCategory cat, string expr, string typeName) => cat switch
    {
        FieldTypeCategory.Byte or FieldTypeCategory.SByte or
        FieldTypeCategory.Short or FieldTypeCategory.UShort or
        FieldTypeCategory.Int or FieldTypeCategory.UInt or
        FieldTypeCategory.Long or FieldTypeCategory.ULong or
        FieldTypeCategory.Float or FieldTypeCategory.Double or
        FieldTypeCategory.Decimal or FieldTypeCategory.Bool
            => $"{expr}.{GetValueAccessor(cat)}",
        FieldTypeCategory.Char => $"(char){expr}.ByteValue",
        FieldTypeCategory.String => $"({expr}.TagType != EchoType.Null ? {expr}.StringValue : null)",
        FieldTypeCategory.ByteArray => $"({expr}.TagType != EchoType.Null ? {expr}.ByteArrayValue : null)",
        FieldTypeCategory.Enum => $"({typeName}){expr}.IntValue",
        FieldTypeCategory.DateTime => $"System.DateTime.FromBinary({expr}.Get(\"date\")!.LongValue)",
        FieldTypeCategory.TimeSpan => $"new System.TimeSpan({expr}.Get(\"ticks\")!.LongValue)",
        FieldTypeCategory.Guid => $"System.Guid.Parse({expr}.StringValue)",
        _ => throw new System.ArgumentException($"No deserialize expression for {cat}")
    };

    /// <summary>
    /// Emits setup code (if needed) and returns an expression or variable name
    /// holding the serialized EchoObject for a field.
    /// </summary>
    private static string EmitSerializeExpr(StringBuilder sb, FieldToSerialize field, string indent)
    {
        switch (field.Category)
        {
            // Simple single-expression categories
            case FieldTypeCategory.Byte: case FieldTypeCategory.SByte:
            case FieldTypeCategory.Short: case FieldTypeCategory.UShort:
            case FieldTypeCategory.Int: case FieldTypeCategory.UInt:
            case FieldTypeCategory.Long: case FieldTypeCategory.ULong:
            case FieldTypeCategory.Float: case FieldTypeCategory.Double:
            case FieldTypeCategory.Decimal: case FieldTypeCategory.Bool:
            case FieldTypeCategory.Char:
            case FieldTypeCategory.String: case FieldTypeCategory.ByteArray:
            case FieldTypeCategory.Enum: case FieldTypeCategory.Guid:
                return GetSerializeExpression(field.Category, field.Name);

            // DateTime → compound with "date" key (matches DateTimeFormat)
            case FieldTypeCategory.DateTime:
            {
                var v = $"_s_{field.Name}";
                sb.AppendLine($"{indent}var {v} = EchoObject.NewCompound();");
                sb.AppendLine($"{indent}{v}.Add(\"date\", new EchoObject(EchoType.Long, {field.Name}.ToBinary()));");
                return v;
            }

            // TimeSpan → compound with "ticks" key (matches TimeSpanFormat)
            case FieldTypeCategory.TimeSpan:
            {
                var v = $"_s_{field.Name}";
                sb.AppendLine($"{indent}var {v} = EchoObject.NewCompound();");
                sb.AppendLine($"{indent}{v}.Add(\"ticks\", new EchoObject(EchoType.Long, {field.Name}.Ticks));");
                return v;
            }

            // List<T> → bare EchoType.List (matches ListFormat)
            case FieldTypeCategory.ListOfKnown:
            {
                var v = $"_s_{field.Name}";
                var elemExpr = GetSerializeExpression(field.ElementCategory, "_item");
                sb.AppendLine($"{indent}EchoObject {v};");
                sb.AppendLine($"{indent}if ({field.Name} != null)");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    {v} = EchoObject.NewList();");
                sb.AppendLine($"{indent}    foreach (var _item in {field.Name})");
                sb.AppendLine($"{indent}        {v}.ListAdd({elemExpr});");
                sb.AppendLine($"{indent}}}");
                sb.AppendLine($"{indent}else");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    {v} = new EchoObject();");
                sb.AppendLine($"{indent}}}");
                return v;
            }

            // T[] → compound with "array" key (matches ArrayFormat)
            case FieldTypeCategory.ArrayOfKnown:
            {
                var v = $"_s_{field.Name}";
                var arrList = $"_arrList_{field.Name}";
                var elemExpr = GetSerializeExpression(field.ElementCategory, "_item");
                sb.AppendLine($"{indent}EchoObject {v};");
                sb.AppendLine($"{indent}if ({field.Name} != null)");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    {v} = EchoObject.NewCompound();");
                sb.AppendLine($"{indent}    var {arrList} = EchoObject.NewList();");
                sb.AppendLine($"{indent}    foreach (var _item in {field.Name})");
                sb.AppendLine($"{indent}        {arrList}.ListAdd({elemExpr});");
                sb.AppendLine($"{indent}    {v}.Add(\"array\", {arrList});");
                sb.AppendLine($"{indent}}}");
                sb.AppendLine($"{indent}else");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    {v} = new EchoObject();");
                sb.AppendLine($"{indent}}}");
                return v;
            }

            // Dictionary<string, T> → bare compound (matches DictionaryFormat for string keys)
            case FieldTypeCategory.DictStringToKnown:
            {
                var v = $"_s_{field.Name}";
                var valExpr = GetSerializeExpression(field.ElementCategory, "_kvp.Value");
                sb.AppendLine($"{indent}EchoObject {v};");
                sb.AppendLine($"{indent}if ({field.Name} != null)");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    {v} = EchoObject.NewCompound();");
                sb.AppendLine($"{indent}    foreach (var _kvp in {field.Name})");
                sb.AppendLine($"{indent}        {v}.Add(_kvp.Key, {valExpr});");
                sb.AppendLine($"{indent}}}");
                sb.AppendLine($"{indent}else");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    {v} = new EchoObject();");
                sb.AppendLine($"{indent}}}");
                return v;
            }

            // Fallback → existing Serializer.Serialize call
            case FieldTypeCategory.Fallback:
            default:
                return $"Serializer.Serialize(typeof({field.TypeName}), {field.Name}, ctx)";
        }
    }

    /// <summary>
    /// Emits code to deserialize from <paramref name="sourceExpr"/> and assign to the field.
    /// </summary>
    private static void EmitDeserializeAssignment(StringBuilder sb, FieldToSerialize field, string sourceExpr, string indent)
    {
        switch (field.Category)
        {
            // Value types — direct accessor
            case FieldTypeCategory.Byte: case FieldTypeCategory.SByte:
            case FieldTypeCategory.Short: case FieldTypeCategory.UShort:
            case FieldTypeCategory.Int: case FieldTypeCategory.UInt:
            case FieldTypeCategory.Long: case FieldTypeCategory.ULong:
            case FieldTypeCategory.Float: case FieldTypeCategory.Double:
            case FieldTypeCategory.Decimal: case FieldTypeCategory.Bool:
            case FieldTypeCategory.Char:
            case FieldTypeCategory.Enum:
            case FieldTypeCategory.DateTime: case FieldTypeCategory.TimeSpan:
            case FieldTypeCategory.Guid:
                sb.AppendLine($"{indent}{field.Name} = {GetDeserializeExpression(field.Category, sourceExpr, field.TypeName)};");
                break;

            // Reference types with null handling — add ! to suppress nullable warning
            case FieldTypeCategory.String:
            case FieldTypeCategory.ByteArray:
                sb.AppendLine($"{indent}{field.Name} = {GetDeserializeExpression(field.Category, sourceExpr, field.TypeName)}!;");
                break;

            // List<T> → iterate bare list (matches ListFormat)
            case FieldTypeCategory.ListOfKnown:
            {
                var elemDeser = GetDeserializeExpression(field.ElementCategory, "_item", field.ElementTypeName!);
                sb.AppendLine($"{indent}if ({sourceExpr}.TagType != EchoType.Null)");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    {field.Name} = new System.Collections.Generic.List<{field.ElementTypeName}>();");
                sb.AppendLine($"{indent}    foreach (var _item in {sourceExpr}.List)");
                sb.AppendLine($"{indent}        {field.Name}.Add({elemDeser});");
                sb.AppendLine($"{indent}}}");
                sb.AppendLine($"{indent}else");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    {field.Name} = null!;");
                sb.AppendLine($"{indent}}}");
                break;
            }

            // T[] → read from compound "array" key (matches ArrayFormat)
            case FieldTypeCategory.ArrayOfKnown:
            {
                var arrData = $"_arrData_{field.Name}";
                var elemDeser = GetDeserializeExpression(field.ElementCategory, $"{arrData}.List[_i]", field.ElementTypeName!);
                sb.AppendLine($"{indent}if ({sourceExpr}.TagType != EchoType.Null && {sourceExpr}.TryGet(\"array\", out var {arrData}))");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    {field.Name} = new {field.ElementTypeName}[{arrData}.List.Count];");
                sb.AppendLine($"{indent}    for (int _i = 0; _i < {arrData}.List.Count; _i++)");
                sb.AppendLine($"{indent}        {field.Name}[_i] = {elemDeser};");
                sb.AppendLine($"{indent}}}");
                sb.AppendLine($"{indent}else");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    {field.Name} = null!;");
                sb.AppendLine($"{indent}}}");
                break;
            }

            // Dictionary<string, T> → iterate compound tags (matches DictionaryFormat for string keys)
            case FieldTypeCategory.DictStringToKnown:
            {
                var valDeser = GetDeserializeExpression(field.ElementCategory, "_kvp.Value", field.ElementTypeName!);
                sb.AppendLine($"{indent}if ({sourceExpr}.TagType != EchoType.Null)");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    {field.Name} = new System.Collections.Generic.Dictionary<string, {field.ElementTypeName}>();");
                sb.AppendLine($"{indent}    foreach (var _kvp in {sourceExpr}.Tags)");
                sb.AppendLine($"{indent}        {field.Name}.Add(_kvp.Key, {valDeser});");
                sb.AppendLine($"{indent}}}");
                sb.AppendLine($"{indent}else");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    {field.Name} = null!;");
                sb.AppendLine($"{indent}}}");
                break;
            }

            // Fallback → existing Serializer.Deserialize call
            case FieldTypeCategory.Fallback:
            default:
                sb.AppendLine($"{indent}{field.Name} = ({field.TypeName})Serializer.Deserialize({sourceExpr}, typeof({field.TypeName}), ctx)!;");
                break;
        }
    }

    #endregion

    #region Code Generation

    private static string GenerateSerializerSource(TypeToGenerate typeInfo)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using Prowl.Echo;");
        sb.AppendLine();

        // Add namespace if present
        if (!string.IsNullOrEmpty(typeInfo.Namespace))
        {
            sb.AppendLine($"namespace {typeInfo.Namespace}");
            sb.AppendLine("{");
        }

        // Generate the partial class/struct
        var keyword = typeInfo.IsStruct ? "struct" : "class";
        var indent = string.IsNullOrEmpty(typeInfo.Namespace) ? "" : "    ";

        sb.AppendLine($"{indent}partial {keyword} {typeInfo.TypeName} : ISerializable");
        sb.AppendLine($"{indent}{{");

        // Generate Serialize method
        if (typeInfo.IsFixedStructure)
            GenerateFixedStructureSerializeMethod(sb, typeInfo, indent + "    ");
        else
            GenerateSerializeMethod(sb, typeInfo, indent + "    ");

        sb.AppendLine();

        // Generate Deserialize method
        if (typeInfo.IsFixedStructure)
            GenerateFixedStructureDeserializeMethod(sb, typeInfo, indent + "    ");
        else
            GenerateDeserializeMethod(sb, typeInfo, indent + "    ");

        sb.AppendLine($"{indent}}}");

        if (!string.IsNullOrEmpty(typeInfo.Namespace))
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private static void GenerateSerializeMethod(StringBuilder sb, TypeToGenerate typeInfo, string indent)
    {
        sb.AppendLine($"{indent}public void Serialize(ref EchoObject compound, SerializationContext ctx)");
        sb.AppendLine($"{indent}{{");

        foreach (var field in typeInfo.Fields)
        {
            bool needsNullCheck = field.HasIgnoreOnNull;
            bool hasSerializeIf = field.SerializeIfConditions.Count > 0;

            if (needsNullCheck || hasSerializeIf)
            {
                var conditions = new List<string>();
                if (needsNullCheck)
                    conditions.Add($"{field.Name} != null");
                foreach (var condition in field.SerializeIfConditions)
                    conditions.Add(condition);

                sb.AppendLine($"{indent}    if ({string.Join(" && ", conditions)})");
                sb.AppendLine($"{indent}    {{");
                var expr = EmitSerializeExpr(sb, field, indent + "        ");
                sb.AppendLine($"{indent}        compound.Add(\"{field.Name}\", {expr});");
                sb.AppendLine($"{indent}    }}");
            }
            else
            {
                var expr = EmitSerializeExpr(sb, field, indent + "    ");
                sb.AppendLine($"{indent}    compound.Add(\"{field.Name}\", {expr});");
            }
        }

        sb.AppendLine($"{indent}}}");
    }

    private static void GenerateDeserializeMethod(StringBuilder sb, TypeToGenerate typeInfo, string indent)
    {
        sb.AppendLine($"{indent}public void Deserialize(EchoObject value, SerializationContext ctx)");
        sb.AppendLine($"{indent}{{");

        foreach (var field in typeInfo.Fields)
        {
            var varName = $"_{field.Name}";

            // Try to deserialize from current field name
            sb.AppendLine($"{indent}    if (value.TryGet(\"{field.Name}\", out var {varName}))");
            sb.AppendLine($"{indent}    {{");
            EmitDeserializeAssignment(sb, field, varName, indent + "        ");
            sb.AppendLine($"{indent}    }}");

            // If field has FormerlySerializedAs, try those names as fallback
            for (int i = 0; i < field.FormerlySerializedAs.Count; i++)
            {
                var oldName = field.FormerlySerializedAs[i];
                var oldVarName = $"_old_{field.Name}_{i}";
                sb.AppendLine($"{indent}    else if (value.TryGet(\"{oldName}\", out var {oldVarName}))");
                sb.AppendLine($"{indent}    {{");
                EmitDeserializeAssignment(sb, field, oldVarName, indent + "        ");
                sb.AppendLine($"{indent}    }}");
            }
        }

        sb.AppendLine($"{indent}}}");
    }

    private static void GenerateFixedStructureSerializeMethod(StringBuilder sb, TypeToGenerate typeInfo, string indent)
    {
        sb.AppendLine($"{indent}public void Serialize(ref EchoObject compound, SerializationContext ctx)");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    var list = EchoObject.NewList();");

        foreach (var field in typeInfo.Fields)
        {
            var expr = EmitSerializeExpr(sb, field, indent + "    ");
            sb.AppendLine($"{indent}    list.ListAdd({expr});");
        }

        sb.AppendLine($"{indent}    compound = list;");
        sb.AppendLine($"{indent}}}");
    }

    private static void GenerateFixedStructureDeserializeMethod(StringBuilder sb, TypeToGenerate typeInfo, string indent)
    {
        sb.AppendLine($"{indent}public void Deserialize(EchoObject value, SerializationContext ctx)");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    if (value.TagType != EchoType.List)");
        sb.AppendLine($"{indent}        throw new System.InvalidOperationException(\"Expected list for fixed structure deserialization\");");
        sb.AppendLine();
        sb.AppendLine($"{indent}    var listValue = (System.Collections.Generic.List<EchoObject>)value.Value!;");
        sb.AppendLine();
        sb.AppendLine($"{indent}    if (listValue.Count != {typeInfo.Fields.Count})");
        sb.AppendLine($"{indent}        throw new System.InvalidOperationException($\"Field count mismatch. Expected {typeInfo.Fields.Count} but got {{listValue.Count}}\");");
        sb.AppendLine();

        for (int i = 0; i < typeInfo.Fields.Count; i++)
        {
            var field = typeInfo.Fields[i];
            EmitDeserializeAssignment(sb, field, $"listValue[{i}]", indent + "    ");
        }

        sb.AppendLine($"{indent}}}");
    }

    #endregion

    private const string AttributeSource = @"// <auto-generated/>
namespace Prowl.Echo
{
    /// <summary>
    /// Marks a class or struct for automatic ISerializable implementation via source generation.
    /// The generator will create optimized Serialize and Deserialize methods based on the type's fields.
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    public sealed class GenerateSerializerAttribute : System.Attribute
    {
    }
}";

    private record TypeToGenerate(
        string TypeName,
        string FullTypeName,
        string? Namespace,
        bool IsPartial,
        bool IsStruct,
        bool IsFixedStructure,
        List<FieldToSerialize> Fields
    );

    private record FieldToSerialize(
        string Name,
        string TypeName,
        bool HasIgnoreOnNull,
        List<string> SerializeIfConditions,
        List<string> FormerlySerializedAs,
        FieldTypeCategory Category,
        string? ElementTypeName,
        FieldTypeCategory ElementCategory
    );
}
