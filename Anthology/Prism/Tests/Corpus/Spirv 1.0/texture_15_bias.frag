#version 460

layout(set=0, binding=0) uniform sampler2D tex2d;

layout(location=0) out vec4 fragColor;

void main() {
    vec2 uv = gl_FragCoord.xy * 0.001;
    float bias = gl_FragCoord.x * 0.001;
    vec4 a = texture(tex2d, uv, bias);
    fragColor = a;
}
