using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Runs all registered post-process steps in canonical order, executing each whose
/// <see cref="IPostProcess.Flag"/> appears in <see cref="ImportContext.Settings"/>.
/// </summary>
internal sealed class Pipeline
{
    private readonly ImportContext _context;
    private readonly List<IPostProcess> _orderedSteps;

    public Pipeline(ImportContext context)
    {
        _context = context;
        _orderedSteps = BuildCanonicalOrder();
    }

    public void Run(IntermediateScene scene)
    {
        var flags = _context.Settings.PostProcess;
        foreach (var step in _orderedSteps)
        {
            if ((flags & step.Flag) == 0)
                continue;

            _context.CancellationToken.ThrowIfCancellationRequested();
            try
            {
                step.Execute(scene, _context);
            }
            catch (ImportException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ImportException(
                    $"Post-process step '{step.Name}' threw: {ex.Message}",
                    _context.SourcePath,
                    _context.Format,
                    ex);
            }
        }
    }

    /// <summary>
    /// Canonical execution order. Earlier steps massage raw topology, then geometry transforms run,
    /// then dedup, then skin/weight work, then graph + mesh optimizers, then validation.
    /// </summary>
    private static List<IPostProcess> BuildCanonicalOrder() => new()
    {
        // Topology cleanup first so later steps see only valid faces.
        new TriangulateStep(),
        new RemoveDegeneratesStep(),
        new FindInvalidDataStep(),
        new SortByPrimitiveTypeStep(),

        // Geometry transforms.
        new GlobalScaleStep(),
        new FlipUVsStep(),
        new FlipWindingOrderStep(),
        new ConvertCoordinateSystemStep(),

        // Per-vertex computations + dedup.
        new CalcTangentSpaceStep(),
        new JoinIdenticalVerticesStep(),

        // Skin / animation finalize.
        new LimitBoneWeightsStep(),
        new DeboneStep(),
        new SplitByBoneCountStep(),
        new PopulateSkeletonsStep(),

        // Graph / mesh optimizers.
        new OptimizeMeshesStep(),
        new OptimizeGraphStep(),
        new SplitLargeMeshesStep(),
        new ImproveCacheLocalityStep(),

        // Bookkeeping.
        new EmbedTexturesStep(),
        new GenerateBoundsStep(),
        new ValidateDataStructureStep(),
    };
}
