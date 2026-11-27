#version 460

layout(set=0, binding=0) uniform sampler2D tex2d;
layout(set=0, binding=1) uniform samplerBuffer texBuf;
layout(set=0, binding=2) uniform sampler2DMS texMS;

layout(location=0) out vec4 fragColor;

void main() {
    ivec2 p = ivec2(gl_FragCoord.xy);
    int i = p.x & 255;
    vec4 a = texelFetch(tex2d, p, 2);
    vec4 b = texelFetch(texBuf, i);
    vec4 c = texelFetch(texMS, p, 3);
    fragColor = a + b + c;
}
