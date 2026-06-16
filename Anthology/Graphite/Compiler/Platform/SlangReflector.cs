using System;
using System.Collections.Generic;

using Prowl.Slang;

using SlangScalar = Prowl.Slang.ScalarType;


namespace Prowl.Graphite.Compiler;


internal static class SlangReflector
{
    public static ShaderDescription BuildDescription(
        ComponentType linkedComponent, int layoutIndex, DiagnosticHandler handler,
        string? entryPointNameOverride = null, bool bindsBySemantic = false)
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
                ReflectVertexInputs(vertexLayouts, ep, bindsBySemantic);
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


    static void ReflectVertexInputs(List<VertexLayoutDescription> layouts, EntryPointReflection ep, bool bindsBySemantic)
    {
        foreach (VariableLayoutReflection p in ep.Parameters)
        {
            if (p.CategoryCount != 1)
                continue;

            if (p.GetCategoryByIndex(0) != ParameterCategory.VaryingInput)
                continue;

            AddVertexInputs(layouts, p, bindsBySemantic);
        }
    }


    static void AddVertexInputs(List<VertexLayoutDescription> layouts, VariableLayoutReflection parameter, bool bindsBySemantic)
    {
        if (parameter.Type.Kind == TypeKind.Struct)
        {
            foreach (VariableLayoutReflection field in parameter.TypeLayout.Fields)
                AddVertexInput(layouts, field, bindsBySemantic);

            return;
        }

        AddVertexInput(layouts, parameter, bindsBySemantic);
    }


    static void AddVertexInput(List<VertexLayoutDescription> layouts, VariableLayoutReflection field, bool bindsBySemantic)
    {
        if (!TryGetFormat(field.TypeLayout, out VertexElementFormat format))
            return;

        string rawSemantic = field.SemanticName;
        uint semanticIndex = field.SemanticIndex;

        // User-visible semantic name is blended from raw semantic + index
        VertexElementDescription element = new($"{rawSemantic}{semanticIndex}", format);

        // d3d11 backend is funky so we use the raw semantic and its index directly there
        uint location;
        if (bindsBySemantic)
        {
            element.D3D11SemanticName = rawSemantic;
            location = semanticIndex;
        }
        else
        {
            location = field.BindingIndex;
        }

        layouts.Add(new VertexLayoutDescription(location, format.GetSizeInBytes(), element));
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
