// Criterion: a pass that imports ScopedAxis and reaches both axes through helper functions declared
// in that module, so both must be enumerated.
module ScopedAxisImporter;

import ScopedAxis;

struct ImporterOutput
{
    float4 clipPosition : SV_Position;
}

[shader("vertex")]
ImporterOutput vertex(float3 position : POSITION)
{
    ImporterOutput output;
    output.clipPosition = float4(position, 1);
    return output;
}

[shader("fragment")]
float4 fragment(ImporterOutput input) : SV_Target
{
    return ReadsMode(ReadsFlag(float4(1, 1, 1, 1)));
}
