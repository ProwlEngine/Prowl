#version 450
// An array of sampler2D at a single binding (descriptor / material-texture array),
// indexed dynamically by a per-fragment material id.
layout(set = 0, binding = 0) uniform sampler2D uTextures[8];
layout(location = 0) flat in int vMaterial;
layout(location = 1) in vec2 vUV;
layout(location = 0) out vec4 oColor;
void main() {
    oColor = texture(uTextures[vMaterial], vUV);
}
