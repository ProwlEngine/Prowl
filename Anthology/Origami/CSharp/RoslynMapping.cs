// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Tags;

namespace Prowl.OrigamiUI.CSharp;

/// <summary>Translates Roslyn tags, classifications and symbols into the editor's kinds.</summary>
internal static class RoslynMapping
{
    private static readonly Dictionary<string, CodeTokenKind> s_classifications = new()
    {
        [ClassificationTypeNames.ClassName] = CodeTokenKind.Class,
        [ClassificationTypeNames.RecordClassName] = CodeTokenKind.Record,
        [ClassificationTypeNames.StructName] = CodeTokenKind.Struct,
        [ClassificationTypeNames.RecordStructName] = CodeTokenKind.Struct,
        [ClassificationTypeNames.InterfaceName] = CodeTokenKind.Interface,
        [ClassificationTypeNames.EnumName] = CodeTokenKind.Enum,
        [ClassificationTypeNames.DelegateName] = CodeTokenKind.Delegate,
        [ClassificationTypeNames.TypeParameterName] = CodeTokenKind.TypeParameter,
        [ClassificationTypeNames.MethodName] = CodeTokenKind.Method,
        [ClassificationTypeNames.ExtensionMethodName] = CodeTokenKind.ExtensionMethod,
        [ClassificationTypeNames.PropertyName] = CodeTokenKind.Property,
        [ClassificationTypeNames.FieldName] = CodeTokenKind.Field,
        [ClassificationTypeNames.ConstantName] = CodeTokenKind.Constant,
        [ClassificationTypeNames.EventName] = CodeTokenKind.Event,
        [ClassificationTypeNames.EnumMemberName] = CodeTokenKind.EnumMember,
        [ClassificationTypeNames.LocalName] = CodeTokenKind.Local,
        [ClassificationTypeNames.ParameterName] = CodeTokenKind.Parameter,
        [ClassificationTypeNames.NamespaceName] = CodeTokenKind.Namespace,
        [ClassificationTypeNames.LabelName] = CodeTokenKind.Label,
        [ClassificationTypeNames.ExcludedCode] = CodeTokenKind.ExcludedCode,
        [ClassificationTypeNames.Keyword] = CodeTokenKind.Keyword,
        [ClassificationTypeNames.ControlKeyword] = CodeTokenKind.ControlKeyword,
        [ClassificationTypeNames.StringLiteral] = CodeTokenKind.String,
        [ClassificationTypeNames.VerbatimStringLiteral] = CodeTokenKind.String,
        [ClassificationTypeNames.StringEscapeCharacter] = CodeTokenKind.StringEscape,
        [ClassificationTypeNames.NumericLiteral] = CodeTokenKind.Number,
        [ClassificationTypeNames.Comment] = CodeTokenKind.Comment,
        [ClassificationTypeNames.XmlDocCommentText] = CodeTokenKind.DocComment,
        [ClassificationTypeNames.XmlDocCommentComment] = CodeTokenKind.DocComment,
        [ClassificationTypeNames.XmlDocCommentCDataSection] = CodeTokenKind.DocComment,
        [ClassificationTypeNames.XmlDocCommentDelimiter] = CodeTokenKind.DocCommentTag,
        [ClassificationTypeNames.XmlDocCommentName] = CodeTokenKind.DocCommentTag,
        [ClassificationTypeNames.XmlDocCommentAttributeName] = CodeTokenKind.DocCommentTag,
        [ClassificationTypeNames.XmlDocCommentAttributeQuotes] = CodeTokenKind.DocCommentTag,
        [ClassificationTypeNames.XmlDocCommentAttributeValue] = CodeTokenKind.DocCommentTag,
        [ClassificationTypeNames.XmlDocCommentEntityReference] = CodeTokenKind.DocCommentTag,
        [ClassificationTypeNames.XmlDocCommentProcessingInstruction] = CodeTokenKind.DocCommentTag,
        [ClassificationTypeNames.PreprocessorKeyword] = CodeTokenKind.Preprocessor,
        [ClassificationTypeNames.PreprocessorText] = CodeTokenKind.PreprocessorText,
    };

    private static readonly Dictionary<string, CodeTokenKind> s_textTags = new()
    {
        [TextTags.Keyword] = CodeTokenKind.Keyword,
        [TextTags.Class] = CodeTokenKind.Class,
        [TextTags.Struct] = CodeTokenKind.Struct,
        [TextTags.RecordStruct] = CodeTokenKind.Struct,
        [TextTags.Record] = CodeTokenKind.Record,
        [TextTags.Interface] = CodeTokenKind.Interface,
        [TextTags.Enum] = CodeTokenKind.Enum,
        [TextTags.Delegate] = CodeTokenKind.Delegate,
        [TextTags.TypeParameter] = CodeTokenKind.TypeParameter,
        [TextTags.Method] = CodeTokenKind.Method,
        [TextTags.ExtensionMethod] = CodeTokenKind.ExtensionMethod,
        [TextTags.Property] = CodeTokenKind.Property,
        [TextTags.Field] = CodeTokenKind.Field,
        [TextTags.Constant] = CodeTokenKind.Constant,
        [TextTags.Event] = CodeTokenKind.Event,
        [TextTags.EnumMember] = CodeTokenKind.EnumMember,
        [TextTags.Local] = CodeTokenKind.Local,
        [TextTags.RangeVariable] = CodeTokenKind.Local,
        [TextTags.Parameter] = CodeTokenKind.Parameter,
        [TextTags.Namespace] = CodeTokenKind.Namespace,
        [TextTags.Label] = CodeTokenKind.Label,
        [TextTags.StringLiteral] = CodeTokenKind.String,
        [TextTags.NumericLiteral] = CodeTokenKind.Number,
        [TextTags.Punctuation] = CodeTokenKind.Punctuation,
        [TextTags.Operator] = CodeTokenKind.Operator,
    };

    private static readonly Dictionary<string, CodeSymbolKind> s_completionTags = new()
    {
        [WellKnownTags.Class] = CodeSymbolKind.Class,
        [WellKnownTags.Structure] = CodeSymbolKind.Struct,
        [WellKnownTags.Interface] = CodeSymbolKind.Interface,
        [WellKnownTags.Enum] = CodeSymbolKind.Enum,
        [WellKnownTags.EnumMember] = CodeSymbolKind.EnumMember,
        [WellKnownTags.Delegate] = CodeSymbolKind.Delegate,
        [WellKnownTags.Method] = CodeSymbolKind.Method,
        [WellKnownTags.ExtensionMethod] = CodeSymbolKind.ExtensionMethod,
        [WellKnownTags.Property] = CodeSymbolKind.Property,
        [WellKnownTags.Field] = CodeSymbolKind.Field,
        [WellKnownTags.Event] = CodeSymbolKind.Event,
        [WellKnownTags.Local] = CodeSymbolKind.Local,
        [WellKnownTags.RangeVariable] = CodeSymbolKind.Local,
        [WellKnownTags.Parameter] = CodeSymbolKind.Parameter,
        [WellKnownTags.Constant] = CodeSymbolKind.Constant,
        [WellKnownTags.TypeParameter] = CodeSymbolKind.TypeParameter,
        [WellKnownTags.Namespace] = CodeSymbolKind.Namespace,
        [WellKnownTags.Keyword] = CodeSymbolKind.Keyword,
        [WellKnownTags.Snippet] = CodeSymbolKind.Snippet,
        [WellKnownTags.Operator] = CodeSymbolKind.Operator,
        [WellKnownTags.Label] = CodeSymbolKind.Label,
    };

    public static bool TryClassification(string classification, out CodeTokenKind kind)
        => s_classifications.TryGetValue(classification, out kind);

    public static CodeTokenKind TextTag(string tag)
        => s_textTags.TryGetValue(tag, out var kind) ? kind : CodeTokenKind.Plain;

    public static CodeSymbolKind CompletionKind(ImmutableArray<string> tags)
    {
        foreach (var tag in tags)
            if (s_completionTags.TryGetValue(tag, out var kind)) return kind;
        return CodeSymbolKind.Text;
    }

    public static List<CodeTaggedText> Tagged(IEnumerable<TaggedText> parts)
    {
        var list = new List<CodeTaggedText>();
        foreach (var part in parts)
            list.Add(new CodeTaggedText(part.Text, TextTag(part.Tag)));
        return list;
    }

    public static CodeTokenKind DisplayPart(SymbolDisplayPartKind kind) => kind switch
    {
        SymbolDisplayPartKind.Keyword => CodeTokenKind.Keyword,
        SymbolDisplayPartKind.ClassName => CodeTokenKind.Class,
        SymbolDisplayPartKind.RecordClassName => CodeTokenKind.Record,
        SymbolDisplayPartKind.StructName or SymbolDisplayPartKind.RecordStructName => CodeTokenKind.Struct,
        SymbolDisplayPartKind.InterfaceName => CodeTokenKind.Interface,
        SymbolDisplayPartKind.EnumName => CodeTokenKind.Enum,
        SymbolDisplayPartKind.DelegateName => CodeTokenKind.Delegate,
        SymbolDisplayPartKind.TypeParameterName => CodeTokenKind.TypeParameter,
        SymbolDisplayPartKind.MethodName => CodeTokenKind.Method,
        SymbolDisplayPartKind.ExtensionMethodName => CodeTokenKind.ExtensionMethod,
        SymbolDisplayPartKind.PropertyName => CodeTokenKind.Property,
        SymbolDisplayPartKind.FieldName => CodeTokenKind.Field,
        SymbolDisplayPartKind.ConstantName => CodeTokenKind.Constant,
        SymbolDisplayPartKind.EventName => CodeTokenKind.Event,
        SymbolDisplayPartKind.EnumMemberName => CodeTokenKind.EnumMember,
        SymbolDisplayPartKind.LocalName or SymbolDisplayPartKind.RangeVariableName => CodeTokenKind.Local,
        SymbolDisplayPartKind.ParameterName => CodeTokenKind.Parameter,
        SymbolDisplayPartKind.NamespaceName => CodeTokenKind.Namespace,
        SymbolDisplayPartKind.LabelName => CodeTokenKind.Label,
        SymbolDisplayPartKind.StringLiteral => CodeTokenKind.String,
        SymbolDisplayPartKind.NumericLiteral => CodeTokenKind.Number,
        SymbolDisplayPartKind.Punctuation => CodeTokenKind.Punctuation,
        SymbolDisplayPartKind.Operator => CodeTokenKind.Operator,
        _ => CodeTokenKind.Plain,
    };

    public static CodeSymbolKind SymbolKind(ISymbol symbol) => symbol switch
    {
        INamespaceSymbol => CodeSymbolKind.Namespace,
        ITypeParameterSymbol => CodeSymbolKind.TypeParameter,
        INamedTypeSymbol t => t.TypeKind switch
        {
            TypeKind.Struct => CodeSymbolKind.Struct,
            TypeKind.Interface => CodeSymbolKind.Interface,
            TypeKind.Enum => CodeSymbolKind.Enum,
            TypeKind.Delegate => CodeSymbolKind.Delegate,
            _ => t.IsRecord ? CodeSymbolKind.Record : CodeSymbolKind.Class,
        },
        IMethodSymbol m => m.MethodKind switch
        {
            MethodKind.Constructor or MethodKind.StaticConstructor => CodeSymbolKind.Constructor,
            MethodKind.UserDefinedOperator or MethodKind.Conversion => CodeSymbolKind.Operator,
            _ => m.IsExtensionMethod ? CodeSymbolKind.ExtensionMethod : CodeSymbolKind.Method,
        },
        IPropertySymbol => CodeSymbolKind.Property,
        IFieldSymbol f => f.ContainingType?.TypeKind == TypeKind.Enum ? CodeSymbolKind.EnumMember
            : f.IsConst ? CodeSymbolKind.Constant : CodeSymbolKind.Field,
        IEventSymbol => CodeSymbolKind.Event,
        ILocalSymbol l => l.IsConst ? CodeSymbolKind.Constant : CodeSymbolKind.Local,
        IRangeVariableSymbol => CodeSymbolKind.Local,
        IParameterSymbol => CodeSymbolKind.Parameter,
        ILabelSymbol => CodeSymbolKind.Label,
        _ => CodeSymbolKind.Text,
    };
}
