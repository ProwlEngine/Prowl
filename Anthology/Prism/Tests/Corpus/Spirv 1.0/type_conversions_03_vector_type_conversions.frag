#version 460 core

layout(location = 0) out vec4 FragColor;

void main()
{
    ivec3 iv = ivec3(1, 2, 3);
    vec3 v = vec3(iv);

    vec3 v2 = vec3(1.5, 2.5, 3.5);
    ivec3 iv2 = ivec3(v2);

    uvec3 uv = uvec3(iv);
    bvec3 bv = bvec3(v2);

    vec4 v4 = vec4(1.0, 2.0, 3.0, 4.0);
    vec2 v2t = vec2(v4);
    vec4 v4e = vec4(v2, 1.0);

    float sum = v.x + float(iv2.x) + float(uv.x) + float(bv.x) + v2t.x + v4e.w;
    FragColor = vec4(sum * 0.01, 0.0, 0.0, 1.0);
}
