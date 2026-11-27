#version 450
layout(set = 0, binding = 0, std430) readonly buffer Instances {
    mat4 transforms[];
} instances;
layout(location = 0) in vec3 aPosition;
layout(location = 0) out flat int vInstance;
void main() {
    vInstance = gl_InstanceIndex;
    mat4 m = instances.transforms[gl_InstanceIndex];
    gl_Position = m * vec4(aPosition, 1.0);
}
