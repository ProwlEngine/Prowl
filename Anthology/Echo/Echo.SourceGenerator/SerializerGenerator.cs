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

            fields.Add(new FieldToSerialize(
                Name: field.Name,
                TypeName: field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""),
                HasIgnoreOnNull: hasIgnoreOnNull,
                SerializeIfConditions: serializeIfAttrs!,
                FormerlySerializedAs: formerlySerializedAs!
            ));
        }

        return fields;
    }

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
            // Check if we need to wrap in conditions
            bool needsNullCheck = field.HasIgnoreOnNull;
            bool hasSerializeIf = field.SerializeIfConditions.Count > 0;

            if (needsNullCheck || hasSerializeIf)
            {
                var conditions = new List<string>();

                if (needsNullCheck)
                {
                    conditions.Add($"{field.Name} != null");
                }

                foreach (var condition in field.SerializeIfConditions)
                {
                    conditions.Add(condition);
                }

                sb.AppendLine($"{indent}    if ({string.Join(" && ", conditions)})");
                sb.AppendLine($"{indent}    {{");
                sb.AppendLine($"{indent}        compound.Add(\"{field.Name}\", Serializer.Serialize(typeof({field.TypeName}), {field.Name}, ctx));");
                sb.AppendLine($"{indent}    }}");
            }
            else
            {
                sb.AppendLine($"{indent}    compound.Add(\"{field.Name}\", Serializer.Serialize(typeof({field.TypeName}), {field.Name}, ctx));");
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
            // Try to deserialize from current field name
            sb.AppendLine($"{indent}    if (value.TryGet(\"{field.Name}\", out var _{field.Name}))");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        {field.Name} = ({field.TypeName})Serializer.Deserialize(_{field.Name}, typeof({field.TypeName}), ctx)!;");
            sb.AppendLine($"{indent}    }}");

            // If field has FormerlySerializedAs, try those names as fallback
            for (int i = 0; i < field.FormerlySerializedAs.Count; i++)
            {
                var oldName = field.FormerlySerializedAs[i];
                var varName = $"_old_{field.Name}_{i}";
                sb.AppendLine($"{indent}    else if (value.TryGet(\"{oldName}\", out var {varName}))");
                sb.AppendLine($"{indent}    {{");
                sb.AppendLine($"{indent}        {field.Name} = ({field.TypeName})Serializer.Deserialize({varName}, typeof({field.TypeName}), ctx)!;");
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
            sb.AppendLine($"{indent}    list.ListAdd(Serializer.Serialize(typeof({field.TypeName}), {field.Name}, ctx));");
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
            sb.AppendLine($"{indent}    {field.Name} = ({field.TypeName})Serializer.Deserialize(listValue[{i}], typeof({field.TypeName}), ctx)!;");
        }

        sb.AppendLine($"{indent}}}");
    }

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
        List<string> FormerlySerializedAs
    );
}
