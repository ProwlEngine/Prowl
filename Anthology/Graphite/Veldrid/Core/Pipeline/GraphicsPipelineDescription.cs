using System;

namespace Prowl.Veldrid;

/// <summary>
/// Describes a graphics <see cref="Pipeline"/>, for creation using a <see cref="ResourceFactory"/>.
/// Most pipeline state lives on the wrapped <see cref="ShaderProgram"/>; this description carries only the values that
/// the legacy pipeline path still owns (primitive topology and output description).
/// </summary>
public struct GraphicsPipelineDescription : IEquatable<GraphicsPipelineDescription>
{
    /// <summary>
    /// The <see cref="ShaderProgram"/> that owns the bulk of the pipeline state.
    /// </summary>
    public ShaderProgram Program;

    /// <summary>
    /// The <see cref="PrimitiveTopology"/> used by draw commands issued through this pipeline.
    /// </summary>
    public PrimitiveTopology PrimitiveTopology;

    /// <summary>
    /// A description of the output attachments used by the pipeline.
    /// </summary>
    public OutputDescription Outputs;

    /// <summary>
    /// Constructs a new <see cref="GraphicsPipelineDescription"/>.
    /// </summary>
    /// <param name="program">The <see cref="ShaderProgram"/> that owns pipeline state.</param>
    /// <param name="primitiveTopology">The primitive topology for draws.</param>
    /// <param name="outputs">The output attachments used by the pipeline.</param>
    public GraphicsPipelineDescription(
        ShaderProgram program,
        PrimitiveTopology primitiveTopology,
        OutputDescription outputs)
    {
        Program = program;
        PrimitiveTopology = primitiveTopology;
        Outputs = outputs;
    }

    /// <summary>
    /// Element-wise equality.
    /// </summary>
    public bool Equals(GraphicsPipelineDescription other)
    {
        return ReferenceEquals(Program, other.Program)
            && PrimitiveTopology == other.PrimitiveTopology
            && Outputs.Equals(other.Outputs);
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(
            Program?.GetHashCode() ?? 0,
            (int)PrimitiveTopology,
            Outputs);
    }
}
