// texture is missing the required coordinate argument so it is called with too few arguments
#version 450
layout(location=0) out vec4 outColor;
layout(binding=0) uniform sampler2D tex;
void main() {
    outColor = texture(tex);
}
