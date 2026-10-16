#version 450
layout(set = 0, binding = 0) uniform sampler2DArray uLayers;
layout(location = 0) in vec3 vUVW;
layout(location = 0) out vec4 oColor;
void main() {
    oColor = texture(uLayers, vUVW);
}
