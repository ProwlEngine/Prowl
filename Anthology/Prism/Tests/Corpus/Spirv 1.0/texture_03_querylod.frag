#version 460

layout(set=0, binding=0) uniform sampler2D tex2d;

layout(location=0) out vec4 fragColor;

void main() {
    vec2 uv = gl_FragCoord.xy * 0.001;
    vec2 lod = textureQueryLod(tex2d, uv);
    fragColor = vec4(lod, 0.0, 1.0);
}
