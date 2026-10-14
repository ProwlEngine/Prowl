// constant_id can only apply to a scalar spec constant not a vector
#version 450

layout(constant_id = 0) const vec3 v = vec3(1.0);

layout(location = 0) out vec4 fragColor;

void main() {
    fragColor = vec4(v, 1.0);
}
