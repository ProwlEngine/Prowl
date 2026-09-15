// Criterion: a pass compiled in the same session as ScopedAxisImporter but importing nothing that
// declares an axis. It must enumerate zero axes rather than inheriting the session's other modules.
module ScopedAxisOutsider;

struct OutsiderOutput
{
    float4 clipPosition : SV_Position;
}

[shader("vertex")]
OutsiderOutput vertex(float3 position : POSITION)
{
    OutsiderOutput output;
    output.clipPosition = float4(position, 1);
    return output;
}

[shader("fragment")]
float4 fragment(OutsiderOutput input) : SV_Target
{
    return float4(1, 1, 1, 1);
}
