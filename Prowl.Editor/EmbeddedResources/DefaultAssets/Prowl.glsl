#ifndef SHADER_PROWL
#define SHADER_PROWL

vec2 OctWrap(vec2 v)
{
    vec2 signVec = vec2(1.0, 1.0);
    if (v.x < 0.0) signVec.x = -1.0;
    if (v.y < 0.0) signVec.y = -1.0;
    return (1.0 - abs(v.yx)) * signVec;
}

vec2 EncodeNormal(vec3 n)
{
    n /= (abs(n.x) + abs(n.y) + abs(n.z));
    if (n.z < 0.0)
        n.xy = OctWrap(n.xy);
    n.xy = n.xy * 0.5 + 0.5;
    return n.xy;
}

vec3 DecodeNormal(vec2 f)
{
    f = f * 2.0 - 1.0;

    // https://twitter.com/Stubbesaurus/status/937994790553227264
    vec3 n = vec3(f.x, f.y, 1.0 - abs(f.x) - abs(f.y));
    float t = clamp(-n.z, 0.0, 1.0);

    vec2 signVec = vec2(1.0, 1.0);
    if (n.x < 0.0) signVec.x = -1.0;
    if (n.y < 0.0) signVec.y = -1.0;
    
    n.xy += signVec * t;
    return normalize(n);
}

#endif