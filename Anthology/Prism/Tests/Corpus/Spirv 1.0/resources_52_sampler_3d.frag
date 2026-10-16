#version 450
layout(set = 0, binding = 0) uniform sampler3D uVolume;
layout(location = 0) in vec3 vUVW;
layout(location = 0) out vec4 oColor;
void main() {
    oColor = texture(uVolume, vUVW);
}
