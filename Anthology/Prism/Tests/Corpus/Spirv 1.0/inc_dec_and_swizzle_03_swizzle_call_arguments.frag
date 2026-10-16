#version 460 core

// A swizzle is not code: it rides in the access chain until the load, so an argument's copy temp is
// created BEFORE the shuffle that fills it. This holds for an addressable base and an r-value alike,
// and for out/inout parameters the write-back goes per component.

layout(set = 0, binding = 0) uniform sampler2D tex;
layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 FragColor;

void takesVec2(vec2 v, out float sum) { sum = v.x + v.y; }
void modifies(inout vec2 v) { v = v * 2.0; }
void takesFloat(float f, out float doubled) { doubled = f * 2.0; }
void takesVec3(vec3 v, out float total) { total = v.x + v.y + v.z; }

void main()
{
    vec4 local = vec4(uv, 1.0, 2.0);
    float s1, s2, s3, s4, s5;

    takesVec2(local.xy, s1);            // addressable base, multi-component
    takesVec2(local.zw, s2);
    takesVec2(texture(tex, uv).xy, s3); // r-value base, multi-component
    takesFloat(texture(tex, uv).r, s4); // r-value base, single component
    takesVec3(local.xyz, s5);

    modifies(local.xy);                 // inout through a swizzle: per-component write-back
    modifies(local.zw);

    FragColor = local + vec4(s1, s2, s3, s4 + s5);
}
