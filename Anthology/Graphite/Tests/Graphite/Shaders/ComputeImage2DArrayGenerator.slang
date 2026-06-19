module ComputeImage2DArrayGenerator;

// Writes a flat per-layer grey value into every texel of a 2D-array storage texture. The value
// is (layer + 1) * floor(1 / layerCount), letting a test verify per-array-layer writes from
// compute. Dispatched with one z-group per array layer.

struct Globals
{
    RWTexture2DArray<float4> ComputeOutput;
};
ParameterBlock<Globals> Resources;

[shader("compute")]
[numthreads(32, 32, 1)]
void compute(uint3 id : SV_DispatchThreadID)
{
    uint width;
    uint height;
    uint layers;
    Resources.ComputeOutput.GetDimensions(width, height, layers);

    float sideColorStep = floor(1.0 / layers);
    Resources.ComputeOutput[id] = float4((id.z + 1) * sideColorStep);
}
