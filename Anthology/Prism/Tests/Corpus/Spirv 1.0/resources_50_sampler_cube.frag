#version 450
layout(set = 0, binding = 0) uniform samplerCube uEnv;
layout(location = 0) in vec3 vDir;
layout(location = 0) out vec4 oColor;
void main() {
    oColor = texture(uEnv, normalize(vDir));
}
