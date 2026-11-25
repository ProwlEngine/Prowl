using System;
using System.Collections.Generic;
using System.Text;

using Prowl.Graphite.ShaderDef;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// How a deferred view lights the pixels of one kind of surface. A GBuffer pass names it by <see cref="Id"/>, written
/// through Graphite/GBuffer.glsl, and the view's lighting pass shades those pixels with its light function.
/// </summary>
public sealed class ShadingModel
{
    internal ShadingModel(string name, uint id, string? lighting, string? function, bool unlit)
    {
        Name = name;
        Id = id;
        Lighting = lighting;
        Function = function;
        Unlit = unlit;
    }

    public string Name { get; }

    /// <summary>What its GBuffer pass writes as the shading model, 1 to 15.</summary>
    public uint Id { get; }

    /// <summary>GLSL defining <see cref="Function"/>, null for a model lit as Default Lit is.</summary>
    public string? Lighting { get; }

    /// <summary>
    /// The model's light function, vec3 (PBR_Closure c, Graphite_LightContribution l, vec4 custom), custom the four
    /// values its GBuffer pass kept. It is what Graphite_BRDF is to a forward pass.
    /// </summary>
    public string? Function { get; }

    /// <summary>Whether its pixels take no light and show their emission alone.</summary>
    public bool Unlit { get; }
}

/// <summary>
/// The shading models of a renderer, and the one lighting program its deferred views draw, written from them: each
/// pixel is lit by the light function of its own model. Registering, replacing or removing a model writes the program
/// anew, compiled as the renderer's <see cref="CompilePolicy"/> says, and views keep the last one until it is ready.
/// </summary>
public sealed class ShadingModels : IDisposable
{
    public const uint DefaultLit = 1, Unlit = 2, TwoSidedFoliage = 3;

    /// <summary>The highest id the GBuffer holds.</summary>
    public const uint MaxId = 15;

    private const int RetireFrames = 8;

    private readonly GraphicsDevice _device;
    private readonly IShaderCompiler? _compiler;
    private readonly CompilePolicy _policy;
    private readonly BlendStateDescription _blend;
    private readonly object _lock = new();
    private readonly List<ShadingModel> _models = new();
    private readonly List<(FullscreenProgram Program, int Frames)> _retired = new();
    private FullscreenProgram? _pending;
    private int _version, _built;

    internal ShadingModels(GraphicsDevice device, IShaderCompiler? compiler, CompilePolicy policy)
    {
        _device = device;
        _compiler = compiler;
        _policy = policy;
        BlendAttachmentDescription written = BlendAttachmentDescription.OverrideBlend;
        BlendAttachmentDescription kept = written with { ColorWriteMask = ColorWriteMask.None };
        _blend = BlendStateDescription.SingleOverrideBlend with { AttachmentStates = [written, kept, kept, kept, written] };

        _models.Add(new ShadingModel("DefaultLit", DefaultLit, null, null, false));
        _models.Add(new ShadingModel("Unlit", Unlit, null, null, true));
        _models.Add(new ShadingModel("TwoSidedFoliage", TwoSidedFoliage, "#include \"Graphite/Foliage/FoliageLight.glsl\"\n", "Foliage_DeferredLight", false));
        Program = Create(Source());
    }

    /// <summary>Told of a lighting program that fails to compile, which leaves the last one in use.</summary>
    internal Action<RendererError>? Report { get; set; }

    /// <summary>The lighting program in use. Render thread.</summary>
    internal FullscreenProgram Program { get; private set; }

    /// <summary>The models as of this call. Host thread.</summary>
    public ShadingModel[] All
    {
        get
        {
            lock (_lock)
                return [.. _models];
        }
    }

    public ShadingModel? Find(string name)
    {
        lock (_lock)
            return _models.Find(model => model.Name == name);
    }

    /// <summary>
    /// Adds a model, or replaces the one of the same name keeping its id, and has the lighting program written anew.
    /// lighting is GLSL defining function, see <see cref="ShadingModel.Function"/>. It may use everything
    /// Graphite/PBR/Closure.glsl gives. Host thread.
    /// </summary>
    public ShadingModel Register(string name, string lighting, string function)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(lighting);
        ArgumentException.ThrowIfNullOrEmpty(function);
        lock (_lock)
        {
            int existing = _models.FindIndex(model => model.Name == name);
            uint id = existing >= 0 ? _models[existing].Id : FreeId();
            ShadingModel model = new(name, id, lighting, function, false);
            if (existing >= 0)
                _models[existing] = model;
            else
                _models.Add(model);
            _version++;
            return model;
        }
    }

    /// <summary>Removes a model a host registered. Its pixels are then lit as Default Lit. Host thread.</summary>
    public bool Remove(string name)
    {
        lock (_lock)
        {
            int index = _models.FindIndex(model => model.Name == name && model.Id > TwoSidedFoliage);
            if (index < 0)
                return false;
            _models.RemoveAt(index);
            _version++;
            return true;
        }
    }

    private uint FreeId()
    {
        for (uint id = TwoSidedFoliage + 1; id <= MaxId; id++)
        {
            if (!_models.Exists(model => model.Id == id))
                return id;
        }
        throw new InvalidOperationException($"Every one of the {MaxId} shading model ids is taken.");
    }

    /// <summary>
    /// Writes the program anew when the models changed, and takes it into use once it is compiled. True when
    /// <see cref="Program"/> changed. Render thread, once a frame.
    /// </summary>
    internal bool Update()
    {
        for (int i = _retired.Count - 1; i >= 0; i--)
        {
            if (_retired[i].Frames > 1)
            {
                _retired[i] = (_retired[i].Program, _retired[i].Frames - 1);
                continue;
            }
            _retired[i].Program.Dispose();
            _retired.RemoveAt(i);
        }

        string? source = null;
        lock (_lock)
        {
            if (_built != _version)
            {
                _built = _version;
                source = Source();
            }
        }
        if (source != null)
        {
            _pending?.Dispose();
            _pending = null;
            try
            {
                _pending = Create(source);
            }
            catch (Exception e)
            {
                Report?.Invoke(new RendererError("shader.compile", $"Graphite/DeferredLighting: {e.Message}", RendererErrorSeverity.Error, this) { Key = $"shadingmodels.{_built}" });
            }
        }
        if (_pending == null || !_pending.Ready(1))
            return false;
        _retired.Add((Program, RetireFrames));
        Program = _pending;
        _pending = null;
        return true;
    }

    private FullscreenProgram Create(string source)
        => FullscreenProgram.Create(_device, _compiler, source, "Graphite/DeferredLighting", source: false, blend: _blend, lit: true, depth: true, policy: _policy, report: Report);

    // The head, each model's light function, the two functions that pick by the pixel's model, then the pass itself
    private string Source()
    {
        StringBuilder text = new();
        StringBuilder light = new();
        StringBuilder unlit = new("false");
        text.Append("#include \"Graphite/Internal/DeferredLightingHead.glsl\"\n");
        foreach (ShadingModel model in _models)
        {
            if (model.Unlit)
                unlit.Append($" || model == {model.Id}u");
            if (model.Lighting == null || model.Function == null)
                continue;
            text.Append(model.Lighting).Append('\n');
            light.Append($"    if (graphite_DeferredModel == {model.Id}u)\n        return {model.Function}(c, l, graphite_DeferredCustom);\n");
        }
        text.Append("vec3 Graphite_BRDF(PBR_Closure c, Graphite_LightContribution l)\n{\n").Append(light).Append("    return PBR_EvaluateLight(c, l);\n}\n");
        text.Append($"bool graphite_DeferredUnlit(uint model) {{ return {unlit}; }}\n");
        text.Append("#include \"Graphite/Internal/DeferredLighting.glsl\"\n");
        return text.ToString();
    }

    public void Dispose()
    {
        Program.Dispose();
        _pending?.Dispose();
        foreach ((FullscreenProgram program, _) in _retired)
            program.Dispose();
        _retired.Clear();
    }
}
