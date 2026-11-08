using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Prowl.Analyzers;

/// <summary>
/// Flags comparing a live world (<c>SceneInstance</c>) with a scene asset (<c>Scene</c>), which compiles because both
/// are engine objects and is always false, and a plain <c>Scene</c> field on a component, which holds the scene and
/// everything it uses loaded for as long as the component lives where an <c>AssetRef&lt;Scene&gt;</c> only names it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SceneAnalyzer : DiagnosticAnalyzer
{
    public const string WorldComparedWithAssetId = "PROWLSC001";
    public const string HardSceneFieldId = "PROWLSC002";

    public const string SceneMetadataName = "Prowl.Runtime.Resources.Scene";
    public const string SceneInstanceMetadataName = "Prowl.Runtime.Resources.SceneInstance";
    private const string ComponentMetadataName = "Prowl.Runtime.Component";
    private const string SerializeFieldMetadataName = "Prowl.Echo.SerializeFieldAttribute";

    private static readonly DiagnosticDescriptor s_worldComparedWithAsset = new(
        WorldComparedWithAssetId,
        title: "A live world compared with a scene asset",
        messageFormat: "'{0}' is a live world and '{1}' is a scene asset, so they are never equal. Compare the world's Source with the asset instead.",
        category: "Correctness",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A SceneInstance is the running world and a Scene is the stored asset it was loaded from. They are different objects, so == is always false and != always true.");

    private static readonly DiagnosticDescriptor s_hardSceneField = new(
        HardSceneFieldId,
        title: "A component field holds a scene asset",
        messageFormat: "'{0}' holds the scene asset itself, so the scene and everything it uses stay loaded with this component. Use AssetRef<Scene> to name the scene without loading it.",
        category: "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A plain asset field is a hard reference: loading the component loads the scene, its own scene fields and every asset down the chain. AssetRef<Scene> is a soft reference, loaded only when asked.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(s_worldComparedWithAsset, s_hardSceneField);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            INamedTypeSymbol? scene = start.Compilation.GetTypeByMetadataName(SceneMetadataName);
            INamedTypeSymbol? world = start.Compilation.GetTypeByMetadataName(SceneInstanceMetadataName);
            if (scene is null || world is null) return;

            start.RegisterOperationAction(ctx =>
            {
                var op = (IBinaryOperation)ctx.Operation;
                if (op.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals)) return;
                IOperation left = Unwrap(op.LeftOperand), right = Unwrap(op.RightOperand);
                IOperation? worldSide = DerivesFrom(left.Type, world) && DerivesFrom(right.Type, scene) ? left
                    : DerivesFrom(right.Type, world) && DerivesFrom(left.Type, scene) ? right : null;
                if (worldSide is null) return;
                IOperation assetSide = ReferenceEquals(worldSide, left) ? right : left;
                ctx.ReportDiagnostic(Diagnostic.Create(s_worldComparedWithAsset, worldSide.Syntax.GetLocation(),
                    worldSide.Syntax.ToString(), assetSide.Syntax.ToString()));
            }, OperationKind.Binary);

            INamedTypeSymbol? component = start.Compilation.GetTypeByMetadataName(ComponentMetadataName);
            INamedTypeSymbol? serializeField = start.Compilation.GetTypeByMetadataName(SerializeFieldMetadataName);
            if (component is null) return;

            start.RegisterSymbolAction(ctx =>
            {
                var field = (IFieldSymbol)ctx.Symbol;
                if (field.IsStatic || field.IsConst || field.IsImplicitlyDeclared) return;
                if (!SymbolEqualityComparer.Default.Equals(field.Type.OriginalDefinition, scene)) return;
                if (!DerivesFrom(field.ContainingType, component)) return;
                bool serialized = field.DeclaredAccessibility == Accessibility.Public
                    || (serializeField is not null && HasAttribute(field, serializeField));
                if (!serialized || IsIgnored(field)) return;

                foreach (Location location in field.Locations)
                    ctx.ReportDiagnostic(Diagnostic.Create(s_hardSceneField, location, field.Name));
            }, SymbolKind.Field);
        });
    }

    private static IOperation Unwrap(IOperation operand)
    {
        while (operand is IConversionOperation { IsImplicit: true } conversion)
            operand = conversion.Operand;
        return operand;
    }

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attribute)
    {
        foreach (AttributeData data in symbol.GetAttributes())
            if (SymbolEqualityComparer.Default.Equals(data.AttributeClass, attribute))
                return true;
        return false;
    }

    private static bool IsIgnored(IFieldSymbol field)
    {
        foreach (AttributeData data in field.GetAttributes())
            if (data.AttributeClass?.Name is "SerializeIgnoreAttribute" or "NonSerializedAttribute")
                return true;
        return false;
    }

    private static bool DerivesFrom(ITypeSymbol? type, INamedTypeSymbol baseType)
    {
        for (ITypeSymbol? t = type; t is not null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(t, baseType))
                return true;
        return false;
    }
}
