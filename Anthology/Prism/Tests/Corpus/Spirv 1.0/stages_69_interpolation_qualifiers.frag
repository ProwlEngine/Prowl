#version 450
layout(location = 0) flat in int vMaterialId;
layout(location = 1) noperspective in vec2 vScreenUV;
layout(location = 2) in vec3 vSmoothColor;
layout(location = 0) out vec4 oColor;
void main() {
    vec3 tint = (vMaterialId == 0) ? vec3(1.0, 0.5, 0.5) : vec3(0.5, 0.5, 1.0);
    oColor = vec4(vSmoothColor * tint + vec3(vScreenUV, 0.0) * 0.01, 1.0);
}
