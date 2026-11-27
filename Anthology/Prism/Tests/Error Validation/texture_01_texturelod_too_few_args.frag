// textureLod is missing the required lod argument so it is called with too few arguments
#version 450
layout(location=0) out vec4 outColor;
layout(binding=0) uniform sampler2D tex;
layout(location=0) in vec2 uv;
void main() {
    outColor = textureLod(tex, uv);
}
