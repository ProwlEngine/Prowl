module ComputeFill3DTexture;

// Fills a 3D storage texture: every texel at depth z gets FillValue * (z + 1). Exercises a
// read-write 3D texture and a uniform scalar bound together through a PropertySet.

struct Globals
{
    float FillValue;
    float _padding1;
    float _padding2;
    float _padding3;

    RWTexture3D<float4> TextureToFill;
};
ParameterBlock<Globals> Resources;

[shader("compute")]
[numthreads(16, 16, 1)]
void compute(uint3 id : SV_DispatchThreadID)
{
    float value = Resources.FillValue * (id.z + 1);
    Resources.TextureToFill[id] = float4(value, value, value, value);
}
