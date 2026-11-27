#version 460

layout(set=0, binding=0) uniform sampler2D tex2d;

layout(location=0) out vec4 fragColor;

void main() {
    vec2 uv = gl_FragCoord.xy * 0.001;
    const ivec2 offs[4] = ivec2[4](
        ivec2(0, 0), ivec2(1, 0), ivec2(0, 1), ivec2(1, 1));
    vec4 g = textureGatherOffsets(tex2d, uv, offs);
    vec4 g1 = textureGatherOffsets(tex2d, uv, offs, 1);
    fragColor = g + g1;
}
