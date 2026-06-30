using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Interface implemented by every post-process step. The pipeline calls <see cref="Execute"/>
/// in canonical order for each step whose <see cref="Flag"/> is set on the context.
/// </summary>
internal interface IPostProcess
{
    /// <summary>The single flag that gates this step.</summary>
    PostProcessFlags Flag { get; }

    /// <summary>Short name used in log entries.</summary>
    string Name { get; }

    /// <summary>Mutates <paramref name="scene"/> in place.</summary>
    void Execute(IntermediateScene scene, ImportContext context);
}
