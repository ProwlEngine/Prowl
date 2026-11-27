// textureSize on a sampled 2D texture is missing the required lod argument so it is called with too few arguments
#version 450
layout(location=0) out vec4 outColor;
layout(binding=0) uniform sampler2D tex;
void main() {
    ivec2 sz = textureSize(tex);
    outColor = vec4(float(sz.x), float(sz.y), 0.0, 1.0);
}
