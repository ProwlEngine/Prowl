#version 460

layout(set=0, binding=0) uniform sampler2D tex2d;

layout(location=0) out vec4 fragColor;

void main() {
    ivec2 p = ivec2(gl_FragCoord.xy);
    const ivec2 off = ivec2(3, 4);
    vec4 a = texelFetchOffset(tex2d, p, 0, off);
    fragColor = a;
}
