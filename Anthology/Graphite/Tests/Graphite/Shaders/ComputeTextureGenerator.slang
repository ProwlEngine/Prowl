module ComputeTextureGenerator;

// Writes a 4x1 RGBA texture: red, green, blue, white. A following full-screen blit samples it so
// a test can read the four texels back. Exercises a read-write storage texture in compute.

struct Globals
{
    RWTexture2D<float4> ComputeOutput;
};
ParameterBlock<Globals> Resources;

static const float4 Colors[4] =
{
    float4(1, 0, 0, 1),
    float4(0, 1, 0, 1),
    float4(0, 0, 1, 1),
    float4(1, 1, 1, 1),
};

[shader("compute")]
[numthreads(4, 1, 1)]
void compute(uint3 id : SV_DispatchThreadID)
{
    Resources.ComputeOutput[id.xy] = Colors[id.x];
}
