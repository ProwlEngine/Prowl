#version 460 core

layout(location=0) out vec4 FragColor;

void main()
{
    vec3 v = vec3(0.1, 0.2, 0.3);
    vec3 other = vec3(0.7, 0.8, 0.9);

    // component reordering write
    v.zyx = other.xyz;

    // partial component write
    v.xy = vec2(0.5, 0.6);

    vec4 w = vec4(0.0);
    w.rgba = vec4(v, 1.0);
    w.xy = vec2(0.25, 0.75);

    FragColor = w;
}
