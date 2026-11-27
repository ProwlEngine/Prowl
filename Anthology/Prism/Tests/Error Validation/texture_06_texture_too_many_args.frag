// texture is given a fourth argument so it is called with too many arguments
#version 450
layout(location=0) out vec4 outColor;
layout(binding=0) uniform sampler2D tex;
layout(location=0) in vec2 uv;
layout(location=1) in float bias;
void main() {
    outColor = texture(tex, uv, bias, 2.0);
}
