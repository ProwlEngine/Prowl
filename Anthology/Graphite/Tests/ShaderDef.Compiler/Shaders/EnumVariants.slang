// Criterion: shaders with an enum-typed variant axis. A single enum axis (Lighting) consumed by the
// vertex stage, so the specialized permutations differ in their generated code. Combined with a bool
// axis (Shadows) to exercise a mixed variant space.
import VariantAttributes;

module EnumVariants;

public enum Lighting { None, Baked, Realtime }

[VariantAxis]
extern static const Lighting LightingMode;

[VariantAxis]
extern static const bool Shadows;

struct VertexInput
{
    float3 position : POSITION;
    float4 color    : COLOR;
}

struct VertexOutput
{
    float4 clipPosition : SV_Position;
    float4 color : COLOR;
}

[shader("vertex")]
VertexOutput vertex(VertexInput input)
{
    VertexOutput output;
    output.clipPosition = float4(input.position, 1);

    float4 color = input.color;

    switch (LightingMode)
    {
        case Lighting.Realtime: color *= 4.0; break;
        case Lighting.Baked:    color *= 2.0; break;
        default: break;
    }

    if (Shadows)
        color *= 0.5;

    output.color = color;
    return output;
}

[shader("fragment")]
float4 fragment(VertexOutput input) : SV_Target
{
    return input.color;
}
