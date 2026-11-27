#version 460

layout(set=0, binding=0) uniform sampler2D tex2d;

layout(location=0) out vec4 fragColor;

void main() {
    vec2 uv = gl_FragCoord.xy * 0.001;
    vec3 p3 = vec3(uv, 2.0);
    vec4 p4 = vec4(uv, 0.0, 2.0);
    vec4 a = textureProj(tex2d, p3);
    vec4 b = textureProj(tex2d, p4);
    fragColor = a + b;
}
