#version 450
layout(set = 0, binding = 0) uniform sampler2D uAlbedo;
layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 oColor;
void main() {
    oColor = texture(uAlbedo, vUV);
}
