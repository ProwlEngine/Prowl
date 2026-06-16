using System;
using System.Collections.Generic;

using Prowl.Slang;

using SlangScalar = Prowl.Slang.ScalarType;


namespace Prowl.Graphite.Compiler;


// Shared reflection used by every Slang-backed platform compiler. Each target only differs by its
// compile format (held on the compiler's TargetDescription), so the stage and vertex-input
// reflection is identical across backends and lives here to avoid duplication.
//
// Pipeline state (blend / depth / rasterizer) and resource layouts are intentionally left at their
// defaults; those are owned by the user for now.
internal static class SlangReflector
{
    // entryPointNameOverride forces every stage's reported entry point name. Slang emits SPIR-V
    // entry points named "main" regardless of the source name, so the Vulkan target must override
    // to "main" for Graphite to resolve them; targets that preserve the name (e.g. HLSL) pass null.
    public static ShaderDescription BuildDescription(
        ComponentType linkedComponent, int layoutIndex, DiagnosticHandler handler, string? entryPointNameOverride = null)
    {
        ShaderReflection layout = linkedComponent.GetLayout(layoutIndex, out _);

        ShaderStageDescription[] stages = new ShaderStageDescription[layout.EntryPointCount];
        List<VertexLayoutDescription> vertexLayouts = [];

        // The layout's entry point ordering matches the indices accepted by GetEntryPointCode.
        for (uint i = 0; i < layout.EntryPointCount; i++)
        {
            EntryPointReflection ep = layout.GetEntryPointByIndex(i);

            Memory<byte> code = linkedComponent.GetEntryPointCode((nint)i, layoutIndex, out DiagnosticInfo diagnostics);
            handler.HandleCompilationDiagnostics(diagnostics);

            stages[i] = new ShaderStageDescription
            {
                Stage = ToGraphiteStage(ep.Stage),
                ShaderBytes = code.ToArray(),
                EntryPoint = entryPointNameOverride ?? ep.Name,
                Debug = false,
            };

            if (ep.Stage == ShaderStage.Vertex)
                ReflectVertexInputs(vertexLayouts, ep);
        }

        return new ShaderDescription
        {
            Stages = stages,
            BlendState = default,
            DepthStencilState = default,
            RasterizerState = default,
            VertexLayouts = [.. vertexLayouts],
            ResourceLayouts = [],
        };
    }


    static void ReflectVertexInputs(List<VertexLayoutDescription> layouts, EntryPointReflection ep)
    {
        foreach (VariableLayoutReflection p in ep.Parameters)
        {
            if (p.CategoryCount != 1)
                continue;

            if (p.GetCategoryByIndex(0) != ParameterCategory.VaryingInput)
                continue;

            AddVertexInputs(layouts, p);
        }
    }


    static void AddVertexInputs(List<VertexLayoutDescription> layouts, VariableLayoutReflection parameter)
    {
        if (parameter.Type.Kind == TypeKind.Struct)
        {
            foreach (VariableLayoutReflection field in parameter.TypeLayout.Fields)
                AddVertexInput(layouts, field.SemanticName, field.TypeLayout, field.BindingIndex);

            return;
        }

        AddVertexInput(layouts, parameter.SemanticName, parameter.TypeLayout, parameter.BindingIndex);
    }


    static void AddVertexInput(List<VertexLayoutDescription> layouts, string name, TypeLayoutReflection typeLayout, uint location)
    {
        if (!TryGetFormat(typeLayout, out VertexElementFormat format))
            return;

        layouts.Add(new VertexLayoutDescription(
            location,
            format.GetSizeInBytes(),
            new VertexElementDescription(name, format)));
    }


    static bool TryGetFormat(TypeLayoutReflection refl, out VertexElementFormat format)
    {
        if (refl.Kind == TypeKind.Scalar)
        {
            format = refl.ScalarType switch
            {
                SlangScalar.Int32 => VertexElementFormat.Int1,
                SlangScalar.UInt32 => VertexElementFormat.UInt1,
                SlangScalar.Float16 => VertexElementFormat.Half1,
                SlangScalar.Float32 => VertexElementFormat.Float1,
                _ => (VertexElementFormat)byte.MaxValue,
            };
            return format != (VertexElementFormat)byte.MaxValue;
        }

        if (refl.Kind == TypeKind.Vector)
        {
            format = (refl.ScalarType, refl.ColumnCount) switch
            {
                (SlangScalar.Float32, 2) => VertexElementFormat.Float2,
                (SlangScalar.Float32, 3) => VertexElementFormat.Float3,
                (SlangScalar.Float32, 4) => VertexElementFormat.Float4,

                (SlangScalar.Int32, 2) => VertexElementFormat.Int2,
                (SlangScalar.Int32, 3) => VertexElementFormat.Int3,
                (SlangScalar.Int32, 4) => VertexElementFormat.Int4,

                (SlangScalar.UInt32, 2) => VertexElementFormat.UInt2,
                (SlangScalar.UInt32, 3) => VertexElementFormat.UInt3,
                (SlangScalar.UInt32, 4) => VertexElementFormat.UInt4,

                (SlangScalar.Float16, 2) => VertexElementFormat.Half2,
                (SlangScalar.Float16, 4) => VertexElementFormat.Half4,

                (SlangScalar.Int8, 2) => VertexElementFormat.SByte2,
                (SlangScalar.Int8, 4) => VertexElementFormat.SByte4,

                (SlangScalar.UInt8, 2) => VertexElementFormat.Byte2,
                (SlangScalar.UInt8, 4) => VertexElementFormat.Byte4,

                (SlangScalar.Int16, 2) => VertexElementFormat.Short2,
                (SlangScalar.Int16, 4) => VertexElementFormat.Short4,

                (SlangScalar.UInt16, 2) => VertexElementFormat.UShort2,
                (SlangScalar.UInt16, 4) => VertexElementFormat.UShort4,

                _ => (VertexElementFormat)byte.MaxValue,
            };
            return format != (VertexElementFormat)byte.MaxValue;
        }

        format = default;
        return false;
    }


    static ShaderStages ToGraphiteStage(ShaderStage stage) =>
        stage switch
        {
            ShaderStage.Vertex => ShaderStages.Vertex,
            ShaderStage.Fragment => ShaderStages.Fragment,
            ShaderStage.Compute => ShaderStages.Compute,
            ShaderStage.Geometry => ShaderStages.Geometry,
            ShaderStage.Hull => ShaderStages.TessellationControl,
            ShaderStage.Domain => ShaderStages.TessellationEvaluation,
            _ => throw new NotSupportedException($"Unsupported shader stage: {stage}")
        };
}
