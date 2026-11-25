using System;
using System.Collections.Generic;
using System.Text;

using Prowl.Graphite.ShaderDef;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// How a deferred view lights the pixels of one kind of surface. A GBuffer pass names it by <see cref="Id"/>, written
/// through Graphite/GBuffer.glsl, and the view's lighting pass lights those pixels as the model says.
/// </summary>
public sealed class ShadingModel
{
    internal ShadingModel(string name, uint id, string unpack, string lighting, bool unlit, ShadingModelDefinition? definition = null)
    {
        Definition = definition;
        Name = name;
        Id = id;
        Unpack = unpack;
        Lighting = lighting;
        Unlit = unlit;
    }

    public string Name { get; }

    /// <summary>The document it was loaded from, null for the ones the renderer writes itself.</summary>
    internal ShadingModelDefinition? Definition { get; }

    /// <summary>What its GBuffer pass writes as the shading model, 1 to 15.</summary>
    public uint Id { get; }

    /// <summary>
    /// GLSL defining void Model_Unpack(vec4 custom, inout PBR_Params p), run before the pixel's closure is prepared
    /// with the four values its GBuffer pass kept, or empty.
    /// </summary>
    public string Unpack { get; }

    /// <summary>
    /// GLSL defining vec3 Model_Light(PBR_Closure c, Graphite_LightContribution l, vec4 custom), vec3
    /// Model_Ambient(PBR_Closure c, PBR_Indirect i, vec4 custom) or both, in place of the PBR model's, or empty.
    /// </summary>
    public string Lighting { get; }

    /// <summary>Whether its pixels take no light and show their emission alone.</summary>
    public bool Unlit { get; }

    /// <summary>Whether the lights behind its pixels light them too.</summary>
    public bool LightWraps => Definition?.LightWraps ?? false;
}

/// <summary>
/// The surface models every renderer has: the library's shaders Lit, Unlit and Foliage, and the ShadingModel documents
/// among its files, Graphite/Standard/{Name}.shader, with the id each has in a GBuffer. The ids after them are a
/// renderer's own to give, see <see cref="ShadingModels"/>.
/// </summary>
internal static class ShadingModelLibrary
{
    public const uint Lit = 1, Unlit = 2, Foliage = 3;

    /// <summary>The highest id the GBuffer holds.</summary>
    public const uint MaxId = 15;

    /// <summary>The library's ShadingModel documents a GBuffer keeps, whose ids follow Foliage's in this order.</summary>
    public static readonly string[] Shipped = ["ClearCoat", "Cloth", "Subsurface", "Hair", "Eye"];

    /// <summary>The first id a renderer gives a model its host loads.</summary>
    public static uint FirstLoadedId => Foliage + (uint)Shipped.Length + 1;

    private static readonly object s_lock = new();
    private static readonly Dictionary<string, ShadingModelDefinition?> s_documents = new(StringComparer.Ordinal);

    /// <summary>The ShadingModel document the library holds under a name, null when it holds none.</summary>
    public static ShadingModelDefinition? Find(string name)
    {
        lock (s_lock)
        {
            if (s_documents.TryGetValue(name, out ShadingModelDefinition? found))
                return found;
        }
        InternalShaders.Register();
        ShadingModelDefinition? document = ShaderLibrary.Get($"{ShaderLibrary.Prefix}Standard/{name}.shader") is { } source && ShaderParser.IsShadingModel(source)
            ? ShaderParser.ParseShadingModel(source) : null;
        lock (s_lock)
            return s_documents[name] = document;
    }

    /// <summary>The GBuffer id of a model the library holds, 0 for one that is forward only or not the library's.</summary>
    public static uint IdOf(string name)
    {
        int shipped = Array.IndexOf(Shipped, name);
        return shipped >= 0 ? Foliage + 1 + (uint)shipped : name switch { "Lit" => Lit, "Unlit" => Unlit, "Foliage" => Foliage, _ => 0 };
    }
}

/// <summary>
/// The shading models of a renderer, the library's and the ones its host loaded, which only its own shaders build
/// on, and the one lighting program its deferred views draw, written from them: each pixel is lit as its own model says. It holds every model from the start, so nothing compiles as surfaces come into
/// view. Only loading or replacing a model writes it anew, compiled as the renderer's <see cref="CompilePolicy"/> says,
/// and views keep the last one until it is ready. A program that fails to compile is reported and leaves out the models
/// new to it, whose pixels are lit as Lit until a document that compiles replaces them. The models' code shares one
/// program, so a function a Lighting or Unpack block defines beside its hooks needs a name no other model uses.
/// </summary>
public sealed class ShadingModels : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly IShaderCompiler? _compiler;
    private readonly CompilePolicy _policy;
    private readonly BlendStateDescription _blend;
    private readonly object _lock = new();
    private readonly List<ShadingModel> _models = new();
    private readonly HashSet<ShadingModel> _broken = new();
    private readonly Dictionary<string, ShadingModelDefinition> _loaded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _ids = new(StringComparer.Ordinal);
    private readonly Action<RendererError>? _report;
    private FullscreenProgram? _pending;
    private ShadingModel[] _lit, _pendingModels = [];
    private string _source, _pendingSource = "";
    private int _version, _built;

    internal ShadingModels(GraphicsDevice device, IShaderCompiler? compiler, CompilePolicy policy, Action<RendererError>? report = null)
    {
        _device = device;
        _report = report;
        _compiler = compiler;
        _policy = policy;
        BlendAttachmentDescription written = BlendAttachmentDescription.OverrideBlend;
        _blend = BlendStateDescription.SingleOverrideBlend with { AttachmentStates = [written, written] };

        _models.Add(new ShadingModel("Lit", ShadingModelLibrary.Lit, "", "", false));
        _models.Add(new ShadingModel("Unlit", ShadingModelLibrary.Unlit, "", "", true));
        InternalShaders.Register();
        _models.Add(new ShadingModel("Foliage", ShadingModelLibrary.Foliage, "", ShaderLibrary.Get($"{ShaderLibrary.Prefix}Foliage/FoliageModel.glsl")!, false));
        foreach (string name in ShadingModelLibrary.Shipped)
        {
            if (ShadingModelLibrary.Find(name) is { } shipped)
                Add(shipped);
        }
        _source = Source(out _lit);
        Program = Create(_source, _report);
    }

    /// <summary>Takes a program views drew with until now, to dispose once no frame in flight draws with it.</summary>
    internal Action<IDisposable>? Retire { get; set; }

    /// <summary>The lighting program in use. Render thread.</summary>
    internal FullscreenProgram Program { get; private set; }

    /// <summary>The models with an id as of this call. Host thread.</summary>
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
    /// Takes a ShadingModel document: the renderer's shaders may then build on it with Surface "Name", and the lighting
    /// program is written anew with it. A document of a name already loaded, or of one of the library's documents,
    /// replaces it and keeps its id. Shaders loaded before keep the passes they were given. Its Base names a surface
    /// model shader, Graphite/Standard/{Base}.shader, not another ShadingModel. Returns the model, null for a forward
    /// only one. Host thread.
    /// </summary>
    public ShadingModel? Load(ShadingModelDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        string name = definition.Name;
        if (name is "Lit" or "Unlit" or "Foliage" or "Terrain")
            throw new ArgumentException($"{name} is a surface model the renderer ships, a ShadingModel takes another name.");
        InternalShaders.Register();
        string? source = ShaderLibrary.Get($"{ShaderLibrary.Prefix}Standard/{definition.Base}.shader");
        lock (_lock)
        {
            if (_loaded.ContainsKey(definition.Base) || (source != null && ShaderParser.IsShadingModel(source)))
                throw new ArgumentException($"The ShadingModel {name} is based on the ShadingModel {definition.Base}, its Base is a surface model shader such as Lit.");
            if (source == null)
                throw new ArgumentException($"The ShadingModel {name} is based on {definition.Base}, and there is no surface model shader of that name.");
            if (!definition.ForwardOnly && ShadingModelLibrary.IdOf(name) == 0 && !_ids.ContainsKey(name))
            {
                uint id = ShadingModelLibrary.FirstLoadedId + (uint)_ids.Count;
                if (id > ShadingModelLibrary.MaxId)
                    throw new InvalidOperationException($"Every one of the {ShadingModelLibrary.MaxId} shading model ids is taken.");
                _ids[name] = id;
            }
            _loaded[name] = definition;
            ShadingModel? model = Add(definition);
            _version++;
            return model;
        }
    }

    /// <summary>The document a Surface block of one of the renderer's shaders builds on by name, null when it names none. Any thread.</summary>
    internal ShadingModelDefinition? Definition(string name)
    {
        lock (_lock)
        {
            if (_loaded.TryGetValue(name, out ShadingModelDefinition? loaded))
                return loaded;
        }
        return ShadingModelLibrary.Find(name);
    }

    /// <summary>The GBuffer id of a model by name, 0 for one that is forward only or unknown. Any thread.</summary>
    internal uint IdOf(string name)
    {
        if (ShadingModelLibrary.IdOf(name) is > 0 and uint shipped)
            return shipped;
        lock (_lock)
            return _ids.GetValueOrDefault(name);
    }

    private ShadingModel? Add(ShadingModelDefinition definition)
    {
        _models.RemoveAll(model => model.Name == definition.Name);
        _broken.RemoveWhere(model => model.Name == definition.Name);
        if (definition.ForwardOnly)
            return null;
        ShadingModel model = new(definition.Name, IdOf(definition.Name), definition.Unpack, definition.Lighting, false, definition);
        _models.Add(model);
        return model;
    }

    /// <summary>
    /// Writes the program anew when the models changed, and takes it into use once it is compiled. True when
    /// <see cref="Program"/> changed. Render thread, once a frame.
    /// </summary>
    internal bool Update()
    {
        string? source = null;
        ShadingModel[] models = [];
        lock (_lock)
        {
            if (_built != _version)
            {
                _built = _version;
                source = Source(out models);
            }
        }
        if (source != null)
        {
            _pending?.Dispose();
            _pending = null;
            if (source != _source)
            {
                (_pendingSource, _pendingModels) = (source, models);
                try
                {
                    _pending = Create(source, null);
                }
                catch (Exception e)
                {
                    Fail(e.Message);
                }
            }
        }
        if (_pending == null)
            return false;

        // Every form the program in use draws with, so no view loses its lighting to the swap
        bool mono = Program.Built(1), layered = Program.Built(2);
        bool ready = (layered && !mono) || _pending.Ready(1);
        if (layered)
            ready &= _pending.Ready(2);
        if (_pending.Failed)
        {
            Fail(_pending.Error ?? "");
            _pending.Dispose();
            _pending = null;
            return false;
        }
        if (!ready)
            return false;
        if (Retire is { } retire)
            retire(Program);
        else
            Program.Dispose();
        Program = _pending;
        (_source, _lit) = (_pendingSource, _pendingModels);
        _pending = null;
        return true;
    }

    // The models new to the program that failed are left out of the next one
    private void Fail(string message)
    {
        List<string> names = new();
        lock (_lock)
        {
            foreach (ShadingModel model in _pendingModels)
            {
                if (Array.IndexOf(_lit, model) < 0 && _broken.Add(model))
                    names.Add(model.Name);
            }
            if (names.Count > 0)
                _version++;
        }
        string left = names.Count > 0 ? $", so these are lit as Lit until replaced: {string.Join(", ", names)}" : "";
        _report?.Invoke(new RendererError("shader.compile", $"Graphite/DeferredLighting: {message}{left}", RendererErrorSeverity.Error, this) { Key = $"shadingmodels.{_built}" });
    }

    private FullscreenProgram Create(string source, Action<RendererError>? report)
        => FullscreenProgram.Create(_device, _compiler, source, "Graphite/DeferredLighting", source: false, blend: _blend, lit: true, policy: _policy, report: report);

    // The head, each model's code under names of its own, the functions that pick by the pixel's model, then the pass
    // itself. No model adds to the closure, so a pixel pays for its own model's lighting alone.
    private string Source(out ShadingModel[] models)
    {
        StringBuilder text = new();
        text.Append("#include \"Graphite/Internal/DeferredLightingHead.glsl\"\n");

        StringBuilder unpack = new(), light = new(), ambient = new();
        StringBuilder unlit = new("false"), wraps = new("false"), back = new("false");
        models = _models.FindAll(model => !_broken.Contains(model)).ToArray();
        foreach (ShadingModel model in models)
        {
            if (model.Unlit)
                unlit.Append($" || model == {model.Id}u");
            if (model.LightWraps)
                wraps.Append($" || model == {model.Id}u");
            string code = model.Unpack + "\n" + model.Lighting;
            if (string.IsNullOrWhiteSpace(code))
                continue;
            string prefix = $"graphite_Model{model.Id}_";
            text.Append($"#define Model_Unpack {prefix}Unpack\n#define Model_Light {prefix}Light\n#define Model_Ambient {prefix}Ambient\n");
            text.Append(code).Append("\n#undef Model_Unpack\n#undef Model_Light\n#undef Model_Ambient\n");
            if (ShadingModelDefinition.DefinesFunction(model.Unpack, "void", "Model_Unpack"))
                unpack.Append($"    if (model == {model.Id}u)\n        {prefix}Unpack(custom, p);\n");
            if (ShadingModelDefinition.DefinesFunction(model.Lighting, "vec3", "Model_Light"))
            {
                back.Append($" || model == {model.Id}u");
                light.Append($"    if (graphite_DeferredModel == {model.Id}u)\n        return {prefix}Light(c, l, graphite_DeferredCustom);\n");
            }
            if (ShadingModelDefinition.DefinesFunction(model.Lighting, "vec3", "Model_Ambient"))
                ambient.Append($"    if (graphite_DeferredModel == {model.Id}u)\n        return {prefix}Ambient(c, i, graphite_DeferredCustom);\n");
        }
        text.Append("void graphite_DeferredUnpack(uint model, vec4 custom, inout PBR_Params p)\n{\n").Append($"    graphite_DeferredWraps = {wraps};\n    graphite_DeferredFront = !({back});\n").Append(unpack).Append("}\n");
        text.Append("vec3 Graphite_BRDF(PBR_Closure c, Graphite_LightContribution l)\n{\n").Append(light).Append("    return PBR_EvaluateLight(c, l);\n}\n");
        text.Append("vec3 Graphite_AmbientBRDF(PBR_Closure c, PBR_Indirect i)\n{\n").Append(ambient).Append("    return PBR_EvaluateAmbient(c, i);\n}\n");
        text.Append($"bool graphite_DeferredUnlit(uint model) {{ return {unlit}; }}\n");
        text.Append("#include \"Graphite/Internal/DeferredLighting.glsl\"\n");
        return text.ToString();
    }

    public void Dispose()
    {
        Program.Dispose();
        _pending?.Dispose();
    }
}
