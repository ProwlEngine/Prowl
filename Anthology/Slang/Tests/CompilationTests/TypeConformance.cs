namespace Prowl.Slang.Test;

// Test the compilation API for adding type conformances.

public class TypeConformance
{
    [Fact]
    [DisplayTestMethodName]
    public void TypeConformanceTest()
    {
        string userSourceBody =
"""
struct SurfaceInteraction
{
};

__generic<T> struct InterfacePtr
{
    T* dptr;
};

struct BsdfSample
{
    float3 wo;
    float pdf;
    bool delta;
    float3 spectrum;
};

interface IBsdf
{
    BsdfSample sample(SurfaceInteraction si, float2 uv);
};

struct Diffuse : IBsdf
{
    float3 _reflectance;

    BsdfSample sample(SurfaceInteraction si, float2 uv)
    {
        BsdfSample sample;
        sample.wo = float3(uv, 1.0f);
        sample.pdf = uv.x;
        sample.delta = false;
        sample.spectrum = _reflectance;
        return sample;
    }
};

interface IShape
{
    property InterfacePtr<IBsdf> bsdf;
};

struct Mesh : IShape
{
    InterfacePtr<IBsdf> bsdf;
};

struct Sphere : IShape
{
    InterfacePtr<IBsdf> bsdf;
};

[[vk::push_constant]] IShape* shapes;
struct Path
{
    float3 sample(IShape* shapes)
    {
        float3 spectrum = { 0.0f, 0.0f, 0.0f };
        float3 throughput = { 1.0f, 1.0f, 1.0f };

        while (true)
        {
            SurfaceInteraction si = { };

            if (true)
            {
                const float p = min(max(throughput.r, max(throughput.g, throughput.b)), 0.95f);
                if (1.0f >= p) return spectrum;
            }

            BsdfSample sample = shapes[0].bsdf.dptr.sample(si, float2(1.0f));
            throughput *= sample.spectrum;
        }
        return spectrum;
    }
};

[[vk::binding(0, 0)]] RWTexture2D<float4> output;

[shader("compute"), numthreads(1, 1, 1)]
void computeMain()
{
    Path path = Path();
    float3 spectrum = path.sample(nullptr);
    output[uint2(0, 0)] += float4(spectrum, 1.0f);
}
""";

        TargetDescription targetDesc = new()
        {
            Format = CompileTarget.Spirv,
            Profile = GlobalSession.FindProfile("spirv_1_5"),

            CompilerOptionEntries =
            [
                new()
                {
                    Name = CompilerOptionName.Optimization,
                    Value = new()
                    {
                        Kind = CompilerOptionValueKind.Int,
                        IntValue0 = 0
                    }
                }
            ]
        };

        SessionDescription sessionDesc = new()
        {
            Targets = [targetDesc],
        };

        Session session = GlobalSession.CreateSession(sessionDesc);

        Module module = session.LoadModuleFromSourceString("m", "m.slang", userSourceBody, out _);

        EntryPoint entryPoint = module.FindAndCheckEntryPoint("computeMain", ShaderStage.Compute, out _);

        ShaderReflection layout = module.GetLayout();

        TypeReflection diffuse = layout.FindTypeByName("Diffuse");
        TypeReflection ibsdf = layout.FindTypeByName("IBsdf");
        TypeReflection ishape = layout.FindTypeByName("IShape");
        TypeReflection mesh = layout.FindTypeByName("Mesh");
        TypeReflection sphere = layout.FindTypeByName("Sphere");

        ComponentType diffuseIBsdf = session.CreateTypeConformanceComponentType(
            diffuse,
            ibsdf,
            0, out _);
        ComponentType meshIShape = session.CreateTypeConformanceComponentType(
            mesh,
            ishape,
            0, out _);

        ComponentType sphereIShape = session.CreateTypeConformanceComponentType(
            sphere,
            ishape,
            0, out _);

        ComponentType composedProgram = session.CreateCompositeComponentType(
            [module, entryPoint, diffuseIBsdf, meshIShape, sphereIShape], out _);

        ComponentType linkedProgram = composedProgram.Link(out _);

        Memory<byte> code = linkedProgram.GetTargetCode(0, out _);

        Assert.NotEqual(0, code.Length);
    }
}
