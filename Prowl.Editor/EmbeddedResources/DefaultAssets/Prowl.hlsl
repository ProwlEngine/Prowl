#ifndef SHADER_PROWL
#define SHADER_PROWL

float2 OctWrap(float2 v)
{
    float2 signVec = float2(1.0, 1.0);
    signVec.x = v.x < 0.0 ? -1.0 : 1.0;
    signVec.y = v.y < 0.0 ? -1.0 : 1.0;
    return (1.0 - abs(v.yx)) * signVec;
}

float2 EncodeNormal(float3 n)
{
    n /= (abs(n.x) + abs(n.y) + abs(n.z));
    n.xy = n.z < 0.0 ? OctWrap(n.xy) : n.xy;
    n.xy = n.xy * 0.5 + 0.5;
    return n.xy;
}

float3 DecodeNormal(float2 f)
{
    f = f * 2.0 - 1.0;
    // https://twitter.com/Stubbesaurus/status/937994790553227264
    float3 n = float3(f.x, f.y, 1.0 - abs(f.x) - abs(f.y));
    float t = saturate(-n.z); // Using saturate instead of clamp for HLSL
    
    float2 signVec;
    signVec.x = n.x < 0.0 ? -1.0 : 1.0;
    signVec.y = n.y < 0.0 ? -1.0 : 1.0;
    
    n.xy += signVec * t;
    return normalize(n);
}

#endif
