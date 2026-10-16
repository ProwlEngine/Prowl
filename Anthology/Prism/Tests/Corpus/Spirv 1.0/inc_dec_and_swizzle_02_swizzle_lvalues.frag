#version 460 core

// Swizzles on the left: identity (which glslang drops entirely), partial, reordered, and through a
// chain with indices. A single component stays in the access chain; a wider one shuffles.

layout(std140, set = 0, binding = 0) uniform Data
{
    vec4 rows[2];
    mat4 m;
} data;

layout(location = 0) in vec4 vIn;
layout(location = 0) out vec4 FragColor;

struct Nest { vec4 v; vec3 w; };

void main()
{
    vec4 a = vIn;
    vec3 b = vIn.xyz;
    Nest n;
    n.v = vIn;
    n.w = vIn.rgb;

    a.x = 1.0;                 // one component: an index in the chain
    a.xy = vec2(2.0, 3.0);     // partial: per component
    a.zw += vec2(0.5);
    a.wzyx = vIn.xyzw;         // reordered full: shuffle
    a.xyzw = vIn;              // identity: no swizzle at all
    b.xyz = vec3(1.0);         // identity on a vec3
    b.rgb *= 0.5;
    b.yx = b.xy;               // reordered partial

    n.v.y = 4.0;               // one component through a struct member chain
    n.v.xz = vec2(5.0);        // partial through a chain
    n.w.zyx = n.w.xyz;

    vec4 fromBlock = data.rows[1];
    fromBlock.x += data.m[2].w;
    fromBlock.yz = data.rows[0].xy;

    FragColor = a + vec4(b, 0.0) + n.v + vec4(n.w, 1.0) + fromBlock;
}
