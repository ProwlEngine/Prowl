#version 460 core

layout(location=0) out vec4 FragColor;

void main()
{
    vec4 v = vec4(0.1, 0.2, 0.3, 0.4);

    // xyzw swizzles, lengths 1 to 4
    float a1 = v.x;
    vec2 a2 = v.xy;
    vec3 a3 = v.xyz;
    vec4 a4 = v.xyzw;

    // rgba swizzles, lengths 1 to 4
    float b1 = v.r;
    vec2 b2 = v.rg;
    vec3 b3 = v.rgb;
    vec4 b4 = v.rgba;

    // stpq swizzles, lengths 1 to 4
    float c1 = v.s;
    vec2 c2 = v.st;
    vec3 c3 = v.stp;
    vec4 c4 = v.stpq;

    vec4 sum = vec4(a1, a3) + vec4(b2, b2) * 0.0 + a3.x * 0.0;
    sum = a4 + vec4(b1, b3) * 0.0 + vec4(c1, c2, 0.0) * 0.0 + c3.x * 0.0 + c4 * 0.0;

    FragColor = sum;
}
