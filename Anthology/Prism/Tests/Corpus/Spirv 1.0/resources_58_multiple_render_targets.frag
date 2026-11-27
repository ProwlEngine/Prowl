#version 450
layout(location = 0) in vec3 vNormal;
layout(location = 1) in vec3 vAlbedo;
layout(location = 2) in vec3 vWorldPos;
layout(location = 0) out vec4 oAlbedo;
layout(location = 1) out vec4 oNormal;
layout(location = 2) out vec4 oPosition;
void main() {
    oAlbedo = vec4(vAlbedo, 1.0);
    oNormal = vec4(normalize(vNormal) * 0.5 + 0.5, 1.0);
    oPosition = vec4(vWorldPos, 1.0);
}
