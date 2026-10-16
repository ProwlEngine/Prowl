#version 460 core

// sampler2DMS texelFetch with sample index, and textureSamples
layout(location = 0) out vec4 FragColor;

layout(binding = 0) uniform sampler2DMS tex_ms;

void main() {
    ivec2 coord = ivec2(gl_FragCoord.xy);
    vec4 sample0 = texelFetch(tex_ms, coord, 0);

    int sample_count = textureSamples(tex_ms);

    FragColor = sample0 * float(sample_count);
}
