// texelFetch is missing the coordinate and lod arguments so it is called with too few arguments
#version 450
layout(location=0) out vec4 outColor;
layout(binding=0) uniform sampler2D tex;
void main() {
    outColor = texelFetch(tex);
}
