#version 450
layout(set = 0, binding = 0) uniform CameraUBO {
    mat4 view;
    mat4 proj;
    vec3 cameraPos;
    float time;
} camera;
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 0) out vec3 vNormal;
void main() {
    vNormal = aNormal;
    gl_Position = camera.proj * camera.view * vec4(aPosition, 1.0);
}
