#ifndef SHADER_RANDOM
#define SHADER_RANDOM

uint triple32(uint x)
{
    // https://nullprogram.com/blog/2018/07/31/
    x ^= x >> 17;
    x *= 0xed5ad4bbu;
    x ^= x >> 11;
    x *= 0xac4c1b51u;
    x ^= x >> 15;
    x *= 0x31848babu;
    x ^= x >> 14;
    return x;
}

static uint randState = triple32(uint(SV_Position.x + Resolution.x * SV_Position.y) + uint(Resolution.x * Resolution.y) * Frame);
uint RandNext() { return randState = triple32(randState); }
uint2 RandNext2() { return uint2(RandNext(), RandNext()); }
uint3 RandNext3() { uint2 xy = RandNext2(); return uint3(xy.x, xy.y, RandNext()); }
uint4 RandNext4() { uint3 xyz = RandNext3(); return uint4(xyz.x, xyz.y, xyz.z, RandNext()); }
float RandNextF() { return float(RandNext()) / float(0xffffffffu); }
float2 RandNext2F() { return float2(RandNext2()) / float(0xffffffffu); }
float3 RandNext3F() { return float3(RandNext3()) / float(0xffffffffu); }
float4 RandNext4F() { return float4(RandNext4()) / float(0xffffffffu); }
float RandF(uint seed) { return float(triple32(seed)) / float(0xffffffffu); }
float2 Rand2F(uint2 seed) { return float2(triple32(seed.x), triple32(seed.y)) / float(0xffffffffu); }

#endif
