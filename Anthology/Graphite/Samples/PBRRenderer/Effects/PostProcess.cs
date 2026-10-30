using System.Collections.Generic;

using Prowl.Graphite.Rendering;
using Prowl.Graphite.ShaderDef;
using Prowl.Vector;


namespace Prowl.Graphite.Samples.PBRRenderer;


internal sealed class BloomSettings
{
    public float Threshold = 1.2f;
    public float Intensity = 0.3f;
    public float Radius = 1.0f;
}


internal static class FullscreenPrograms
{
    public static GraphicsProgram Create(GraphicsDevice device, IShaderCompiler compiler, string fragment, string name)
    {
        string text = $"Vertex\n{{\n#define GRAPHITE_NO_SOURCE 1\n#include \"Graphite/Fullscreen.glsl\"\n}}\nFragment\n{{\n#define GRAPHITE_NO_SOURCE 1\n#include \"Graphite/Fullscreen.glsl\"\n{fragment}\n}}\n";
        ShaderPass pass = new() { State = new PassState(), Source = text, SourceName = name };
        ShaderDescription description = compiler.Compile(pass, [], device.BackendType);
        description.BlendState = BlendStateDescription.SingleOverrideBlend;
        description.DepthStencilState = DepthStencilStateDescription.Disabled;
        description.RasterizerState = RasterizerStateDescription.CullNone;
        GraphicsProgram program = device.ResourceFactory.CreateGraphicsProgram(description);
        program.Name = name;
        return program;
    }
}


internal sealed class BloomStage(BloomSettings settings, GraphicsProgram program, Sampler sampler, int order, string? input, string? add, string output, float scale, bool prefilter) : RenderFeature
{
    private readonly PropertySet _properties = new();
    private float _threshold;
    private float _radius;

    public override RenderSlot Slot => RenderSlot.HdrPost;
    public override int Order => order;

    public override void Extract()
    {
        _threshold = settings.Threshold;
        _radius = settings.Radius;
    }

    private sealed class ViewHandles
    {
        public TextureHandle Input;
        public TextureHandle Add;
        public TextureHandle Output;
    }

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        ViewHandles handles = builder.GetViewState<ViewHandles>();
        if (input == null)
            builder.ReadColor();
        else
            handles.Input = builder.Read(input);

        if (add != null)
            handles.Add = builder.Read(add);

        handles.Output = builder.Temporary(output, view.Texture(PixelFormat.R16_G16_B16_A16_Float, scale));
        builder.Publish(output, handles.Output);
    }

    public override void Render(FeatureContext context)
    {
        ViewHandles handles = context.GetViewState<ViewHandles>();
        Texture source = input == null ? context.Color! : context.Get(handles.Input).ColorTextures[0];
        CommandBuffer cmd = context.Command;

        cmd.SetFramebuffer(context.Get(handles.Output).Framebuffer, new TargetLoadStoreOps(new AttachmentOps(LoadAction.DontCare, StoreAction.Store), AttachmentOps.Loaded));
        cmd.SetFullViewport();
        cmd.SetShader(program);
        cmd.SetVertexSource(VertexSource.None);

        _properties.SetTexture("_Source", source, sampler);
        _properties.SetFloat2("_TexelSize", new Float2(1f / source.Width, 1f / source.Height));
        _properties.SetFloat("_Threshold", prefilter ? _threshold : -1f);
        _properties.SetFloat("_Radius", _radius);
        if (add != null)
            _properties.SetTexture("_Add", context.Get(handles.Add).ColorTextures[0], sampler);

        cmd.SetProperties(_properties);
        cmd.Draw(3);
    }
}


internal sealed class BloomComposite(BloomSettings settings, FullscreenProgram program, Sampler sampler, string input) : RenderFeature
{
    private readonly PropertySet _properties = new();
    private float _intensity;

    public override RenderSlot Slot => RenderSlot.HdrPost;
    public override int Order => 100;

    public override void Extract() => _intensity = settings.Intensity;

    private sealed class ViewHandles
    {
        public TextureHandle Bloom;
    }

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.GetViewState<ViewHandles>().Bloom = builder.Read(input);
        builder.WriteColor();
    }

    public override void Render(FeatureContext context)
    {
        _properties.SetTexture("_Bloom", context.Get(context.GetViewState<ViewHandles>().Bloom).ColorTextures[0], sampler);
        _properties.SetFloat("_Intensity", _intensity);
        context.Fullscreen(program, _properties);
    }
}


internal sealed class Bloom : System.IDisposable
{
    private const string Downsample = """
        uniform sampler2D _Source;
        uniform vec2 _TexelSize;
        uniform float _Threshold;

        vec3 Fetch(vec2 offset) { return texture(_Source, vUV + offset * _TexelSize).rgb; }

        void main()
        {
            vec3 a = Fetch(vec2(-2.0, 2.0)), b = Fetch(vec2(0.0, 2.0)), c = Fetch(vec2(2.0, 2.0));
            vec3 d = Fetch(vec2(-2.0, 0.0)), e = Fetch(vec2(0.0, 0.0)), f = Fetch(vec2(2.0, 0.0));
            vec3 g = Fetch(vec2(-2.0, -2.0)), h = Fetch(vec2(0.0, -2.0)), i = Fetch(vec2(2.0, -2.0));
            vec3 j = Fetch(vec2(-1.0, 1.0)), k = Fetch(vec2(1.0, 1.0)), l = Fetch(vec2(-1.0, -1.0)), m = Fetch(vec2(1.0, -1.0));
            vec3 color = e * 0.125 + (a + c + g + i) * 0.03125 + (b + d + f + h) * 0.0625 + (j + k + l + m) * 0.125;
            if (_Threshold >= 0.0)
            {
                float luma = max(max(color.r, color.g), color.b);
                float soft = clamp(luma - _Threshold + 0.5, 0.0, 1.0);
                soft = soft * soft * 0.5;
                color *= max(soft, luma - _Threshold) / max(luma, 1e-4);
            }
            outColor = vec4(min(color, vec3(64.0)), 1.0);
        }
        """;

    private const string Upsample = """
        uniform sampler2D _Source;
        uniform sampler2D _Add;
        uniform vec2 _TexelSize;
        uniform float _Radius;

        void main()
        {
            vec2 o = _TexelSize * _Radius;
            vec3 sum = texture(_Source, vUV + vec2(-o.x, o.y)).rgb + texture(_Source, vUV + vec2(o.x, o.y)).rgb
                + texture(_Source, vUV + vec2(-o.x, -o.y)).rgb + texture(_Source, vUV + vec2(o.x, -o.y)).rgb;
            sum += (texture(_Source, vUV + vec2(-o.x, 0.0)).rgb + texture(_Source, vUV + vec2(o.x, 0.0)).rgb
                + texture(_Source, vUV + vec2(0.0, o.y)).rgb + texture(_Source, vUV + vec2(0.0, -o.y)).rgb) * 2.0;
            sum += texture(_Source, vUV).rgb * 4.0;
            outColor = vec4(sum / 16.0 + texture(_Add, vUV).rgb, 1.0);
        }
        """;

    private const string Composite = """
        uniform sampler2D _Bloom;
        uniform float _Intensity;

        void main()
        {
            outColor = vec4(Graphite_SampleSource(vUV).rgb + texture(_Bloom, vUV).rgb * _Intensity, 1.0);
        }
        """;

    private const int Levels = 4;

    private readonly RenderFeature[] _features;
    private readonly GraphicsProgram _downsample;
    private readonly GraphicsProgram _upsample;
    private readonly FullscreenProgram _composite;
    private readonly Sampler _sampler;

    public Bloom(Renderer renderer, IShaderCompiler compiler)
    {
        GraphicsDevice device = renderer.Device;
        _sampler = device.ResourceFactory.CreateSampler(new SamplerDescription
        {
            AddressModeU = SamplerAddressMode.Clamp,
            AddressModeV = SamplerAddressMode.Clamp,
            AddressModeW = SamplerAddressMode.Clamp,
            MinFilter = FilterMode.Linear,
            MagFilter = FilterMode.Linear,
            MipFilter = FilterMode.Linear,
        });
        _downsample = FullscreenPrograms.Create(device, compiler, Downsample, "Bloom downsample");
        _upsample = FullscreenPrograms.Create(device, compiler, Upsample, "Bloom upsample");
        _composite = renderer.CreateFullscreenProgram(Composite, "Bloom composite");

        List<RenderFeature> features = new();
        int order = 0;
        for (int level = 0; level < Levels; level++)
        {
            features.Add(new BloomStage(Settings, _downsample, _sampler, order++, level == 0 ? null : $"Bloom.Down{level - 1}", null, $"Bloom.Down{level}", 0.5f / (1 << level), level == 0));
        }
        for (int level = Levels - 2; level >= 0; level--)
        {
            string source = level == Levels - 2 ? $"Bloom.Down{Levels - 1}" : $"Bloom.Up{level + 1}";
            features.Add(new BloomStage(Settings, _upsample, _sampler, order++, source, $"Bloom.Down{level}", $"Bloom.Up{level}", 0.5f / (1 << level), false));
        }
        features.Add(new BloomComposite(Settings, _composite, _sampler, "Bloom.Up0"));
        _features = features.ToArray();
    }

    public BloomSettings Settings { get; } = new();

    public bool Enabled
    {
        get => _features[0].Enabled;
        set
        {
            foreach (RenderFeature feature in _features)
                feature.Enabled = value;
        }
    }

    public void AddTo(FeatureSet set)
    {
        foreach (RenderFeature feature in _features)
            set.Add(feature);
    }

    public void Dispose()
    {
        _downsample.Dispose();
        _upsample.Dispose();
        _composite.Dispose();
        _sampler.Dispose();
    }
}
