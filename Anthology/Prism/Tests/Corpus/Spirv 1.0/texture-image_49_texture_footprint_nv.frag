#version 460 core
#extension GL_NV_shader_texture_footprint : require

// textureFootprintNV: returns a bool and fills a gl_TextureFootprint2DNV out struct.
layout(set = 0, binding = 0) uniform sampler2D tex;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main() {
    gl_TextureFootprint2DNV footprint;
    bool single = textureFootprintNV(tex, uv, 2, false, footprint);
    vec2 anchor = vec2(footprint.anchor);
    vec2 mask = vec2(footprint.mask);
    float lod = float(footprint.lod);
    outColor = single ? vec4(anchor, mask.x + lod, 1.0) : vec4(0.0);
}
