// texture on a sampler2D is given a float coordinate but it requires a vec2 coordinate
#version 450
layout(location=0) out vec4 outColor;
layout(binding=0) uniform sampler2D tex;
layout(location=0) in float u;
void main() {
    outColor = texture(tex, u);
}
