namespace Prowl.Graphite;

/// <summary>
/// Bindable resource controlling texture sampling in shaders.
/// </summary>
public abstract class Sampler : GraphicsResource
{
    private readonly SamplerDescription _description;

    internal Sampler(in SamplerDescription description)
    {
        _description = description;
    }

    /// <summary>
    /// Description this sampler was created with.
    /// </summary>
    public SamplerDescription Description => _description;
}
