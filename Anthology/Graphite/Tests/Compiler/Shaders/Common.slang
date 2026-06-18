// Imported by Modules.slang. Owns the `lighting` constant buffer, so a program that imports this
// module gains a block variable that is declared outside its own (base) module. The reflector must
// still surface `lighting` in the program layout.
module Common;

public struct Lighting
{
    float4 sunDirection;
    float4 sunColor;
    float ambientIntensity;
}

public ConstantBuffer<Lighting> lighting;

public float3 shade(float3 albedo, float3 normal)
{
    float ndl = max(0.0, dot(normalize(normal), normalize(lighting.sunDirection.xyz)));
    return albedo * (lighting.ambientIntensity + ndl) * lighting.sunColor.rgb;
}
